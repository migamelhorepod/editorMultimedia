using System.Security.Principal;

namespace Editor.Security;

/// <summary>
/// Security descriptor blueprint for the future AppContainer pipe endpoint.
/// An SDDL alone is NOT an applied DACL. Creation is intentionally prohibited
/// until the Windows pipe endpoint enforces this descriptor and authenticates
/// the connected client. Never fall back to CurrentUserOnly for AppContainer.
/// </summary>
public static class RestrictedWorkerPipePolicy
{
    public static string BuildDacl(SecurityIdentifier controllerSid, SecurityIdentifier workerSid)
    {
        ArgumentNullException.ThrowIfNull(controllerSid);
        ArgumentNullException.ThrowIfNull(workerSid);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (!workerSid.Value.StartsWith("S-1-15-2-", StringComparison.Ordinal))
            throw new ArgumentException("Expected a Windows AppContainer package SID.", nameof(workerSid));
        if (controllerSid.Equals(workerSid))
            throw new ArgumentException("Controller and sandbox identities must differ.");
        // Owner = controller; only SYSTEM, controller and exact AppContainer
        // receive pipe access. No Everyone, Users or ALL APPLICATION PACKAGES.
        return $"O:{controllerSid.Value}D:P(A;;GA;;;SY)(A;;GA;;;{controllerSid.Value})(A;;GRGW;;;{workerSid.Value})";
    }

    public static void DemandConfiguredPipeEndpoint()
    {
        throw new UnauthorizedAccessException(
            "Restricted IPC endpoint is not active; do not fall back to user-only pipe.");
    }
}
