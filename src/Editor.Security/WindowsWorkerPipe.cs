using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Editor.Security;

/// <summary>
/// Local Windows IPC prototype. The kernel supplies the connected client PID;
/// a caller-provided taskId/workerId is NOT proof of identity.
/// This is not a filesystem sandbox and must not be used to process untrusted
/// media until an independently verified Windows isolation policy exists.
/// </summary>
public sealed partial class WindowsWorkerPipe
{
    public const string Prefix = "editor-poc003-";
    private readonly AccessGrantRegistry registry;

    public WindowsWorkerPipe(AccessGrantRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        PipeName = Prefix + Guid.NewGuid().ToString("N");
    }

    public string PipeName { get; }

    public async Task<WorkerResponse> ServeOnceAsync(
        int expectedWorkerProcessId, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("IPC identity check requires Windows.");
        if (expectedWorkerProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedWorkerProcessId));

        using var server = new NamedPipeServerStream(PipeName,
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out uint actualPid))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not authenticate the connected worker process.");
        if (actualPid != (uint)expectedWorkerProcessId)
            throw new UnauthorizedAccessException("Unexpected worker process identity.");

        var request = await WorkerWireProtocol.ReadAsync<WorkerRequest>(
            server, cancellationToken).ConfigureAwait(false);
        var decision = registry.Authorize(request);
        var response = new WorkerResponse(WorkerRequest.CurrentVersion, request.RequestId, decision);
        await WorkerWireProtocol.WriteAsync(server, response, cancellationToken)
            .ConfigureAwait(false);
        return response;
    }

    public static async Task<WorkerResponse> ConnectOnceAsync(
        string pipeName, WorkerRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("IPC identity check requires Windows.");
        if (!IsOurPipeName(pipeName))
            throw new ArgumentException("Invalid POC-003 IPC name.", nameof(pipeName));
        using var client = new NamedPipeClientStream(".",
            pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
        await WorkerWireProtocol.WriteAsync(client, request, cancellationToken)
            .ConfigureAwait(false);
        var reply = await WorkerWireProtocol.ReadAsync<WorkerResponse>(
            client, cancellationToken).ConfigureAwait(false);
        if (reply.ProtocolVersion != WorkerRequest.CurrentVersion ||
            reply.RequestId != request.RequestId)
            throw new InvalidDataException("IPC response does not match the request.");
        return reply;
    }

    private static bool IsOurPipeName(string? name) =>
        name is not null && name.Length == Prefix.Length + 32 &&
        name.StartsWith(Prefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(name.AsSpan(Prefix.Length), "N", out _);

    [LibraryImport("kernel32.dll", EntryPoint = "GetNamedPipeClientProcessId",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe, out uint clientProcessId);
}
