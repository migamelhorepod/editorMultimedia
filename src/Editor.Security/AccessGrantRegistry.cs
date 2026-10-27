using System.Security.Cryptography;

namespace Editor.Security;

/// <summary>
/// The worker never sends a filesystem path. Only the controller owns
/// resource-to-path mappings and decides which operations are permitted.
/// </summary>
public enum MediaAccessOperation
{
    Heartbeat = 0,
    InspectMetadata = 1,
    ReadChunk = 2
}

public enum AccessDecision
{
    Allowed,
    InvalidProtocol,
    Unauthorized,
    Expired,
    Revoked
}

public sealed record WorkerRequest(
    int ProtocolVersion,
    Guid RequestId,
    Guid TaskId,
    Guid LibraryId,
    Guid ResourceId,
    Guid GrantId,
    MediaAccessOperation Operation)
{
    public const int CurrentVersion = 1;

    public static WorkerRequest Heartbeat() =>
        new(CurrentVersion, Guid.NewGuid(), Guid.Empty, Guid.Empty,
            Guid.Empty, Guid.Empty, MediaAccessOperation.Heartbeat);
}

public sealed record WorkerResponse(int ProtocolVersion, Guid RequestId, AccessDecision Decision)
{
    public bool Accepted => Decision == AccessDecision.Allowed;
}

/// <summary>
/// In-memory, bounded, fail-closed authorization registry for one ACTIVE library.
/// Authorization here is necessary but NOT sufficient for filesystem isolation.
/// The Windows sandbox must enforce an independent kernel-level boundary before
/// any untrusted media processing is enabled.
/// </summary>
public sealed class AccessGrantRegistry
{
    public const int MaxGrants = 1024;
    public static readonly TimeSpan MaxGrantLifetime = TimeSpan.FromMinutes(5);

    private sealed record Lease(
        Guid TaskId, Guid LibraryId, Guid ResourceId,
        MediaAccessOperation Operation, long IssuedTimestamp, TimeSpan Lifetime);

    private readonly object gate = new();
    private readonly Dictionary<Guid, Lease> grants = new();
    private readonly HashSet<Guid> revokedTasks = [];
    private readonly TimeProvider clock;
    private bool inactive;

    public AccessGrantRegistry(Guid activeLibraryId, TimeProvider? clock = null)
    {
        if (activeLibraryId == Guid.Empty)
            throw new ArgumentException("Active library ID must be nonempty.", nameof(activeLibraryId));
        ActiveLibraryId = activeLibraryId;
        this.clock = clock ?? TimeProvider.System;
    }

    public Guid ActiveLibraryId { get; }

    public Guid Issue(Guid taskId, Guid libraryId, Guid resourceId,
        MediaAccessOperation operation, TimeSpan lifetime)
    {
        if (taskId == Guid.Empty || libraryId != ActiveLibraryId ||
            resourceId == Guid.Empty || !IsAuthorizedOperation(operation) ||
            lifetime <= TimeSpan.Zero || lifetime > MaxGrantLifetime)
            throw new ArgumentException("Invalid grant scope or lifetime.");

        lock (gate)
        {
            if (inactive || revokedTasks.Contains(taskId))
                throw new InvalidOperationException("Library is inactive or task was cancelled.");

            PruneExpired();
            if (grants.Count >= MaxGrants)
                throw new InvalidOperationException("Active grant capacity exhausted.");

            // Guid.NewGuid uses a cryptographically strong random source on .NET.
            var id = Guid.NewGuid();
            grants.Add(id, new Lease(taskId, libraryId, resourceId,
                operation, clock.GetTimestamp(), lifetime));
            return id;
        }
    }

    public AccessDecision Authorize(WorkerRequest request)
    {
        if (request.ProtocolVersion != WorkerRequest.CurrentVersion ||
            request.RequestId == Guid.Empty ||
            !Enum.IsDefined(request.Operation))
            return AccessDecision.InvalidProtocol;

        // Heartbeats have no resource permissions and do not grant media access.
        if (request.Operation == MediaAccessOperation.Heartbeat)
            return request.TaskId == Guid.Empty && request.LibraryId == Guid.Empty &&
                request.ResourceId == Guid.Empty && request.GrantId == Guid.Empty
                ? AccessDecision.Allowed : AccessDecision.InvalidProtocol;

        if (!IsAuthorizedOperation(request.Operation) ||
            request.TaskId == Guid.Empty || request.LibraryId != ActiveLibraryId ||
            request.ResourceId == Guid.Empty || request.GrantId == Guid.Empty)
            return AccessDecision.Unauthorized;

        lock (gate)
        {
            if (inactive || revokedTasks.Contains(request.TaskId))
                return AccessDecision.Revoked;
            if (!grants.TryGetValue(request.GrantId, out var lease))
                return AccessDecision.Unauthorized;
            if (lease.TaskId != request.TaskId || lease.LibraryId != request.LibraryId ||
                lease.ResourceId != request.ResourceId || lease.Operation != request.Operation)
                return AccessDecision.Unauthorized;
            if (clock.GetElapsedTime(lease.IssuedTimestamp) >= lease.Lifetime)
            {
                grants.Remove(request.GrantId);
                return AccessDecision.Expired;
            }
            return AccessDecision.Allowed;
        }
    }

    public void RevokeGrant(Guid grantId)
    {
        lock (gate) grants.Remove(grantId);
    }

    public void RevokeTask(Guid taskId)
    {
        if (taskId == Guid.Empty) return;
        lock (gate)
        {
            revokedTasks.Add(taskId);
            foreach (var key in grants.Where(p => p.Value.TaskId == taskId)
                .Select(p => p.Key).ToArray())
                grants.Remove(key);
        }
    }

    public void DeactivateLibrary()
    {
        lock (gate)
        {
            inactive = true;
            grants.Clear();
            revokedTasks.Clear();
        }
    }

    private void PruneExpired()
    {
        foreach (var id in grants.Where(p =>
            clock.GetElapsedTime(p.Value.IssuedTimestamp) >= p.Value.Lifetime)
            .Select(p => p.Key).ToArray())
            grants.Remove(id);
    }

    private static bool IsAuthorizedOperation(MediaAccessOperation op) =>
        op is MediaAccessOperation.InspectMetadata or MediaAccessOperation.ReadChunk;
}
