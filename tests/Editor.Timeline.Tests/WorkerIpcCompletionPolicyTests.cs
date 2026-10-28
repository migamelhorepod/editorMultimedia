using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

/// <summary>
/// BDD: the controller must never treat an exited sandbox worker as proof that
/// its independently authenticated IPC response arrived successfully.
/// </summary>
public sealed class WorkerIpcCompletionPolicyTests
{
    [Fact]
    public void GivenWorkerExitedSuccessfullyWithoutIpc_WhenControllerWaits_ThenItFailsClosedWithinDeadline()
    {
        var missing = new TaskCompletionSource<WorkerResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var failure = Assert.Throws<InvalidOperationException>(() =>
            WorkerIpcCompletionPolicy.RequireAuthenticatedResponse(
                missing.Task, 0, TimeSpan.FromMilliseconds(30)));
        Assert.Contains("without completing authenticated IPC", failure.Message);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GivenWorkerExitedWithErrorAndNoIpc_WhenControllerChecks_ThenItFailsImmediately()
    {
        var missing = new TaskCompletionSource<WorkerResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = Assert.Throws<InvalidOperationException>(() =>
            WorkerIpcCompletionPolicy.RequireAuthenticatedResponse(missing.Task, 77));
        Assert.Contains("status 77", failure.Message);
    }

    [Fact]
    public void GivenAuthenticatedResponse_WhenWorkerExitedWithSuccess_ThenControllerAcceptsReport()
    {
        Guid id = Guid.NewGuid();
        var authenticated = new WorkerResponse(
            WorkerRequest.CurrentVersion, id, AccessDecision.Allowed);
        Assert.Same(authenticated,
            WorkerIpcCompletionPolicy.RequireAuthenticatedResponse(
                Task.FromResult(authenticated), 0));
    }

    [Fact]
    public void GivenServerIpcFailed_WhenWorkerExited_ThenAuthenticationErrorIsPropagated()
    {
        var rejected = Task.FromException<WorkerResponse>(
            new UnauthorizedAccessException("Wrong worker process ID"));
        Assert.Throws<UnauthorizedAccessException>(() =>
            WorkerIpcCompletionPolicy.RequireAuthenticatedResponse(rejected, 0));
    }

    [Fact]
    public void GivenInvalidGrace_WhenControllerChecks_ThenItRejectsInvalidConfiguration()
    {
        var pending = new TaskCompletionSource<WorkerResponse>();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkerIpcCompletionPolicy.RequireAuthenticatedResponse(
                pending.Task, 0, TimeSpan.Zero));
    }
}
