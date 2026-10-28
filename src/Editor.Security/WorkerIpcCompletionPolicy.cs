namespace Editor.Security;

/// <summary>
/// The OS process exit code does not authenticate an IPC response. This policy
/// bounds the time a controller will wait once the restricted worker exits,
/// including when the worker erroneously reports success without a handshake.
/// </summary>
internal static class WorkerIpcCompletionPolicy
{
    private static readonly TimeSpan PostExitGrace = TimeSpan.FromSeconds(2);

    internal static WorkerResponse RequireAuthenticatedResponse(
        Task<WorkerResponse> serverTask, int workerExitCode,
        TimeSpan? grace = null)
    {
        ArgumentNullException.ThrowIfNull(serverTask);
        if (!serverTask.IsCompleted && workerExitCode != 0)
            throw new InvalidOperationException(
                $"AppContainer worker exited with status {workerExitCode} before completing authenticated IPC.");

        if (!serverTask.IsCompleted)
        {
            TimeSpan deadline = grace ?? PostExitGrace;
            if (deadline <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(grace));
            try
            {
                return serverTask.WaitAsync(deadline).GetAwaiter().GetResult();
            }
            catch (TimeoutException ex)
            {
                throw new InvalidOperationException(
                    "AppContainer worker exited without completing authenticated IPC.", ex);
            }
        }
        return serverTask.GetAwaiter().GetResult();
    }
}
