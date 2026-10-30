using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class Poc0032LifecycleTests
{
    private sealed class SpyJob(Action terminate) : IDisposable
    {
        public void Dispose() => terminate();
    }

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
        Assert.Throws<IOException>(() => { manager.BeginImport(); });
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
    public void StorageDisappearanceClosesAffectedJobWithoutUserCancel()
    {
        var coordinator = new MediaImportCoordinator(Guid.NewGuid());
        var jobId = coordinator.BeginImport();
        bool present = true;
        var terminated = new List<Guid>();
        var monitor = new LibraryStorageMonitor(coordinator, Path.GetTempPath(),
            terminated.Add, _ => present);
        Assert.True(monitor.Check());
        present = false;
        Assert.False(monitor.Check());
        Assert.Contains(jobId, terminated);
        Assert.Equal(1, coordinator.ActiveImports);
        Assert.Throws<IOException>(() => { coordinator.BeginImport(); });
        present = true;
        Assert.True(monitor.Check());
        Assert.Throws<IOException>(() => { coordinator.BeginImport(); });
        coordinator.CompleteImport(jobId); // Confirm technical shutdown before new imports.
        Assert.NotEqual(Guid.Empty, coordinator.BeginImport());
    }

    [Fact]
    public void UnavailableLibraryCanBeReplacedOnlyAfterActiveOperationsEnd()
    {
        var manager = new MediaImportCoordinator(Guid.NewGuid());
        var taskId = manager.BeginImport();
        manager.MarkStorageUnavailable();
        Assert.False(manager.TryChangeLibrary(Guid.NewGuid()).Allowed);
        manager.CompleteImport(taskId);
        var replacement = Guid.NewGuid();
        Assert.True(manager.TryChangeLibrary(replacement).Allowed);
        Assert.Equal(replacement, manager.ActiveLibrary);
        Assert.NotEqual(Guid.Empty, manager.BeginImport());
    }

    [Fact]
    public void LibraryModalNeverChangesSelectionWhileImporting()
    {
        var library = Guid.NewGuid();
        var coordinator = new MediaImportCoordinator(library);
        var task = coordinator.BeginImport();
        var requested = coordinator.TryChangeLibrary(Guid.NewGuid());
        Assert.False(requested.Allowed);
        Assert.Equal("Aguarde o processamento ser finalizado", requested.PopupMessage);
        Assert.Equal(library, coordinator.ActiveLibrary);
        coordinator.CompleteImport(task);
        Assert.True(coordinator.TryChangeLibrary(Guid.NewGuid()).Allowed);
    }

    [Fact]
    public void ForcedChildProcessTerminationRecoversOnlyIncompleteResult()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "poc0032-forced-" + Guid.NewGuid().ToString("N"));
        var recovery = new MediaResultRecovery(root);
        Guid done = Guid.NewGuid(), pending = Guid.NewGuid();
        File.WriteAllText(recovery.Begin(done), "completed");
        recovery.Commit(done);
        string partial = recovery.Begin(pending);
        var info = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("[IO.File]::AppendAllText('" + partial.Replace("'", "''") + "', 'interrupted'); Start-Sleep -Seconds 20");
        using var child = System.Diagnostics.Process.Start(info)
            ?? throw new InvalidOperationException("Cannot start crash fixture.");
        try
        {
            Assert.True(SpinWait.SpinUntil(
                () => File.Exists(partial) && new FileInfo(partial).Length > 0,
                TimeSpan.FromSeconds(8)), "Crash fixture did not write partial result.");
            child.Kill(entireProcessTree: true);
            Assert.True(child.WaitForExit(5000));
            var recovered = new MediaResultRecovery(root).Recover();
            Assert.Equal(1, recovered.IncompleteDiscarded);
            Assert.Equal(1, recovered.CompletedPreserved);
            Assert.False(File.Exists(partial));
            Assert.True(File.Exists(Path.Combine(root, done.ToString("N") + ".complete")));
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StartupRecoveryRejectsCorruptedCompletedOutput()
    {
        string folder = Path.Combine(Path.GetTempPath(), "poc0032-integrity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recovery = new MediaResultRecovery(folder);
            Guid id = Guid.NewGuid();
            File.WriteAllText(recovery.Begin(id), "correct");
            string finished = recovery.Commit(id);
            File.WriteAllText(finished, "corrupted");
            var result = recovery.Recover();
            Assert.Equal(1, result.IncompleteDiscarded);
            Assert.Equal(0, result.CompletedPreserved);
            Assert.False(File.Exists(finished));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task WindowsJobTerminationClosesActiveWorkerWithinThirtySeconds()
    {
        if (!OperatingSystem.IsWindows()) return;
        var info = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Start-Sleep -Seconds 20");
        using var worker = System.Diagnostics.Process.Start(info)
            ?? throw new InvalidOperationException("Cannot launch worker fixture.");
        try
        {
            using var job = WindowsWorkerJob.Attach(worker);
            var registry = new AccessGrantRegistry(Guid.NewGuid());
            var supervisor = new WorkerFailureSupervisor(registry, job,
                () => Task.CompletedTask);
            var started = System.Diagnostics.Stopwatch.StartNew();
            await supervisor.HandleFailureAsync(Guid.NewGuid());
            Assert.True(worker.WaitForExit(5000), "Closing the Job must kill its worker.");
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
        }
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
            new SpyJob(() => terminated = true),
            () => { exited = true; return Task.CompletedTask; });
        Assert.Equal("Ocorreu uma falha e o aplicativo precisará ser finalizado",
            await supervisor.HandleFailureAsync(task));
        Assert.Equal(AccessDecision.Revoked, grants.Authorize(request));
        Assert.True(terminated && exited);
        Assert.Equal(TimeSpan.FromSeconds(30), WorkerFailureSupervisor.MaximumShutdown);
    }
}
