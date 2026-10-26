namespace Editor.Timeline;

public enum MediaKind { Video, Image, Audio }
public enum AssociationKind { Independent, Positioned }

/// <summary>Time in 100-nanosecond ticks. UI pixels and source FPS never define time.</summary>
public readonly record struct MediaTime(long Ticks) : IComparable<MediaTime>
{
    public static MediaTime Zero => new(0);
    public static MediaTime FromSeconds(decimal seconds) => new(checked((long)(seconds * 10_000_000m)));
    public decimal Seconds => Ticks / 10_000_000m;
    public int CompareTo(MediaTime other) => Ticks.CompareTo(other.Ticks);
    public static MediaTime operator +(MediaTime a, MediaTime b) => new(checked(a.Ticks + b.Ticks));
    public static MediaTime operator -(MediaTime a, MediaTime b) => new(checked(a.Ticks - b.Ticks));
}

public sealed record Clip(
    Guid Id, Guid MediaId, MediaKind Kind, int Track,
    MediaTime Start, MediaTime Duration,
    AssociationKind Association = AssociationKind.Independent,
    Guid? ParentId = null, MediaTime ParentOffset = default,
    bool OrphanNotice = false,
    string? EffectsToken = null,
    MediaTime SourceStart = default)
{
    public MediaTime End => Start + Duration;
    public MediaTime SourceEnd => SourceStart + Duration;
}

/// <summary>Timeline guide only: never a media source and never part of a clip association.</summary>
public sealed record TimelineMarker(Guid Id, MediaTime At, string Label);

public sealed record TimelineState(long Version, IReadOnlyList<Clip> Clips, IReadOnlyList<TimelineMarker>? Markers = null)
{
    public IReadOnlyList<TimelineMarker> MarkerItems => Markers ?? Array.Empty<TimelineMarker>();
    public MediaTime Duration => Clips.Count == 0 ? MediaTime.Zero : Clips.Max(c => c.End);
    public Clip Find(Guid id) => Clips.FirstOrDefault(c => c.Id == id)
        ?? throw new TimelineException("CLIP_NOT_FOUND", $"Clip {id} was not found.");
}

public sealed class TimelineException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record CommandResult(TimelineState State, IReadOnlyList<Guid> ChangedClipIds);
