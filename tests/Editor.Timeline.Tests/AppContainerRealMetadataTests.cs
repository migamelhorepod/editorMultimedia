using System.Diagnostics;
using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class AppContainerRealMetadataTests
{
    [Fact]
    public void GivenScopedRealWav_WhenProcessed_ThenFfprobeRunsOnlyInsideVerifiedAppContainer()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string worker = Path.Combine(root, "TestResults", "sandbox-worker", "Editor.Security.Worker.exe");
        Assert.True(File.Exists(worker));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(worker)!, "ffprobe.exe")));
        string folder = Path.Combine(Path.GetTempPath(), "editor-real-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string wav = Path.Combine(folder, "audio.wav");
        try
        {
            // 1.2 seconds, deliberately different from the old 0.1s test-only fixture.
            const int sampleCount = 9600;
            using (var stream = File.Create(wav))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + sampleCount);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1);
                writer.Write((short)1); writer.Write(8000);
                writer.Write(8000); writer.Write((short)1);
                writer.Write((short)8);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(sampleCount);
                writer.Write(Enumerable.Repeat((byte)128, sampleCount).ToArray());
            }
            var sid = WindowsAppContainerPreflight.DerivePackageSid();
            var start = new ProcessStartInfo("icacls.exe") { UseShellExecute = false };
            start.ArgumentList.Add(wav);
            start.ArgumentList.Add("/grant");
            start.ArgumentList.Add("*" + sid + ":R");
            using var acl = Process.Start(start)!;
            Assert.True(acl.WaitForExit(5000));
            Assert.Equal(0, acl.ExitCode);

            Guid library = Guid.NewGuid(), task = Guid.NewGuid(), resource = Guid.NewGuid();
            var registry = new AccessGrantRegistry(library);
            var catalog = new AuthorizedMediaCatalog(folder, registry);
            catalog.Register(resource, wav);
            var grant = registry.Issue(task, library, resource,
                MediaAccessOperation.InspectMetadata, TimeSpan.FromMinutes(1));
            var request = new WorkerRequest(WorkerRequest.CurrentVersion, Guid.NewGuid(),
                task, library, resource, grant, MediaAccessOperation.InspectMetadata);
            Assert.Equal(0, WindowsAppContainerLauncher.RunAuthorizedMetadataProcessing(
                worker, catalog, request));
            registry.RevokeTask(task);
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsAppContainerLauncher.RunAuthorizedMetadataProcessing(worker, catalog, request));
        }
        finally { Directory.Delete(folder, true); }
    }
}
