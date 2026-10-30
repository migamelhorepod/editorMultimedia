namespace Editor.Security;

/// <summary>
/// Handles technical faults, never voluntary cancellation of an import.
/// Dispose of the Job before notifying the UI: KILL_ON_JOB_CLOSE removes worker
/// processes and their open handles even when the worker is unresponsive.
/// </summary>
public sealed class WorkerFailureSupervisor
{
    public const string PopupMessage = MediaImportCoordinator.FatalMessage;
    public static readonly TimeSpan MaximumShutdown = TimeSpan.FromSeconds(30);

    private readonly AccessGrantRegistry registry;
    private readonly IDisposable workerJob;
    private readonly Func<Task> closeApplication;

    public WorkerFailureSupervisor(AccessGrantRegistry registry,
        IDisposable workerJob, Func<Task> closeApplication)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.workerJob = workerJob ?? throw new ArgumentNullException(nameof(workerJob));
        this.closeApplication = closeApplication ?? throw new ArgumentNullException(nameof(closeApplication));
    }

    public async Task<string> HandleFailureAsync(Guid taskId)
    {
        // No user cancel command exists; this method may only be invoked
        // after a technical fault or an unavailable backing store.
        registry.RevokeTask(taskId);
        workerJob.Dispose(); // Synchronous Windows handle close is critical.
        // At this point the worker's Job handle must be gone.
        using var timeout = new CancellationTokenSource(MaximumShutdown);
        try
        {
            await closeApplication().WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Application did not terminate within the 30-second fault policy.");
        }
        return PopupMessage;
    }
}
