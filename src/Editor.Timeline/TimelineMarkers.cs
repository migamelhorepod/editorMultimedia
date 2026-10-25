namespace Editor.Timeline;

/// <summary>Immutable, undoable timeline guides, independent of clip/media data.</summary>
public static partial class TimelineEngine
{
    private static CommandResult CommitMarkers(TimelineState old, IReadOnlyList<TimelineMarker> markers)
    {
        var next = new TimelineState(checked(old.Version + 1), old.Clips.ToArray(), markers.ToArray());
        Validate(next);
        return new CommandResult(next, Array.Empty<Guid>());
    }

    public static CommandResult AddMarker(TimelineState state, long version, TimelineMarker marker)
    {
        Check(state, version);
        return CommitMarkers(state, [..state.MarkerItems, marker]);
    }

    public static CommandResult RemoveMarker(TimelineState state, long version, Guid markerId)
    {
        Check(state, version);
        if (state.MarkerItems.All(m => m.Id != markerId))
            throw new TimelineException("MARKER_NOT_FOUND", "Marker does not exist.");
        return CommitMarkers(state, state.MarkerItems.Where(m => m.Id != markerId).ToArray());
    }

    public static CommandResult MoveMarker(TimelineState state, long version, Guid markerId, MediaTime to)
    {
        Check(state, version);
        if (state.MarkerItems.All(m => m.Id != markerId))
            throw new TimelineException("MARKER_NOT_FOUND", "Marker does not exist.");
        return CommitMarkers(state, state.MarkerItems.Select(m => m.Id == markerId ? m with { At = to } : m).ToArray());
    }
}
