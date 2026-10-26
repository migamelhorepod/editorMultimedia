using Editor.Security;

// Diagnostic-only worker. There is intentionally NO file or FFmpeg access here.
// Actual decoding stays prohibited until Windows token/isolation tests pass.
if (args.Length != 2 || args[0] != "--pipe")
{
    Console.Error.WriteLine("Usage: Editor.Security.Worker --pipe <controller-assigned-name>");
    return 64;
}
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
try
{
    var reply = await WindowsWorkerPipe.ConnectOnceAsync(
        args[1], WorkerRequest.Heartbeat(), timeout.Token);
    return reply.Accepted ? 0 : 1;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or
    UnauthorizedAccessException or ArgumentException or
    OperationCanceledException or PlatformNotSupportedException)
{
    Console.Error.WriteLine($"Diagnostic IPC failed: {ex.GetType().Name}");
    return 1;
}
