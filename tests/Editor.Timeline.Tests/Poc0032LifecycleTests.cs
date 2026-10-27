using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class Poc0032LifecycleTests
{
    [Fact]
    public void ImportHasNoCancelAndLibrarySwitchIsBlockedWhileBusy()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        var manager = new MediaImportCoordinator(first);
        var import = manager.BeginImport();
        Assert.Equal(first, manager.ActiveLibrary);
        Assert.Equal(new LibraryTransitionResult(false,
            "Aguarde o processamento ser finalizado"), manager.TryChangeLibrary(second));
        Assert.Equal(new LibraryTransitionResult(false,
            "Aguarde o processamento ser finalizado"), manager.TryChangeLibrary(null));
        Assert.Equal(1, manager.ActiveImports);
        manager.CompleteImport(import);
        Assert.True(manager.TryChangeLibrary(second).Allowed);
        Assert.Equal(second, manager.ActiveLibrary);
        Assert.DoesNotContain(typeof(MediaImportCoordinator).GetMethods(),
            method => method.Name.Contains("CancelImport", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingDiskBlocksNewImportsAndKeepsTasksForTechnicalTermination()
    {
        var manager = new MediaImportCoordinator(Guid.NewGuid());
        var id = manager.BeginImport();
        Assert.Contains(id, manager.MarkStorageUnavailable());
        Assert.Throws<IOException>(manager.BeginImport);
        Assert.False(manager.TryChangeLibrary(Guid.NewGuid()).Allowed);
    }

    [Fact]
    public void RecoveryDiscardsOnlyIncompleteAndPreservesCommitted()
    {
        var root = Path.Combine(Path.GetTempPath(), "poc0032-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new MediaResultRecovery(root);
            var complete = Guid.NewGuid();
            var incomplete = Guid.NewGuid();
            File.WriteAllText(manager.Begin(complete), "completed");
            string preserved = manager.Commit(complete);
            File.WriteAllText(manager.Begin(incomplete), "partial");
            string unrelated = Path.Combine(root, "keep-user-document.txt");
            File.WriteAllText(unrelated, "untouched");
            var report = manager.Recover();
            Assert.Equal(new RecoveryResult(1, 1), report);
            Assert.Equal("completed", File.ReadAllText(preserved));
            Assert.Equal("untouched", File.ReadAllText(unrelated));
            Assert.False(File.Exists(Path.Combine(root, incomplete.ToString("N") + ".partial")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailureRevokesTokenAndCallsShutdown()
    {
        var library = Guid.NewGuid();
        var task = Guid.NewGuid();
        var media = Guid.NewGuid();
        var grants = new AccessGrantRegistry(library);
        var grant = grants.Issue(task, library, media, MediaAccessOperation.ReadChunk, TimeSpan.FromMinutes(1));
        var request = new WorkerRequest(WorkerRequest.CurrentVersion, Guid.NewGuid(),
            task, library, media, grant, MediaAccessOperation.ReadChunk);
        bool terminated = false, exited = false;
        var supervisor = new WorkerFailureSupervisor(grants,
            () => { terminated = true; return Task.CompletedTask; },
            () => { exited = true; return Task.CompletedTask; });
        Assert.Equal("Ocorreu uma falha e o aplicativo precisará ser finalizado",
            await supervisor.HandleFailureAsync(task));
        Assert.Equal(AccessDecision.Revoked, grants.Authorize(request));
        Assert.True(terminated && exited);
        Assert.Equal(TimeSpan.FromSeconds(30), WorkerFailureSupervisor.MaximumShutdown);
    }
}
