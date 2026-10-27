using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Editor.Security;

/// <summary>
/// Windows AppContainer identity preflight only. Computing a package SID is
/// NOT creating a sandbox, profile, token, or restricted process.
/// </summary>
public static class WindowsAppContainerPreflight
{
    public const string ProfileName = "MigaMelhore.EditorMultimedia.MediaWorker";

    public static string DerivePackageSid()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AppContainer identity requires Windows.");

        int hr = DeriveAppContainerSidFromAppContainerName(ProfileName, out IntPtr sid);
        if (hr < 0 || sid == IntPtr.Zero)
            throw new Win32Exception(hr, "Unable to derive the worker AppContainer SID.");

        try
        {
            return new SecurityIdentifier(sid).Value;
        }
        finally
        {
            FreeSid(sid);
        }
    }

    /// <summary>
    /// Keep the actual launch disabled until STARTUPINFOEX, capabilities,
    /// process creation and adversarial OS isolation have been verified.
    /// </summary>
    public static void DemandSandboxedLaunch()
    {
        throw new UnauthorizedAccessException(
            "AppContainer launch is not implemented or verified; no unrestricted fallback.");
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int DeriveAppContainerSidFromAppContainerName(
        string pszAppContainerName, out IntPtr ppsidAppContainerSid);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern IntPtr FreeSid(IntPtr pSid);
}
