namespace Editor.Security;

/// <summary>
/// Controller-facing, fail-closed entrypoint for the *real* Windows AppContainer
/// metadata and decode operations. Each invocation is registered and granted
/// only to the active library, exact file, exact operation and task lifetime.
/// Worker binaries are never started outside the verified launcher.
/// </summary>
public sealed class IsolatedMediaProcessingService
{
    private readonly AccessGrantRegistry grants;
    private readonly AuthorizedMediaCatalog catalog;
    private readonly string workerExecutable;
    private readonly string activeLibraryRoot;

    public IsolatedMediaProcessingService(string activeLibraryRoot,
        Guid activeLibraryId, string workerExecutable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ArgumentException.ThrowIfNullOrWhiteSpace(workerExecutable);
        if (!Path.IsPathFullyQualified(workerExecutable) ||
            !string.Equals(Path.GetFileName(workerExecutable),
                "Editor.Security.Worker.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(workerExecutable))
            throw new UnauthorizedAccessException("The verified AppContainer worker is not installed.");
        string bundle = Path.GetDirectoryName(Path.GetFullPath(workerExecutable))!;
        if (!File.Exists(Path.Combine(bundle, "ffprobe.exe")) ||
            !File.Exists(Path.Combine(bundle, "ffmpeg.exe")))
            throw new UnauthorizedAccessException("The isolated codec bundle is incomplete.");
        this.workerExecutable = Path.GetFullPath(workerExecutable);
        grants = new AccessGrantRegistry(activeLibraryId);
        catalog = new AuthorizedMediaCatalog(activeLibraryRoot, grants);
        this.activeLibraryRoot = Path.GetFullPath(activeLibraryRoot);
        // Reclaim only crash-abandoned, exclusively leased codec copies.
        // Active tasks in another instance retain the lease and are untouched.
        RecoverAbandonedInputs();
    }

    public int RecoverAbandonedInputs() =>
        WindowsScopedCodecInput.RecoverAbandoned(activeLibraryRoot);

    public long InspectDurationTicks(string registeredMediaPath)
    {
        Guid task = Guid.NewGuid(), resource = Guid.NewGuid();
        try
        {
            catalog.Register(resource, registeredMediaPath);
            Guid grant = grants.Issue(task, grants.ActiveLibraryId, resource,
                MediaAccessOperation.InspectMetadata, TimeSpan.FromMinutes(15));
            var request = new WorkerRequest(WorkerRequest.CurrentVersion, Guid.NewGuid(),
                task, grants.ActiveLibraryId, resource, grant, MediaAccessOperation.InspectMetadata);
            return WindowsAppContainerLauncher.ReadAuthorizedMetadataTicks(
                workerExecutable, catalog, request);
        }
        finally
        {
            grants.RevokeTask(task);
            catalog.Unregister(resource);
        }
    }

    public void DecodeToNull(string registeredMediaPath)
    {
        Guid task = Guid.NewGuid(), resource = Guid.NewGuid();
        try
        {
            catalog.Register(resource, registeredMediaPath);
            Guid grant = grants.Issue(task, grants.ActiveLibraryId, resource,
                MediaAccessOperation.ReadChunk, TimeSpan.FromMinutes(15));
            var request = new WorkerRequest(WorkerRequest.CurrentVersion, Guid.NewGuid(),
                task, grants.ActiveLibraryId, resource, grant, MediaAccessOperation.ReadChunk);
            int exitCode = WindowsAppContainerLauncher.RunAuthorizedDecodeProcessing(
                workerExecutable, catalog, request);
            if (exitCode != 0)
                throw new InvalidDataException(
                    "Isolated FFmpeg could not decode this media. Worker status: " + exitCode);
        }
        finally
        {
            grants.RevokeTask(task);
            catalog.Unregister(resource);
        }
    }

    /// <summary>
    /// Do not expose raw worker paths or user-controlled command lines.
    /// A closed service permanently rejects new task grants.
    /// </summary>
    public void RevokeAll() => grants.DeactivateLibrary();
}
