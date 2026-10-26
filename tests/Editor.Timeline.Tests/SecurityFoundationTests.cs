using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class SecurityFoundationTests
{
    private sealed class TestClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public void Advance(TimeSpan delta) => Interlocked.Add(ref ticks, delta.Ticks);
    }

    private static (AccessGrantRegistry Registry, WorkerRequest Request) Authorized(
        TimeProvider? clock = null)
    {
        var library = Guid.NewGuid();
        var task = Guid.NewGuid();
        var resource = Guid.NewGuid();
        var registry = new AccessGrantRegistry(library, clock);
        var grant = registry.Issue(task, library, resource,
            MediaAccessOperation.InspectMetadata, TimeSpan.FromSeconds(15));
        return (registry, new WorkerRequest(WorkerRequest.CurrentVersion,
            Guid.NewGuid(), task, library, resource, grant,
            MediaAccessOperation.InspectMetadata));
    }

    [Fact]
    public void OnlyExactResourceTaskLibraryAndOperationReceiveAuthorization()
    {
        var (registry, request) = Authorized();
        Assert.Equal(AccessDecision.Allowed, registry.Authorize(request));
        Assert.Equal(AccessDecision.Unauthorized, registry.Authorize(
            request with { TaskId = Guid.NewGuid() }));
        Assert.Equal(AccessDecision.Unauthorized, registry.Authorize(
            request with { ResourceId = Guid.NewGuid() }));
        Assert.Equal(AccessDecision.Unauthorized, registry.Authorize(
            request with { LibraryId = Guid.NewGuid() }));
        Assert.Equal(AccessDecision.Unauthorized, registry.Authorize(
            request with { Operation = MediaAccessOperation.ReadChunk }));
        Assert.Equal(AccessDecision.Unauthorized, registry.Authorize(
            request with { GrantId = Guid.NewGuid() }));
    }

    [Fact]
    public void ProtocolVersionAndUnknownOperationAreRejected()
    {
        var (registry, request) = Authorized();
        Assert.Equal(AccessDecision.InvalidProtocol,
            registry.Authorize(request with { ProtocolVersion = 999 }));
        Assert.Equal(AccessDecision.InvalidProtocol,
            registry.Authorize(request with { Operation = (MediaAccessOperation)123 }));
        Assert.Equal(AccessDecision.InvalidProtocol,
            registry.Authorize(request with { RequestId = Guid.Empty }));
        Assert.Equal(AccessDecision.Allowed, registry.Authorize(WorkerRequest.Heartbeat()));
        Assert.Equal(AccessDecision.InvalidProtocol,
            registry.Authorize(WorkerRequest.Heartbeat() with { TaskId = Guid.NewGuid() }));
    }

    [Fact]
    public void GrantExpiresUsingMonotonicClockWithoutSleep()
    {
        var clock = new TestClock();
        var (registry, request) = Authorized(clock);
        clock.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(AccessDecision.Allowed, registry.Authorize(request));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(AccessDecision.Expired, registry.Authorize(request));
        Assert.Equal(AccessDecision.Unauthorized, registry.Authorize(request));
    }

    [Fact]
    public void CancellationRevokesTaskAndPreventsReusingTheSameIdentifier()
    {
        var (registry, request) = Authorized();
        registry.RevokeTask(request.TaskId);
        Assert.Equal(AccessDecision.Revoked, registry.Authorize(request));
        Assert.Throws<InvalidOperationException>(() =>
            registry.Issue(request.TaskId, request.LibraryId, request.ResourceId,
                request.Operation, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void InactiveLibraryRevokesAllGrantsAndPreventsNewOnes()
    {
        var (registry, request) = Authorized();
        registry.DeactivateLibrary();
        Assert.Equal(AccessDecision.Revoked, registry.Authorize(request));
        Assert.Throws<InvalidOperationException>(() =>
            registry.Issue(Guid.NewGuid(), request.LibraryId, request.ResourceId,
                request.Operation, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void DoesNotIssueGrantsForInactiveLibrariesOrInvalidOperation()
    {
        var registry = new AccessGrantRegistry(Guid.NewGuid());
        Assert.Throws<ArgumentException>(() =>
            registry.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                MediaAccessOperation.ReadChunk, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() =>
            registry.Issue(Guid.NewGuid(), registry.ActiveLibraryId, Guid.NewGuid(),
                MediaAccessOperation.Heartbeat, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() =>
            registry.Issue(Guid.NewGuid(), registry.ActiveLibraryId, Guid.NewGuid(),
                MediaAccessOperation.InspectMetadata, TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public async Task FramesAreBoundedAndUnknownPropertiesAreRejected()
    {
        var (registry, request) = Authorized();
        using var stream = new MemoryStream();
        await WorkerWireProtocol.WriteAsync(stream, request);
        stream.Position = 0;
        var parsed = await WorkerWireProtocol.ReadAsync<WorkerRequest>(stream);
        Assert.Equal(AccessDecision.Allowed, registry.Authorize(parsed));
        Assert.Equal(request, parsed);

        using var oversized = new MemoryStream();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, WorkerWireProtocol.MaxFrameBytes + 1);
        await oversized.WriteAsync(header);
        oversized.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WorkerWireProtocol.ReadAsync<WorkerRequest>(oversized));

        var unknown = Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"unexpectedPath\":\"C:\\\\private\"}");
        using var bad = new MemoryStream();
        BinaryPrimitives.WriteInt32LittleEndian(header, unknown.Length);
        await bad.WriteAsync(header);
        await bad.WriteAsync(unknown);
        bad.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WorkerWireProtocol.ReadAsync<WorkerRequest>(bad));
    }

    [Fact]
    public async Task WindowsPipeValidatesKernelClientPidAndUsesControllerGrants()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (registry, request) = Authorized();
        var pipe = new WindowsWorkerPipe(registry);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = pipe.ServeOnceAsync(Environment.ProcessId, timeout.Token);
        var clientReply = await WindowsWorkerPipe.ConnectOnceAsync(
            pipe.PipeName, request, timeout.Token);
        var serverReply = await server;
        Assert.Equal(AccessDecision.Allowed, clientReply.Decision);
        Assert.Equal(serverReply, clientReply);
        Assert.Equal(request.RequestId, clientReply.RequestId);
    }

    [Fact]
    public async Task DiagnosticWorkerRunsInSeparateWindowsProcessAndIdentifiesItsRealPid()
    {
        if (!OperatingSystem.IsWindows()) return;
        // This tests a REAL separate process, not an OS-restricted sandbox.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Test configuration not found.");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var workerDll = Path.Combine(root, "src", "Editor.Security.Worker",
            "bin", configuration, "net10.0", "Editor.Security.Worker.dll");
        Assert.True(File.Exists(workerDll), $"Diagnostic worker not built: {workerDll}");

        var registry = new AccessGrantRegistry(Guid.NewGuid());
        var pipe = new WindowsWorkerPipe(registry);
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(workerDll);
        info.ArgumentList.Add("--pipe");
        info.ArgumentList.Add(pipe.PipeName);
        using var worker = new Process { StartInfo = info };
        Assert.True(worker.Start());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var response = await pipe.ServeOnceAsync(worker.Id, timeout.Token);
            Assert.True(response.Accepted);
            await worker.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, worker.ExitCode);
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task WindowsPipeDeniesClientWhenKernelPidDoesNotMatchExpectedWorker()
    {
        if (!OperatingSystem.IsWindows()) return;
        var registry = new AccessGrantRegistry(Guid.NewGuid());
        var pipe = new WindowsWorkerPipe(registry);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Connect from this process while the controller expects a different PID.
        var server = pipe.ServeOnceAsync(Environment.ProcessId + 1, timeout.Token);
        var client = WindowsWorkerPipe.ConnectOnceAsync(
            pipe.PipeName, WorkerRequest.Heartbeat(), timeout.Token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await server);
        await Assert.ThrowsAnyAsync<IOException>(async () => await client);
    }

    [Fact]
    public async Task WindowsPipeRejectsUnauthorizedResourcesWithoutOpeningFiles()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (registry, request) = Authorized();
        var pipe = new WindowsWorkerPipe(registry);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = pipe.ServeOnceAsync(Environment.ProcessId, timeout.Token);
        var response = await WindowsWorkerPipe.ConnectOnceAsync(
            pipe.PipeName, request with { ResourceId = Guid.NewGuid() }, timeout.Token);
        await server;
        Assert.False(response.Accepted);
        Assert.Equal(AccessDecision.Unauthorized, response.Decision);
    }
}
