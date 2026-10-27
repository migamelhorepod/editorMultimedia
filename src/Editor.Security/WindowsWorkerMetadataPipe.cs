using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Editor.Security;

/// <summary>
/// A bounded metadata-only channel from a kernel-verified AppContainer worker
/// to its controller. The pipe is owned by the controller and scoped to one
/// unpredictable name, one expected OS PID and one request ID.
/// </summary>
public sealed partial class WindowsWorkerMetadataPipe
{
    public sealed record MetadataReport(int ProtocolVersion, Guid RequestId, long DurationTicks);
    public sealed record MetadataAck(int ProtocolVersion, Guid RequestId, bool Accepted);

    public string Name { get; } = WindowsWorkerPipe.Prefix + Guid.NewGuid().ToString("N");

    public async Task<long> ReceiveOnceAsync(int expectedPid, Guid expectedRequestId,
        CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (expectedPid <= 0 || expectedRequestId == Guid.Empty)
            throw new ArgumentException("Worker PID and request ID must be specified.");
        var controller = WindowsIdentity.GetCurrent().User
            ?? throw new UnauthorizedAccessException("Missing controller Windows identity.");
        var sandboxSid = new SecurityIdentifier(WindowsAppContainerPreflight.DerivePackageSid());
        using var endpoint = RestrictedWorkerPipePolicy.CreateEndpoint(Name, controller, sandboxSid);
        await endpoint.WaitForConnectionAsync(token).ConfigureAwait(false);
        if (!GetNamedPipeClientProcessId(endpoint.SafePipeHandle, out uint actualPid) ||
            actualPid != (uint)expectedPid)
            throw new UnauthorizedAccessException("Metadata sender is not the verified sandbox worker.");

        var report = await WorkerWireProtocol.ReadAsync<MetadataReport>(endpoint, token)
            .ConfigureAwait(false);
        if (report.ProtocolVersion != WorkerRequest.CurrentVersion ||
            report.RequestId != expectedRequestId ||
            report.DurationTicks <= 0 ||
            report.DurationTicks > TimeSpan.FromDays(7).Ticks)
            throw new InvalidDataException("Invalid authenticated worker metadata.");
        await WorkerWireProtocol.WriteAsync(endpoint,
            new MetadataAck(WorkerRequest.CurrentVersion, expectedRequestId, true), token)
            .ConfigureAwait(false);
        return report.DurationTicks;
    }

    public static async Task SubmitOnceAsync(string pipeName, MetadataReport report,
        CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!ValidName(pipeName) || report.RequestId == Guid.Empty ||
            report.DurationTicks <= 0 || report.DurationTicks > TimeSpan.FromDays(7).Ticks)
            throw new InvalidDataException("Invalid metadata report request.");
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, token).ConfigureAwait(false);
        await WorkerWireProtocol.WriteAsync(client, report, token).ConfigureAwait(false);
        var ack = await WorkerWireProtocol.ReadAsync<MetadataAck>(client, token).ConfigureAwait(false);
        if (!ack.Accepted || ack.ProtocolVersion != WorkerRequest.CurrentVersion ||
            ack.RequestId != report.RequestId)
            throw new UnauthorizedAccessException("Controller refused metadata report.");
    }

    private static bool ValidName(string name) =>
        name.Length == WindowsWorkerPipe.Prefix.Length + 32 &&
        name.StartsWith(WindowsWorkerPipe.Prefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(name.AsSpan(WindowsWorkerPipe.Prefix.Length), "N", out _);

    [LibraryImport("kernel32.dll", EntryPoint = "GetNamedPipeClientProcessId",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe, out uint clientProcessId);
}
