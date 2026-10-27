using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class AuthorizedMediaCatalogTests
{
    [Fact]
    public void ValidGrantOnlyMapsItsOwnActiveLibraryMedia()
    {
        string folder = Path.Combine(Path.GetTempPath(), "poc003-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string media = Path.Combine(folder, "allowed.wav");
        File.WriteAllBytes(media, new byte[] { 1, 2, 3 });
        try
        {
            var library = Guid.NewGuid();
            var task = Guid.NewGuid();
            var resource = Guid.NewGuid();
            var registry = new AccessGrantRegistry(library);
            var catalog = new AuthorizedMediaCatalog(folder, registry);
            catalog.Register(resource, media);
            var grant = registry.Issue(task, library, resource,
                MediaAccessOperation.InspectMetadata, TimeSpan.FromMinutes(1));
            var request = new WorkerRequest(WorkerRequest.CurrentVersion, Guid.NewGuid(),
                task, library, resource, grant, MediaAccessOperation.InspectMetadata);
            Assert.Equal(Path.GetFullPath(media), catalog.Resolve(request));
            Assert.Throws<UnauthorizedAccessException>(() => catalog.Resolve(
                request with { ResourceId = Guid.NewGuid() }));
            Assert.Throws<UnauthorizedAccessException>(() => catalog.Resolve(
                request with { Operation = MediaAccessOperation.ReadChunk }));
            registry.DeactivateLibrary();
            Assert.Throws<UnauthorizedAccessException>(() => catalog.Resolve(request));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void RealMetadataWorkerRejectsRevokedAndWrongOperationBeforeLaunch()
    {
        string folder = Path.Combine(Path.GetTempPath(), "appcontainer-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string media = Path.Combine(folder, "media.wav");
        File.WriteAllBytes(media, [1, 2, 3]);
        try
        {
            Guid library = Guid.NewGuid(), task = Guid.NewGuid(), resource = Guid.NewGuid();
            var registry = new AccessGrantRegistry(library);
            var catalog = new AuthorizedMediaCatalog(folder, registry);
            catalog.Register(resource, media);
            Guid grant = registry.Issue(task, library, resource,
                MediaAccessOperation.InspectMetadata, TimeSpan.FromMinutes(1));
            var request = new WorkerRequest(WorkerRequest.CurrentVersion, Guid.NewGuid(),
                task, library, resource, grant, MediaAccessOperation.InspectMetadata);
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsAppContainerLauncher.RunAuthorizedMetadataProcessing(
                    "nonexistent-worker.exe", catalog,
                    request with { Operation = MediaAccessOperation.ReadChunk }));
            registry.RevokeTask(task);
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsAppContainerLauncher.RunAuthorizedMetadataProcessing(
                    "nonexistent-worker.exe", catalog, request));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ParentEscapeAndLinkToAnotherLibraryCannotBeRegistered()
    {
        string folder = Path.Combine(Path.GetTempPath(), "poc003-root-" + Guid.NewGuid().ToString("N"));
        string other = Path.Combine(Path.GetTempPath(), "poc003-other-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(other);
        string secret = Path.Combine(other, "private.wav");
        File.WriteAllBytes(secret, new byte[] { 1, 2 });
        try
        {
            var catalog = new AuthorizedMediaCatalog(folder, new AccessGrantRegistry(Guid.NewGuid()));
            Assert.Throws<UnauthorizedAccessException>(() =>
                catalog.Register(Guid.NewGuid(), secret));
            string link = Path.Combine(folder, "shortcut.wav");
            File.CreateSymbolicLink(link, secret);
            Assert.Throws<UnauthorizedAccessException>(() =>
                catalog.Register(Guid.NewGuid(), link));
        }
        finally
        {
            Directory.Delete(folder, true);
            Directory.Delete(other, true);
        }
    }
}
