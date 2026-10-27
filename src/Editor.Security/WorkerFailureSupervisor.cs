namespace Editor.Security;

/// <summary>
/// Fault handling is a technical shutdown, never a user import cancellation.
/// The caller provides the actual worker job disposal and UI shutdown actions.
/// </summary>
public sealed class WorkerFailureSupervisor
{
    public const string PopupMessage = MediaImportCoordinator.FatalMessage;
    public static readonly TimeSpan MaximumShutdown = TimeSpan.FromSeconds(30);

    private readonly AccessGrantRegistry registry;
    private readonly Func<Task> terminateWorker;
    private readonly Func<Task> closeApplication;

    public WorkerFailureSupervisor(AccessGrantRegistry registry,
        Func<Task> terminateWorker, Func<Task> closeApplication)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.terminateWorker = terminateWorker ?? throw new ArgumentNullException(nameof(terminateWorker));
        this.closeApplication = closeApplication ?? throw new ArgumentNullException(nameof(closeApplication));
    }

    public async Task<string> HandleFailureAsync(Guid taskId)
    {
        // Revoke logical access first; terminate the owner Job to close opened
        // kernel handles. An unresponsive process is not trusted to cooperate.
        registry.RevokeTask(taskId);
        using var limit = new CancellationTokenSource(MaximumShutdown);
        try
        {
            await terminateWorker().WaitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            // Do not claim successful cleanup when the termination implementation
            // fails to honor its deadline. The caller must independently enforce
            // Job-object KILL_ON_JOB_CLOSE.
            throw new TimeoutException("Worker could not be confirmed terminated within 30 seconds.");
        }
        await closeApplication().WaitAsync(limit.Token).ConfigureAwait(false);
        return PopupMessage;
    }
}
