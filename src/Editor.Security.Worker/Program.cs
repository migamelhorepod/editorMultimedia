using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Diagnostics;
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

if (args.Length == 2 && args[0] == "--probe-denied-network")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess())
        return 70;
    if (!int.TryParse(args[1], out int port) || port is < 1 or > 65535)
        return 71;
    using var client = new TcpClient();
    try
    {
        using var networkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync("127.0.0.1", port, networkTimeout.Token);
        return 72; // Security failure: outbound connection succeeded.
    }
    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
    {
        return 0; // Kernel refused network access.
    }
    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
    {
        return 73; // Timeout/refused: NOT proof of sandbox denial.
    }
}

// Fixed external IPv4 canary: no DNS, no user URL, no payload transfer.
// 0 is an explicit socket access denial; 73 means inconclusive timeout/refused.
if (args.Length == 1 && args[0] == "--probe-external-network")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess()) return 70;
    using var socket = new TcpClient();
    try
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await socket.ConnectAsync("1.1.1.1", 443, deadline.Token);
        return 72; // Unacceptable: external egress succeeded.
    }
    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
    {
        return 0;
    }
    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
    {
        return 73; // Not proof of WFP policy.
    }
}

if (args.Length == 2 && args[0] == "--restricted-pipe")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess()) return 70;
    try
    {
        using var pipeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var reply = await WindowsWorkerPipe.ConnectRestrictedOnceAsync(
            args[1], WorkerRequest.Heartbeat(), pipeTimeout.Token);
        return reply.Accepted ? 0 : 74; // Diagnostic heartbeat is allowed; media remains blocked.
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or
        UnauthorizedAccessException or OperationCanceledException or TimeoutException)
    {
        return 73;
    }
}

// Test-only codec subprocess: the launcher verifies AppContainer identity and
// applies a two-member Job before any worker code executes. Only a synthetic
// fixture with a dedicated Windows ACL is permitted by the test harness.
if (args.Length == 2 && (args[0] == "--probe-ffprobe" || args[0] == "--probe-ffmpeg"))
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess()) return 70;
    if (!Path.IsPathFullyQualified(args[1])) return 71;
    string tool = args[0] == "--probe-ffmpeg" ? "ffmpeg.exe" : "ffprobe.exe";
    string executable = Path.Combine(AppContext.BaseDirectory, tool);
    if (!File.Exists(executable)) return 75;
    var start = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    if (tool == "ffmpeg.exe") start.ArgumentList.Add("-nostdin");
    start.ArgumentList.Add("-v");
    start.ArgumentList.Add("error");
    start.ArgumentList.Add("-i");
    start.ArgumentList.Add(args[1]);
    if (tool == "ffprobe.exe")
    {
        start.ArgumentList.Add("-show_entries");
        start.ArgumentList.Add("format=duration");
        start.ArgumentList.Add("-of");
        start.ArgumentList.Add("default=noprint_wrappers=1");
    }
    else
    {
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add("null");
        start.ArgumentList.Add("-");
    }
    try
    {
        using var process = Process.Start(start);
        if (process is null) return 76;
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var output = process.StandardOutput.ReadToEndAsync(cancel.Token);
        var error = process.StandardError.ReadToEndAsync(cancel.Token);
        await process.WaitForExitAsync(cancel.Token);
        var stdout = await output;
        _ = await error;
        if (process.ExitCode != 0) return 77;
        if (tool == "ffprobe.exe")
        {
            var durationLine = stdout.Split('\n').FirstOrDefault(
                line => line.Trim().StartsWith("duration=", StringComparison.Ordinal));
            if (durationLine is null ||
                !double.TryParse(durationLine.Trim()["duration=".Length..],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double duration) ||
                duration is < 0.09 or > 0.11)
                return 79; // ffprobe must decode metadata of the granted 0.10 s WAV.
        }
        return 0;
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or
        System.ComponentModel.Win32Exception or OperationCanceledException)
    {
        return 78;
    }
}

// Foreign AppContainer proof: only explicit ERROR_ACCESS_DENIED from CreateFile
// on an existing pipe endpoint counts. A timeout or absent pipe is failure.
if (args.Length == 2 && args[0] == "--probe-foreign-pipe")
{
    if (!OperatingSystem.IsWindows() || !IsAppContainerProcess() ||
        !args[1].StartsWith(WindowsWorkerPipe.Prefix, StringComparison.Ordinal))
        return 70;
    IntPtr handle = CreateFileW(@"\\.\pipe\" + args[1], 0xC0000000u, 0,
        IntPtr.Zero, 3, 0, IntPtr.Zero);
    if (handle != new IntPtr(-1))
    {
        CloseHandle(handle);
        return 72; // Incorrectly granted access to a foreign package SID.
    }
    return Marshal.GetLastWin32Error() == 5 ? 0 : 73;
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
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern IntPtr CreateFileW(string path, uint access, uint share, IntPtr security,
    uint creation, uint flags, IntPtr templateFile);
[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr handle);
