namespace Editor.Timeline;

/// <summary>
/// Single undo/redo history for timeline edits and media-catalog changes.
/// Saving remains explicit and does not change the undo history.
/// </summary>
public sealed class TimelineSession
{
    private sealed record Snapshot(TimelineState Timeline, IReadOnlyList<MediaAsset> Media);

    private readonly Stack<Snapshot> undo = new();
    private readonly Stack<Snapshot> redo = new();

    public TimelineState Current { get; private set; }
    public IReadOnlyList<MediaAsset> Media { get; private set; }
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public TimelineSession(TimelineState initial, IReadOnlyList<MediaAsset>? media = null)
    {
        TimelineEngine.Validate(initial);
        var catalog = media?.ToArray() ?? Array.Empty<MediaAsset>();
        MediaReferences.ValidateCatalog(catalog, initial);
        Current = initial;
        Media = catalog;
    }

    public CommandResult Execute(Func<TimelineState, CommandResult> command,
        IReadOnlyList<MediaAsset>? nextMedia = null)
    {
        var result = command(Current);
        if (result.State.Version != Current.Version + 1)
            throw new TimelineException("INVALID_COMMAND", "Command must produce next version.");
        var candidateMedia = nextMedia?.ToArray() ?? Media;
        // Validate physical source bounds only for ranges actually created or edited.
        // Legacy clips may exceed newly measured source lengths; merely refreshing
        // metadata, moving clips or saving may not silently erase user edits.
        var verified = MediaReferences.VerifyEditedRanges(Current, result.State, candidateMedia);
        if (!ReferenceEquals(verified, result.State))
            result = result with { State = verified };
        MediaReferences.ValidateCatalog(candidateMedia, result.State);

        // Every operation commits both pieces together, or neither.
        undo.Push(new Snapshot(Current, Media));
        redo.Clear();
        Current = result.State;
        Media = candidateMedia;
        return result;
    }

    /// <summary>
    /// Relink an existing media ID to a user-selected file without changing its
    /// timeline occurrences, associations, trims, effects or markers.
    /// This counts as one reversible project edit.
    /// </summary>
    public void RelinkMedia(Guid assetId, string chosenPath)
    {
        var next = MediaReferences.Relink(Media, assetId, chosenPath);
        if (Media.SequenceEqual(next)) return;
        Execute(s => new CommandResult(
            s with { Version = checked(s.Version + 1) }, Array.Empty<Guid>()), next);
    }

    /// <summary>Refresh one imported file's measurements with a reversible edit.</summary>
    public void RefreshMediaMetadata(Guid mediaId)
    {
        var next = MediaReferences.RefreshMetadata(Media, mediaId);
        if (Media.SequenceEqual(next)) return;
        Execute(s => new CommandResult(
            s with { Version = checked(s.Version + 1) }, Array.Empty<Guid>()), next);
    }

    /// <summary>Apply already verified worker metadata without probing media in the editor process.</summary>
    public void RefreshMediaMetadataInspected(Guid mediaId, MediaAsset inspected)
    {
        var previous = Media.FirstOrDefault(m => m.Id == mediaId)
            ?? throw new MediaReferenceException("Mídia não encontrada no catálogo.");
        if (previous.Kind != inspected.Kind ||
            !MediaReferences.SameSource(previous.SourcePath, inspected.SourcePath))
            throw new MediaReferenceException("Arquivo ou tipo não corresponde à mídia atual.");
        var next = Media.Select(m => m.Id == mediaId ? inspected with { Id = mediaId } : m).ToArray();
        if (Media.SequenceEqual(next)) return;
        Execute(s => new CommandResult(
            s with { Version = checked(s.Version + 1) }, Array.Empty<Guid>()), next);
    }

    /// <summary>Relink to worker-verified replacement, preserving the media ID and undo history.</summary>
    public void RelinkMediaInspected(Guid mediaId, MediaAsset inspected)
    {
        var previous = Media.FirstOrDefault(m => m.Id == mediaId)
            ?? throw new MediaReferenceException("Mídia não encontrada no catálogo.");
        if (previous.Kind != inspected.Kind)
            throw new MediaReferenceException("Selecione uma mídia do mesmo tipo.");
        if (inspected.Kind is MediaKind.Video or MediaKind.Audio &&
            inspected.DurationTicks is null)
            throw new MediaReferenceException("A duração da substituta não foi aferida pelo worker.");
        if (Media.Any(m => m.Id != mediaId &&
            MediaReferences.SameSource(m.SourcePath, inspected.SourcePath)))
            throw new MediaReferenceException("A mídia substituta já existe no catálogo.");
        var next = Media.Select(m => m.Id == mediaId ? inspected with { Id = mediaId } : m).ToArray();
        if (Media.SequenceEqual(next)) return;
        Execute(s => new CommandResult(
            s with { Version = checked(s.Version + 1) }, Array.Empty<Guid>()), next);
    }

    /// <summary>Restore trim using metadata supplied by the verified AppContainer worker.</summary>
    public void FitClipToMeasuredSource(Guid clipId, MediaAsset inspected)
    {
        var clip = Current.Find(clipId);
        if (clip.Kind == MediaKind.Image)
            throw new MediaReferenceException("Imagens possuem duração editável.");
        var asset = Media.FirstOrDefault(m => m.Id == clip.MediaId)
            ?? throw new MediaReferenceException("Mídia não vinculada ao projeto.");
        if (asset.Kind != inspected.Kind ||
            !MediaReferences.SameSource(asset.SourcePath, inspected.SourcePath) ||
            inspected.DurationTicks is not long ticks)
            throw new MediaReferenceException("Os metadados da mídia não foram confirmados pelo worker.");
        var refreshed = Media.Select(m => m.Id == asset.Id ? inspected with { Id = asset.Id } : m).ToArray();
        if (clip.Duration.Ticks == ticks && clip.SourceStart == MediaTime.Zero &&
            Media.SequenceEqual(refreshed)) return;
        var desired = new MediaTime(ticks);
        Execute(state =>
        {
            var adjusted = clip.Track == TimelineEngine.MainTrack
                ? TimelineEngine.RestoreMainDuration(state, state.Version, clip.Id, desired)
                : TimelineEngine.RestoreSecondaryDuration(state, state.Version, clip.Id, desired);
            var nextClips = adjusted.State.Clips.Select(c => c.Id == clip.Id
                ? c with { SourceStart = MediaTime.Zero } : c).ToArray();
            var next = adjusted.State with { Clips = nextClips };
            TimelineEngine.Validate(next);
            return adjusted with { State = next };
        }, refreshed);
    }

    /// <summary>
    /// Explicitly replace a clip's current trim with the measured source duration.
    /// Also refresh old project metadata if needed. Both changes form one undo
    /// entry; every following main-track clip is rippled by the timeline engine.
    /// </summary>
    public void FitClipToSource(Guid clipId)
    {
        var clip = Current.Find(clipId);
        if (clip.Kind == MediaKind.Image)
            throw new MediaReferenceException(
                "Imagens possuem duração editável, não duração original aferida.");
        var asset = Media.FirstOrDefault(m => m.Id == clip.MediaId)
            ?? throw new MediaReferenceException("Este clipe não possui arquivo vinculado.");

        // An explicit restore action reads the current file rather than trusting
        // potentially old duration measurements persisted in the project.
        var refreshed = MediaReferences.RefreshMetadata(Media, asset.Id);
        var measured = refreshed.First(m => m.Id == asset.Id).DurationTicks;
        if (measured is not long ticks)
            throw new MediaReferenceException(
                "A duração real desta mídia não pôde ser determinada. " +
                "Nenhum corte foi alterado.");
        if (clip.Duration.Ticks == ticks && clip.SourceStart == MediaTime.Zero &&
            Media.SequenceEqual(refreshed))
            return;

        var desired = new MediaTime(ticks);
        Execute(state =>
        {
            var adjusted = clip.Track == TimelineEngine.MainTrack
                ? TimelineEngine.RestoreMainDuration(state, state.Version, clip.Id, desired)
                : TimelineEngine.RestoreSecondaryDuration(state, state.Version, clip.Id, desired);
            var clips = adjusted.State.Clips.Select(c => c.Id == clip.Id
                ? c with { SourceStart = MediaTime.Zero } : c).ToArray();
            var next = adjusted.State with { Clips = clips };
            TimelineEngine.Validate(next);
            return adjusted with { State = next };
        }, refreshed);
    }

    /// <summary>Forget an unused reference without deleting its source file.</summary>
    public void RemoveUnusedMedia(Guid mediaId)
    {
        var next = MediaReferences.RemoveUnused(Media, Current, mediaId);
        Execute(s => new CommandResult(
            s with { Version = checked(s.Version + 1) }, Array.Empty<Guid>()), next);
    }

    /// <summary>
    /// Remove one asset and every occurrence on the timeline as one undoable
    /// project change. Source files on disk are never deleted.
    /// </summary>
    public void DeleteCatalogMedia(Guid mediaId)
    {
        if (!Media.Any(m => m.Id == mediaId))
            throw new MediaReferenceException("Mídia não encontrada no catálogo.");
        var nextMedia = Media.Where(m => m.Id != mediaId).ToArray();
        Execute(state =>
        {
            var working = state;
            var affected = new HashSet<Guid>();
            // Delete main occurrences first so the same reflow/detachment
            // semantics as normal timeline deletion are preserved.
            var ids = state.Clips.Where(c => c.MediaId == mediaId)
                .OrderBy(c => c.Track == TimelineEngine.MainTrack ? 0 : 1)
                .ThenBy(c => c.Start.Ticks)
                .Select(c => c.Id).ToArray();
            foreach (var id in ids)
            {
                var clip = working.Find(id);
                var result = clip.Track == TimelineEngine.MainTrack
                    ? TimelineEngine.DeleteMain(working, working.Version, id)
                    : TimelineEngine.DeleteSecondary(working, working.Version, id);
                working = result.State;
                affected.UnionWith(result.ChangedClipIds);
            }
            working = working with { Version = checked(state.Version + 1) };
            TimelineEngine.Validate(working);
            return new CommandResult(working, affected.ToArray());
        }, nextMedia);
    }

    public TimelineState Undo()
    {
        if (!CanUndo) throw new TimelineException("NOTHING_TO_UNDO", "Undo stack is empty.");
        redo.Push(new Snapshot(Current, Media));
        var previous = undo.Pop();
        Current = previous.Timeline;
        Media = previous.Media;
        return Current;
    }

    public TimelineState Redo()
    {
        if (!CanRedo) throw new TimelineException("NOTHING_TO_REDO", "Redo stack is empty.");
        undo.Push(new Snapshot(Current, Media));
        var next = redo.Pop();
        Current = next.Timeline;
        Media = next.Media;
        return Current;
    }
}
