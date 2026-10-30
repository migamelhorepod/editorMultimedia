namespace Editor.Security;

/// <summary>
/// POC-003.2 controller-side lifecycle for non-cancellable imports.
/// Revocation is reserved for faults and unavailable storage, never a UI cancel.
/// This does NOT close kernel handles; the owning Job must be disposed on fault.
/// </summary>
public sealed class MediaImportCoordinator
{
    public const string BusyMessage = "Aguarde o processamento ser finalizado";
    public const string FatalMessage = "Ocorreu uma falha e o aplicativo precisará ser finalizado";
    public static readonly TimeSpan MaxFailureShutdown = TimeSpan.FromSeconds(30);

    private readonly object gate = new();
    private readonly HashSet<Guid> active = [];
    private bool unavailable;
    private bool physicalPresent = true;
    private readonly HashSet<Guid> drainingAfterFailure = [];
    private Guid library;

    public MediaImportCoordinator(Guid initialLibrary)
    {
        if (initialLibrary == Guid.Empty) throw new ArgumentException("Library ID is required.", nameof(initialLibrary));
        library = initialLibrary;
    }

    public Guid ActiveLibrary { get { lock (gate) return library; } }
    public int ActiveImports { get { lock (gate) return active.Count; } }

    // An import can commit changes to the current TimelineSession after await.
    // Project replacement, saving and timeline mutations must not race with it.
    public bool CanMutateProject { get { lock (gate) return active.Count == 0; } }

    public void DemandProjectMutationAllowed()
    {
        if (!CanMutateProject)
            throw new InvalidOperationException(BusyMessage);
    }

    public Guid BeginImport()
    {
        lock (gate)
        {
            if (unavailable || library == Guid.Empty) throw new IOException("No active and available library.");
            var id = Guid.NewGuid();
            active.Add(id);
            return id;
        }
    }

    public void CompleteImport(Guid taskId)
    {
        lock (gate)
        {
            if (!active.Remove(taskId))
                throw new InvalidOperationException("Import task is not active.");
            drainingAfterFailure.Remove(taskId);
            if (physicalPresent && drainingAfterFailure.Count == 0) unavailable = false;
        }
    }

    public LibraryTransitionResult TryChangeLibrary(Guid? nextLibrary)
    {
        if (nextLibrary == Guid.Empty) throw new ArgumentException("Invalid target library.", nameof(nextLibrary));
        lock (gate)
        {
            if (active.Count != 0) return new(false, BusyMessage);
            // Storage loss must not prevent selecting a different library
            // once all affected workers have finished terminating.
            library = nextLibrary ?? Guid.Empty;
            unavailable = false;
            physicalPresent = true;
            drainingAfterFailure.Clear();
            return new(true, null);
        }
    }

    public IReadOnlyList<Guid> MarkStorageUnavailable()
    {
        lock (gate)
        {
            unavailable = true;
            physicalPresent = false;
            var affected = active.Where(id => drainingAfterFailure.Add(id)).ToArray();
            return affected;
        }
    }

    public void MarkStorageAvailable()
    {
        lock (gate)
        {
            physicalPresent = true;
            unavailable = drainingAfterFailure.Count != 0;
        }
    }

    // No user-facing CancelImport method by design.
}
public sealed record LibraryTransitionResult(bool Allowed, string? PopupMessage);
