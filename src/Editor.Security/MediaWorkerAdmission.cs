namespace Editor.Security;

/// <summary>
/// Fail-closed admission boundary for any future media worker operation.
/// This gate must not be opened by a caller-controlled bool or by a successful
/// IPC handshake. A Windows sandbox proof must be implemented before media
/// processing can be admitted.
/// </summary>
public static class MediaWorkerAdmission
{
    public static bool IsMediaProcessingAvailable => false;

    public static void DemandMediaProcessing()
    {
        throw new UnauthorizedAccessException(
            "POC-003.1: Windows sandbox is not implemented and verified; " +
            "media worker processing is prohibited. No unrestricted fallback is allowed.");
    }

    public static AccessDecision AuthorizeMediaRequest(
        AccessGrantRegistry registry, WorkerRequest request)
    {
        ArgumentNullException.ThrowIfNull(registry);
        // Even a valid controller grant does not authorize an unsandboxed worker.
        // Preserve protocol and grant diagnostics without issuing media access.
        var decision = registry.Authorize(request);
        return decision == AccessDecision.Allowed &&
               request.Operation != MediaAccessOperation.Heartbeat
            ? AccessDecision.Unauthorized
            : decision;
    }
}
