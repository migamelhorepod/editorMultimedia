using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Editor.Security.Worker;

/// <summary>
/// Windows-only fd: research helper. Runs bundled ffprobe with the EXACT media
/// HANDLE already installed as the worker's STDIN by the controller. Managed
/// Process.Start does not reliably forward inherited STDIN to a child when
/// standard streams are redirected; use explicit Win32 STARTF_USESTDHANDLES.
/// No media path is passed and no copy is created.
/// </summary>
internal static class WindowsInheritedHandleFfprobe
{
    internal static async Task<(int ExitCode, string Output)> ReadDurationAsync(
        string ffprobe, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        IntPtr input = GetStdHandle(unchecked((uint)-10));
        if (input == IntPtr.Zero || input == new IntPtr(-1))
            throw new IOException("Inherited media standard input missing.");

        // The anonymous-pipe client HANDLE is limited to this one child.
        using var outputPipe = new AnonymousPipeServerStream(
            PipeDirection.In, HandleInheritability.Inheritable);
        IntPtr output = outputPipe.ClientSafePipeHandle.DangerousGetHandle();
        IntPtr error = GetStdHandle(unchecked((uint)-12));
        if (error == IntPtr.Zero || error == new IntPtr(-1))
            throw new IOException("Inert standard error handle missing.");

        var startup = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            dwFlags = 0x00000100u,
            hStdInput = input,
            hStdOutput = output,
            hStdError = error
        };
        var commandLine = new System.Text.StringBuilder(
            "\"" + ffprobe + "\" -v error -protocol_whitelist fd -i fd: " +
            "-show_entries format=duration -of default=noprint_wrappers=1");
        if (!CreateProcessW(ffprobe, commandLine, IntPtr.Zero, IntPtr.Zero,
                true, 0x08000000, IntPtr.Zero, null, ref startup, out var child))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot start restricted seekable FFprobe child.");
        try
        {
            // Only the real codec is launched. It inherits the worker's
            // AppContainer security identity and the controller's Job Object.
            outputPipe.DisposeLocalCopyOfClientHandle();
            uint wait = WaitForSingleObject(child.hProcess, 15000);
            if (wait != 0)
            {
                TerminateProcess(child.hProcess, 1);
                throw new TimeoutException("Restricted FFprobe failed its deadline.");
            }
            if (!GetExitCodeProcess(child.hProcess, out uint code))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using var reader = new StreamReader(outputPipe);
            string text = await reader.ReadToEndAsync(cancellationToken);
            return (unchecked((int)code), text);
        }
        finally
        {
            CloseHandle(child.hThread);
            CloseHandle(child.hProcess);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(uint standardHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application,
        System.Text.StringBuilder commandLine, IntPtr processSecurity,
        IntPtr threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags, IntPtr environment, string? currentDirectory,
        ref STARTUPINFO startup, out PROCESS_INFORMATION process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
