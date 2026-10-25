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

    /// <summary>Forget an unused reference without deleting its source file.</summary>
    public void RemoveUnusedMedia(Guid mediaId)
    {
        var next = MediaReferences.RemoveUnused(Media, Current, mediaId);
        Execute(s => new CommandResult(
            s with { Version = checked(s.Version + 1) }, Array.Empty<Guid>()), next);
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
