using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class MediaWorkerAdmissionTests
{
    [Fact]
    public void GivenSandboxProvenButProductionIntegrationPending_WhenAdmittingMedia_ThenItFailsClosed()
    {
        Assert.False(MediaWorkerAdmission.IsMediaProcessingAvailable);
        var error = Assert.Throws<UnauthorizedAccessException>(
            MediaWorkerAdmission.DemandMediaProcessing);
        Assert.Contains("POC-003.2", error.Message);
        Assert.Contains("No unrestricted fallback", error.Message);
    }

    [Theory]
    [InlineData(MediaAccessOperation.InspectMetadata)]
    [InlineData(MediaAccessOperation.ReadChunk)]
    public void EvenValidGrantCannotEnableUnsandboxedMedia(MediaAccessOperation operation)
    {
        var library = Guid.NewGuid();
        var task = Guid.NewGuid();
        var resource = Guid.NewGuid();
        var registry = new AccessGrantRegistry(library);
        var grant = registry.Issue(task, library, resource, operation,
            TimeSpan.FromMinutes(1));
        var request = new WorkerRequest(WorkerRequest.CurrentVersion,
            Guid.NewGuid(), task, library, resource, grant, operation);

        Assert.Equal(AccessDecision.Allowed, registry.Authorize(request));
        Assert.Equal(AccessDecision.Unauthorized,
            MediaWorkerAdmission.AuthorizeMediaRequest(registry, request));
    }

    [Fact]
    public void MalformedAndRevokedRequestsRemainRejected()
    {
        var library = Guid.NewGuid();
        var registry = new AccessGrantRegistry(library);
        Assert.Equal(AccessDecision.InvalidProtocol,
            MediaWorkerAdmission.AuthorizeMediaRequest(registry,
                WorkerRequest.Heartbeat() with { ProtocolVersion = 999 }));
        Assert.Equal(AccessDecision.Allowed,
            MediaWorkerAdmission.AuthorizeMediaRequest(registry,
                WorkerRequest.Heartbeat()));
        var task = Guid.NewGuid();
        var resource = Guid.NewGuid();
        var grant = registry.Issue(task, library, resource,
            MediaAccessOperation.InspectMetadata, TimeSpan.FromSeconds(10));
        registry.RevokeTask(task);
        Assert.Equal(AccessDecision.Revoked,
            MediaWorkerAdmission.AuthorizeMediaRequest(registry,
                new WorkerRequest(WorkerRequest.CurrentVersion, Guid.NewGuid(),
                    task, library, resource, grant,
                    MediaAccessOperation.InspectMetadata)));
    }
}
