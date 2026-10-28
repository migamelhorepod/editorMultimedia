using System.Diagnostics;
using System.Security.Cryptography;
using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

/// <summary>
/// BDD experiments only. These prove fd:0 seekability on real Windows
/// AppContainer/FFprobe when CI passes; they never enable production admission.
/// </summary>
public sealed class AppContainerSeekableFdTests
{
    [Fact]
    public void GivenAuthorizedWavWithoutSourceAclChange_WhenFdProbeRuns_ThenItReadsMetadataWithoutStaging()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = CreateFolder();
        string original = Path.Combine(root, "1.2-seconds.wav");
        try
        {
            WriteWav(original);
            RunCase(root, original, TimeSpan.FromSeconds(1.2));
        }
        finally { RemoveFolderEventually(root); }
    }

    [Fact]
    public void GivenMp4WithMoovAfterMdat_WhenFdProbeSeeks_ThenItReadsMetadataWithoutStaging()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = CreateFolder();
        string original = Path.Combine(root, "moov-at-end.mp4");
        try
        {
            string ffmpeg = Path.Combine(Path.GetDirectoryName(GetWorker())!, "ffmpeg.exe");
            Assert.True(File.Exists(ffmpeg));
            var command = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] {
                "-v", "error", "-f", "lavfi", "-i",
                "color=c=red:s=64x64:r=10", "-t", "1", "-c:v",
                "mpeg4", "-q:v", "5", "-y", original
            }) command.ArgumentList.Add(arg);
            using (var encoder = Process.Start(command)!)
            {
                var stdout = encoder.StandardOutput.ReadToEndAsync();
                var stderr = encoder.StandardError.ReadToEndAsync();
                Assert.True(encoder.WaitForExit(30000), "Trusted synthetic MP4 encoder timed out.");
                _ = stdout.GetAwaiter().GetResult();
                string details = stderr.GetAwaiter().GetResult();
                Assert.True(encoder.ExitCode == 0, details);
            }

            byte[] bytes = File.ReadAllBytes(original);
            int moov = FindFourCc(bytes, "moov");
            int mdat = FindFourCc(bytes, "mdat");
            Assert.True(mdat >= 0 && moov > mdat,
                "Fixture must require seek to read moov after mdat.");
            RunCase(root, original, TimeSpan.FromSeconds(1));
        }
        finally { RemoveFolderEventually(root); }
    }

    [Fact]
    public void GivenRevokedGrant_WhenFdProbeIsRequested_ThenNoWorkerGetsAnInputHandle()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = CreateFolder();
        string original = Path.Combine(root, "revoked.wav");
        try
        {
            WriteWav(original);
            var (catalog, registry, request) = Prepare(root, original);
            registry.RevokeTask(request.TaskId);
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsAppContainerLauncher.ProbeAuthorizedSeekableMetadataTicks(
                    GetWorker(), catalog, request));
            Assert.Empty(Directory.GetFiles(root, ".editorworker-*"));
        }
        finally { RemoveFolderEventually(root); }
    }

    private static void RunCase(string root, string original, TimeSpan duration)
    {
        string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
        string aclBefore = GetAcl(original);
        var (catalog, registry, request) = Prepare(root, original);
        long ticks = WindowsAppContainerLauncher.ProbeAuthorizedSeekableMetadataTicks(
            GetWorker(), catalog, request);
        Assert.InRange(ticks, duration.Ticks - TimeSpan.FromMilliseconds(120).Ticks,
            duration.Ticks + TimeSpan.FromMilliseconds(120).Ticks);
        Assert.Equal(originalHash,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original))));
        Assert.Equal(aclBefore, GetAcl(original));
        Assert.Empty(Directory.GetFiles(root, ".editorworker-*"));
        Assert.Equal(1, Directory.GetFiles(root).Length);
        registry.RevokeTask(request.TaskId);
    }

    private static (AuthorizedMediaCatalog catalog, AccessGrantRegistry registry,
        WorkerRequest request) Prepare(string root, string original)
    {
        Guid library = Guid.NewGuid(), resource = Guid.NewGuid(), task = Guid.NewGuid();
        var registry = new AccessGrantRegistry(library);
        var catalog = new AuthorizedMediaCatalog(root, registry);
        catalog.Register(resource, original);
        var grant = registry.Issue(task, library, resource,
            MediaAccessOperation.InspectMetadata, TimeSpan.FromMinutes(1));
        return (catalog, registry, new WorkerRequest(WorkerRequest.CurrentVersion,
            Guid.NewGuid(), task, library, resource, grant,
            MediaAccessOperation.InspectMetadata));
    }

    private static string GetAcl(string file)
    {
        var psi = new ProcessStartInfo("icacls.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add(file);
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        _ = p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(10000));
        Assert.Equal(0, p.ExitCode);
        return output;
    }

    private static string GetWorker()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        string worker = Path.Combine(root, "TestResults", "sandbox-worker",
            "Editor.Security.Worker.exe");
        Assert.True(File.Exists(worker));
        return worker;
    }

    private static string CreateFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(),
            "editor-seekable-fd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static int FindFourCc(byte[] bytes, string fourcc)
    {
        byte[] marker = System.Text.Encoding.ASCII.GetBytes(fourcc);
        for (int i = 0; i <= bytes.Length - marker.Length; ++i)
            if (bytes.AsSpan(i, marker.Length).SequenceEqual(marker)) return i;
        return -1;
    }

    private static void RemoveFolderEventually(string folder)
    {
        // Job termination is synchronous but the Windows kernel may still be
        // releasing inherited input HANDLEs. Avoid masking the real test
        // failure with a transient cleanup sharing violation.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { Directory.Delete(folder, recursive: true); return; }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static void WriteWav(string path)
    {
        const int samples = 9600;
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + samples);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(8000); w.Write(8000); w.Write((short)1); w.Write((short)8);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(samples);
        w.Write(Enumerable.Repeat((byte)128, samples).ToArray());
    }
}
