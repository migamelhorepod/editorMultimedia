using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class IsolatedMediaProcessingSecurityBddTests
{
    [Fact]
    public void GivenUnregisteredOrExternalMedia_WhenWorkerProcessingRequested_ThenNoWorkerIsLaunched()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "editor-worker-auth-" + Guid.NewGuid().ToString("N"));
        string library = Path.Combine(root, "library");
        Directory.CreateDirectory(library);
        try
        {
            string external = Path.Combine(root, "outside.wav");
            string inside = Path.Combine(library, "inside.wav");
            File.WriteAllBytes(external, [1, 2, 3, 4]);
            File.WriteAllBytes(inside, [1, 2, 3, 4]);
            Guid libId = Guid.NewGuid();
            var registry = new AccessGrantRegistry(libId);
            var catalog = new AuthorizedMediaCatalog(library, registry);
            Guid task = Guid.NewGuid(), resource = Guid.NewGuid();
            Assert.Throws<UnauthorizedAccessException>(() => catalog.Register(resource, external));
            catalog.Register(resource, inside);
            Guid grant = registry.Issue(task, libId, resource,
                MediaAccessOperation.InspectMetadata, TimeSpan.FromMinutes(1));
            var request = new WorkerRequest(WorkerRequest.CurrentVersion,
                Guid.NewGuid(), task, libId, resource, grant, MediaAccessOperation.InspectMetadata);
            // All checks occur before resolving an executable or spawning FFmpeg.
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsAppContainerLauncher.RunAuthorizedMetadataProcessing(
                    "missing-worker.exe", catalog, request with { LibraryId = Guid.NewGuid() }));
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsAppContainerLauncher.RunAuthorizedDecodeProcessing(
                    "missing-worker.exe", catalog, request));
            registry.RevokeTask(task);
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsAppContainerLauncher.ReadAuthorizedMetadataTicks(
                    "missing-worker.exe", catalog, request));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(inside));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(external));
        }
        finally { Directory.Delete(root, true); }
    }
}
