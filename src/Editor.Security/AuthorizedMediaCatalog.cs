namespace Editor.Security;

/// <summary>
/// Controller-owned resource-to-file binding for the active library.
/// Validation is necessary, but NOT sufficient without an OS ACL grant.
/// Never send a filesystem path as authority from the worker.
/// </summary>
public sealed class AuthorizedMediaCatalog
{
    private readonly AccessGrantRegistry grants;
    private readonly string root;
    private readonly Dictionary<Guid, string> paths = new();
    private readonly object gate = new();

    public AuthorizedMediaCatalog(string libraryRoot, AccessGrantRegistry grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (string.IsNullOrWhiteSpace(libraryRoot) ||
            !Path.IsPathFullyQualified(libraryRoot))
            throw new ArgumentException("Absolute active-library root required.", nameof(libraryRoot));
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        if (!Directory.Exists(root) || IsReparsePoint(root))
            throw new UnauthorizedAccessException("Active library root is unavailable or redirected.");
        this.grants = grants;
    }

    public void Register(Guid resourceId, string physicalFile)
    {
        if (resourceId == Guid.Empty) throw new ArgumentException("Missing resource ID.");
        string path = ValidateFile(physicalFile);
        lock (gate) paths.Add(resourceId, path);
    }

    public string Resolve(WorkerRequest request)
    {
        if (request.Operation != MediaAccessOperation.InspectMetadata ||
            grants.Authorize(request) != AccessDecision.Allowed)
            throw new UnauthorizedAccessException("Task, library, resource or operation is not authorized.");
        string file;
        lock (gate)
        {
            if (!paths.TryGetValue(request.ResourceId, out file!))
                throw new UnauthorizedAccessException("Resource is not registered in the active library.");
        }
        return ValidateFile(file); // Reject file swaps and reparse points at access time.
    }

    private string ValidateFile(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file))
            throw new UnauthorizedAccessException("Non-absolute media path.");
        string normalized = Path.GetFullPath(file);
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Media path is outside the active library.");
        if (!File.Exists(normalized)) throw new FileNotFoundException("Media missing.", normalized);
        string cursor = Path.GetDirectoryName(normalized)!;
        while (cursor.Length >= root.Length)
        {
            if (IsReparsePoint(cursor))
                throw new UnauthorizedAccessException("Junction or symlink in media parent chain.");
            if (string.Equals(cursor, root, StringComparison.OrdinalIgnoreCase)) break;
            cursor = Path.GetDirectoryName(cursor)
                ?? throw new UnauthorizedAccessException("Invalid media ancestor.");
        }
        if (IsReparsePoint(normalized))
            throw new UnauthorizedAccessException("Symlinked media is prohibited.");
        return normalized;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
