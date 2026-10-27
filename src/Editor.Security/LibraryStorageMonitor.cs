namespace Editor.Security;

/// <summary>Checks physical storage availability without treating transient missing media as a user cancellation.</summary>
public sealed class LibraryStorageMonitor
{
    private readonly MediaImportCoordinator coordinator;
    private readonly string root;
    private readonly Func<string, bool> available;
    private readonly Action<Guid> terminateAffected;
    private bool wasAvailable = true;

    public LibraryStorageMonitor(MediaImportCoordinator coordinator, string root,
        Action<Guid> terminateAffected, Func<string, bool>? available = null)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.terminateAffected = terminateAffected ?? throw new ArgumentNullException(nameof(terminateAffected));
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new ArgumentException("An absolute library root is required.", nameof(root));
        this.root = Path.GetFullPath(root);
        this.available = available ?? Directory.Exists;
    }

    public bool Check()
    {
        bool present = available(root);
        if (!present)
        {
            // Mark unavailable before notifying any owner; the task cannot be
            // mistaken for a voluntary cancellation.
            foreach (var task in coordinator.MarkStorageUnavailable())
                terminateAffected(task);
        }
        else if (!wasAvailable)
            coordinator.MarkStorageAvailable();
        wasAvailable = present;
        return present;
    }
}
