using System.Security.Principal;
using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class RestrictedWorkerPipePolicyTests
{
    [Fact]
    public void PipePolicyGrantsOnlyControllerSystemAndExactAppContainer()
    {
        if (!OperatingSystem.IsWindows()) return;
        var controller = WindowsIdentity.GetCurrent().User!;
        var worker = new SecurityIdentifier(WindowsAppContainerPreflight.DerivePackageSid());
        var sddl = RestrictedWorkerPipePolicy.BuildDacl(controller, worker);
        Assert.Contains(controller.Value, sddl);
        Assert.Contains(worker.Value, sddl);
        Assert.Contains("D:P", sddl);
        Assert.DoesNotContain(";;;WD)", sddl);
        Assert.DoesNotContain(";;;AC)", sddl);
        Assert.Throws<UnauthorizedAccessException>(
            RestrictedWorkerPipePolicy.DemandConfiguredPipeEndpoint);
    }

    [Fact]
    public void CanCreatePipeWithActualRestrictedSecurityDescriptor()
    {
        if (!OperatingSystem.IsWindows()) return;
        var controller = WindowsIdentity.GetCurrent().User!;
        var worker = new SecurityIdentifier(WindowsAppContainerPreflight.DerivePackageSid());
        string pipe = WindowsWorkerPipe.Prefix + Guid.NewGuid().ToString("N");
        using var endpoint = RestrictedWorkerPipePolicy.CreateEndpoint(pipe, controller, worker);
        Assert.False(endpoint.IsConnected);
    }

    [Fact]
    public async Task RestrictedPipeRejectsUnexpectedClientProcessId()
    {
        if (!OperatingSystem.IsWindows()) return;
        var registry = new AccessGrantRegistry(Guid.NewGuid());
        var pipe = new WindowsWorkerPipe(registry);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var server = pipe.ServeRestrictedOnceAsync(Environment.ProcessId + 1, timeout.Token);
        using var client = new System.IO.Pipes.NamedPipeClientStream(".",
            pipe.PipeName, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, timeout.Token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await server);
    }

    [Fact]
    public void PipePolicyRejectsNonAppContainerIdentity()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sid = WindowsIdentity.GetCurrent().User!;
        Assert.Throws<ArgumentException>(() => RestrictedWorkerPipePolicy.BuildDacl(sid, sid));
    }
}
