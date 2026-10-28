using System.ComponentModel;
using System.Diagnostics;

namespace Editor.Security;

/// <summary>
/// Controller-owned, per-invocation read-only AppContainer input copy.
/// The original media ACL is never modified. An exclusive lease persists
/// across a controller crash so only abandoned copies are reclaimed.
/// This staging design requires free space up to the input's full size;
/// it is NOT a zero-copy pipeline for 10 GB media.
/// </summary>
internal sealed class WindowsScopedCodecInput : IDisposable
{
    private const string Prefix = ".editorworker-";
    private const string InputPart = ".input";
    private const string LeasePart = ".lease";
    private readonly string stage;
    private readonly string leasePath;
    private FileStream? lease;
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
        if (source.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new UnauthorizedAccessException("Unsafe input path.");
        var folder = System.IO.Path.GetDirectoryName(source)
            ?? throw new IOException("Missing parent of authorized media.");
        var length = new FileInfo(source).Length;
        if (length <= 0 || length > 10L * 1024 * 1024 * 1024)
            throw new IOException("Media input exceeds single-file processing bounds.");

        // Fail before creating a partial copy on an already-full conventional HDD.
        // Free space may change while copying: the copy itself still fails closed.
        if (new DriveInfo(System.IO.Path.GetPathRoot(folder)!).AvailableFreeSpace < length)
            throw new IOException("Insufficient free space for isolated media staging.");

        string identity = Guid.NewGuid().ToString("N");
        stage = System.IO.Path.Combine(folder,
            Prefix + identity + InputPart + System.IO.Path.GetExtension(source));
        leasePath = System.IO.Path.Combine(folder, Prefix + identity + LeasePart);
        try
        {
            // The lease is held exclusively through codec execution and cleanup.
            // An app/process crash closes it, enabling next-start recovery.
            lease = new FileStream(leasePath, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 4096, FileOptions.WriteThrough);
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
            {
                input.CopyTo(output, 1024 * 1024);
                output.Flush(flushToDisk: true);
            }

            if (new FileInfo(stage).Length != length)
                throw new IOException("Isolated input copy is incomplete.");

            // Only grant the exact sandbox package SID read access to its copy.
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
            // Preserve the lease whenever cleanup fails, so recovery has a
            // precise marker instead of an untracked multi-gigabyte orphan.
            try { if (File.Exists(stage)) File.Delete(stage); }
            catch (IOException) { /* Do not overwrite original failure. */ }
            catch (UnauthorizedAccessException) { /* Recovered on next start. */ }
            lease?.Dispose();
            if (!File.Exists(stage))
            {
                try { File.Delete(leasePath); }
                catch (IOException) { /* Next-start recovery handles it. */ }
                catch (UnauthorizedAccessException) { /* Fail closed on recovery. */ }
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            // The Job is synchronously closed before disposal by the launcher.
            if (File.Exists(stage)) File.Delete(stage);
        }
        finally
        {
            lease?.Dispose();
            // If cleanup failed, retain the lease marker for safe recovery.
            if (!File.Exists(stage) && File.Exists(leasePath))
                File.Delete(leasePath);
        }
    }

    /// <summary>
    /// Reclaim only GUID-marked, unleased staging copies under an available
    /// active library. Never follow links, delete unrelated files or race
    /// with active worker tasks. Call before accepting new library tasks.
    /// </summary>
    public static int RecoverAbandoned(string libraryRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        if (!System.IO.Path.IsPathFullyQualified(libraryRoot))
            throw new ArgumentException("Absolute active library required.", nameof(libraryRoot));
        var root = System.IO.Path.GetFullPath(libraryRoot);
        if (!Directory.Exists(root) || IsLink(root))
            throw new UnauthorizedAccessException("Unavailable or redirected staging library.");
        int recovered = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? folder))
        {
            foreach (string marker in Directory.EnumerateFiles(folder,
                         Prefix + "*" + LeasePart, SearchOption.TopDirectoryOnly))
            {
                string name = System.IO.Path.GetFileName(marker);
                if (!TryLeaseIdentity(name, out Guid id)) continue;
                if (IsLink(marker))
                    throw new UnauthorizedAccessException("Linked staging lease is unsafe.");
                FileStream? exclusive;
                try
                {
                    exclusive = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
                {
                    continue; // Still held by a live worker/controller.
                }
                using (exclusive)
                {
                    string stem = Prefix + id.ToString("N") + InputPart;
                    var candidates = Directory.EnumerateFiles(folder, stem + "*",
                            SearchOption.TopDirectoryOnly)
                        .Where(p => IsOwnInputName(System.IO.Path.GetFileName(p), stem))
                        .ToArray();
                    if (candidates.Length > 1)
                        throw new InvalidDataException("Ambiguous isolated staging identity.");
                    foreach (var candidate in candidates)
                    {
                        if (IsLink(candidate))
                            throw new UnauthorizedAccessException("Linked staged input is unsafe.");
                        File.Delete(candidate);
                    }
                }
                File.Delete(marker);
                recovered++;
            }
            foreach (string subdirectory in Directory.EnumerateDirectories(folder, "*",
                         SearchOption.TopDirectoryOnly))
                if (!IsLink(subdirectory)) pending.Push(subdirectory);
        }
        return recovered;
    }

    private static bool TryLeaseIdentity(string name, out Guid id)
    {
        id = Guid.Empty;
        return name.StartsWith(Prefix, StringComparison.Ordinal) &&
            name.EndsWith(LeasePart, StringComparison.Ordinal) &&
            Guid.TryParseExact(name.AsSpan(Prefix.Length,
                name.Length - Prefix.Length - LeasePart.Length), "N", out id) &&
            name == Prefix + id.ToString("N") + LeasePart;
    }

    private static bool IsOwnInputName(string name, string stem)
    {
        if (!name.StartsWith(stem, StringComparison.Ordinal)) return false;
        string extension = name[stem.Length..];
        return extension.Length == 0 ||
            (extension.StartsWith('.', StringComparison.Ordinal) &&
             extension.IndexOf('.', 1) < 0);
    }

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
