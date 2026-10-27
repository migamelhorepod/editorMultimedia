using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

/// <summary>
/// POC-003.1 release gate: until OS-level adversarial proofs exist,
/// no media processing may be authorized. These tests MUST NOT be replaced
/// with skips or optimistic feature flags.
/// </summary>
public sealed class Poc0031ReleaseGateTests
{
    [Theory]
    [InlineData(MediaAccessOperation.InspectMetadata)]
    [InlineData(MediaAccessOperation.ReadChunk)]
    public void NoValidGrantCanBypassUnverifiedSandbox(MediaAccessOperation op)
    {
        var library = Guid.NewGuid();
        var task = Guid.NewGuid();
        var resource = Guid.NewGuid();
        var registry = new AccessGrantRegistry(library);
        var grant = registry.Issue(task, library, resource, op, TimeSpan.FromMinutes(1));
        var request = new WorkerRequest(WorkerRequest.CurrentVersion,
            Guid.NewGuid(), task, library, resource, grant, op);
        Assert.False(MediaWorkerAdmission.IsMediaProcessingAvailable);
        Assert.Equal(AccessDecision.Unauthorized,
            MediaWorkerAdmission.AuthorizeMediaRequest(registry, request));
    }

    [Fact]
    public void RefuseMediaEvenAfterAppContainerDiagnosticSucceeds()
    {
        Assert.Throws<UnauthorizedAccessException>(
            WindowsAppContainerPreflight.DemandSandboxedLaunch);
        Assert.Throws<UnauthorizedAccessException>(
            MediaWorkerAdmission.DemandMediaProcessing);
    }
}
