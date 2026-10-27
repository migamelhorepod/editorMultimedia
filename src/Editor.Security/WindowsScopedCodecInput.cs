using System.ComponentModel;
using System.Diagnostics;

namespace Editor.Security;

/// <summary>
/// Per-invocation, read-only AppContainer file authorization for a disposable
/// input copy. Never changes ACLs on the caller's original media file.
/// The copy remains controller-owned and must be disposed after the sandboxed
/// child job has exited. This is an interim single-file proof, not the final
/// zero-copy production pipeline for multi-gigabyte media.
/// </summary>
internal sealed class WindowsScopedCodecInput : IDisposable
{
    private readonly string stage;
    private bool disposed;
    public string Path => stage;

    public WindowsScopedCodecInput(string authorizedSource)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizedSource);
        if (!System.IO.Path.IsPathFullyQualified(authorizedSource) || !File.Exists(authorizedSource))
            throw new FileNotFoundException("Authorized media source is unavailable.", authorizedSource);
        if ((File.GetAttributes(authorizedSource) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Linked input files are prohibited.");

        var source = System.IO.Path.GetFullPath(authorizedSource);
        var folder = System.IO.Path.GetDirectoryName(source)
            ?? throw new IOException("Missing parent of authorized media.");
        var length = new FileInfo(source).Length;
        if (length <= 0 || length > 10L * 1024 * 1024 * 1024)
            throw new IOException("Media input exceeds single-file processing bounds.");

        stage = System.IO.Path.Combine(folder, ".editorworker-" +
            Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(source));
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
            {
                input.CopyTo(output, 1024 * 1024);
                output.Flush(flushToDisk: true);
            }

            // Grant only read on the expendable copy, to this one AppContainer
            // package SID. Never grant permissions on the original media.
            string sid = WindowsAppContainerPreflight.DerivePackageSid();
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("icacls.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(stage);
            process.StartInfo.ArgumentList.Add("/grant");
            process.StartInfo.ArgumentList.Add("*" + sid + ":R");
            if (!process.Start() || !process.WaitForExit(15000))
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new IOException("Could not establish scoped Windows file permission.");
            }
            if (process.ExitCode != 0)
                throw new Win32Exception("Windows denied the AppContainer's scoped read-only file grant.");
        }
        catch
        {
            try { if (File.Exists(stage)) File.Delete(stage); }
            catch (IOException) { /* Preserve original failure; staging requires maintenance. */ }
            catch (UnauthorizedAccessException) { /* Never erase source media. */ }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // The launcher synchronously finishes/kills the Job before reaching this
        // point, ensuring the worker cannot keep using the scoped file permission.
        if (File.Exists(stage)) File.Delete(stage);
    }
}
