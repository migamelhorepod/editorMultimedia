using System.Runtime.InteropServices;
using Editor.Security;

// Diagnostic-only worker. File probes never return file contents and must be
// launched by WindowsAppContainerLauncher under a verified AppContainer token.
if (args.Length == 2 && args[0] == "--probe-denied-file")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess())
        return 70; // A non-sandboxed invocation MUST NOT touch the file.
    if (!Path.IsPathFullyQualified(args[1]))
        return 71;
    try
    {
        using var stream = File.OpenRead(args[1]);
        return 72; // Test fails: OS allowed reading a protected file.
    }
    catch (UnauthorizedAccessException)
    {
        return 0; // Windows enforced the file permission boundary.
    }
    catch (IOException)
    {
        return 73; // Missing/locked files are not proof of access denial.
    }
}

// Existing diagnostic IPC; media stays blocked.
if (args.Length != 2 || args[0] != "--pipe")
{
    Console.Error.WriteLine("Unsupported diagnostic operation.");
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

static bool IsAppContainerProcess()
{
    if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out var token))
        return false;
    try
    {
        return GetTokenInformation(token, 29, out int value,
            sizeof(int), out int size) && size == sizeof(int) && value == 1;
    }
    finally
    {
        CloseHandle(token);
    }
}

[DllImport("kernel32.dll")]
static extern IntPtr GetCurrentProcess();
[DllImport("advapi32.dll", SetLastError = true)]
static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
[DllImport("advapi32.dll", SetLastError = true)]
static extern bool GetTokenInformation(IntPtr token, int infoClass,
    out int value, int length, out int lengthReturned);
[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr handle);
