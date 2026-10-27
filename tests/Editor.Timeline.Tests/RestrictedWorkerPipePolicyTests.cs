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
    public async Task RestrictedPipeHandshakeReturnsMatchingResponse()
    {
        if (!OperatingSystem.IsWindows()) return;
        var pipe = new WindowsWorkerPipe(new AccessGrantRegistry(Guid.NewGuid()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        var server = pipe.ServeRestrictedOnceAsync(Environment.ProcessId, timeout.Token);
        var request = WorkerRequest.Heartbeat();
        var reply = await WindowsWorkerPipe.ConnectRestrictedOnceAsync(
            pipe.PipeName, request, timeout.Token);
        var observed = await server;
        Assert.Equal(request.RequestId, reply.RequestId);
        Assert.Equal(request.RequestId, observed.RequestId);
        Assert.False(MediaWorkerAdmission.IsMediaProcessingAvailable);
    }

    [Fact]
    public async Task RestrictedPipeRejectsSeparateUnauthorizedProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var clientScript = Path.Combine(root, "tests", "security", "unauthorized-pipe-client.ps1");
        Assert.True(File.Exists(clientScript));
        var pipe = new WindowsWorkerPipe(new AccessGrantRegistry(Guid.NewGuid()));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(9));
        var server = pipe.ServeRestrictedOnceAsync(Environment.ProcessId, deadline.Token);
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(clientScript);
        start.ArgumentList.Add(pipe.PipeName);
        using var attacker = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Cannot start negative IPC client.");
        Assert.NotEqual(Environment.ProcessId, attacker.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await server);
        Assert.True(attacker.WaitForExit(5000), "Attacker process remained alive.");
        Assert.Equal(0, attacker.ExitCode);
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
