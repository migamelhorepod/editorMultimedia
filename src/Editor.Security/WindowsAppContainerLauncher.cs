using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Editor.Security;

/// <summary>
/// Experimental native AppContainer launch. This is an OS process identity
/// boundary; media remains prohibited until resource, IPC and adversarial
/// checks have been proven on Windows.
/// </summary>
public static class WindowsAppContainerLauncher
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateSuspended = 0x00000004;
    private static readonly nuint SecurityCapabilitiesAttribute = 0x00020009;

    public static int StartSuspended(string executable, string arguments)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable)
            || !File.Exists(executable))
            throw new ArgumentException("An existing absolute executable path is required.", nameof(executable));
        // Never accept a caller-supplied path to media or unspecified executable.
        if (!string.Equals(Path.GetFullPath(executable),
                Path.GetFullPath(Path.Combine(Environment.SystemDirectory, "cmd.exe")),
                StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Diagnostic launcher only permits Windows cmd.exe.");

        IntPtr sid = IntPtr.Zero, attributes = IntPtr.Zero, capabilitiesPtr = IntPtr.Zero;
        PROCESS_INFORMATION process = default;
        bool initialized = false;
        try
        {
            int hr = DeriveAppContainerSidFromAppContainerName(
                WindowsAppContainerPreflight.ProfileName, out sid);
            if (hr < 0 || sid == IntPtr.Zero)
                Marshal.ThrowExceptionForHR(hr);

            nuint bytes = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
            if (bytes == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            attributes = Marshal.AllocHGlobal(checked((nint)bytes));
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref bytes))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;

            var caps = new SECURITY_CAPABILITIES { AppContainerSid = sid };
            capabilitiesPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_CAPABILITIES>());
            Marshal.StructureToPtr(caps, capabilitiesPtr, false);
            if (!UpdateProcThreadAttribute(attributes, 0, SecurityCapabilitiesAttribute,
                capabilitiesPtr, (nuint)Marshal.SizeOf<SECURITY_CAPABILITIES>(),
                IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startup.lpAttributeList = attributes;
            string commandLine = "\"" + executable + "\" " + arguments;
            if (!CreateProcessW(executable, commandLine, IntPtr.Zero, IntPtr.Zero,
                false, ExtendedStartupInfoPresent | CreateSuspended,
                IntPtr.Zero, Environment.SystemDirectory, ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Windows refused the AppContainer diagnostic process.");
            // A suspended process cannot execute until the owner explicitly
            // resumes its thread; this API intentionally never resumes it.
            return checked((int)process.dwProcessId);
        }
        finally
        {
            if (process.hProcess != IntPtr.Zero)
            {
                TerminateProcess(process.hProcess, 1);
                CloseHandle(process.hProcess);
            }
            if (process.hThread != IntPtr.Zero) CloseHandle(process.hThread);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (capabilitiesPtr != IntPtr.Zero) Marshal.FreeHGlobal(capabilitiesPtr);
            if (sid != IntPtr.Zero) FreeSid(sid);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars;
        public int dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags,
        nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
        uint flags, IntPtr environment, string workingDirectory,
        ref STARTUPINFOEX startup, out PROCESS_INFORMATION information);
    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
