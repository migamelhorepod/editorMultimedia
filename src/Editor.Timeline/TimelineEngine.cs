namespace Editor.Timeline;

/// <summary>Pure domain operations. All changes are atomic: an exception leaves input state unchanged.</summary>
public static partial class TimelineEngine
{
    public const int MaxTracks = 10;
    public const int MainTrack = 0;

    public static void Validate(TimelineState state)
    {
        if (state.Version < 0) throw new TimelineException("INVALID_VERSION", "Negative version.");
        var ids = new HashSet<Guid>();
        foreach (var c in state.Clips)
        {
            if (c.Id == Guid.Empty || c.MediaId == Guid.Empty || !ids.Add(c.Id))
                throw new TimelineException("INVALID_ID", "Missing or duplicate identifier.");
            if (c.Track is < 0 or >= MaxTracks || c.Start.Ticks < 0 || c.Duration.Ticks <= 0)
                throw new TimelineException("INVALID_CLIP", "Invalid track, start or duration.");
            try { _ = c.End; _ = c.SourceEnd; }
            catch (OverflowException)
            { throw new TimelineException("TIME_OVERFLOW", "Timeline or media source time overflow."); }
            if (c.Track == MainTrack && c.Association != AssociationKind.Independent)
                throw new TimelineException("INVALID_MAIN_ASSOCIATION", "Main clips cannot be positioned.");
            if (c.Association == AssociationKind.Independent && (c.ParentId is not null || c.ParentOffset != MediaTime.Zero))
                throw new TimelineException("INVALID_ASSOCIATION", "Independent clips cannot retain parent metadata.");
            if (c.Association == AssociationKind.Positioned)
            {
                if (c.Track == MainTrack || c.ParentId is null)
                    throw new TimelineException("INVALID_ASSOCIATION", "Positioned clip requires main parent.");
                var parent = state.Clips.FirstOrDefault(x => x.Id == c.ParentId && x.Track == MainTrack);
                if (parent is null || c.ParentOffset.Ticks < 0 || c.ParentOffset.Ticks >= parent.Duration.Ticks ||
                    c.Start != parent.Start + c.ParentOffset)
                    throw new TimelineException("INVALID_ASSOCIATION", "Positioned clip has invalid parent or start.");
            }
        }
        var markerPositions = new HashSet<long>();
        foreach (var marker in state.MarkerItems)
        {
            if (marker.Id == Guid.Empty || !ids.Add(marker.Id) || marker.At.Ticks < 0 ||
                string.IsNullOrWhiteSpace(marker.Label) || marker.Label.Length > 60)
                throw new TimelineException("INVALID_MARKER", "Invalid marker ID, time or label.");
            if (!markerPositions.Add(marker.At.Ticks))
                throw new TimelineException("DUPLICATE_MARKER", "A marker already exists at this time.");
        }
        foreach (var track in state.Clips.GroupBy(c => c.Track))
        {
            var sorted = track.OrderBy(c => c.Start.Ticks).ThenBy(c => c.Id).ToList();
            for (int i = 1; i < sorted.Count; i++)
                if (sorted[i - 1].End.Ticks > sorted[i].Start.Ticks)
                    throw new TimelineException("OVERLAP", "Clips overlap on a track.");
        }
    }

    private static CommandResult Commit(TimelineState old, List<Clip> clips)
    {
        var next = new TimelineState(checked(old.Version + 1), clips.ToArray(), old.MarkerItems.ToArray());
        Validate(next);
        var before = old.Clips.ToDictionary(c => c.Id);
        var after = next.Clips.ToDictionary(c => c.Id);
        var changed = before.Keys.Union(after.Keys).Where(id => !before.TryGetValue(id, out var a) ||
            !after.TryGetValue(id, out var b) || a != b).ToArray();
        return new CommandResult(next, changed);
    }

    private static void Check(TimelineState state, long expectedVersion)
    {
        Validate(state);
        if (state.Version != expectedVersion)
            throw new TimelineException("VERSION_CONFLICT", "Stale timeline version.");
    }

    private static Clip Detach(Clip c, bool notify = false) => c with
    {
        Association = AssociationKind.Independent, ParentId = null,
        ParentOffset = MediaTime.Zero, OrphanNotice = notify || c.OrphanNotice
    };

    /// <summary>Associate a secondary clip with the main clip containing its start.
    /// An explicit independent choice is respected until the user associates it again.</summary>
    private static Clip AutoAssociate(TimelineState state, Clip clip)
    {
        if (clip.Track == MainTrack || clip.Association == AssociationKind.Positioned) return clip;
        var parent = state.Clips.SingleOrDefault(c => c.Track == MainTrack &&
            c.Start.Ticks <= clip.Start.Ticks && clip.Start.Ticks < c.End.Ticks);
        return parent is null ? clip : clip with
        {
            Association = AssociationKind.Positioned, ParentId = parent.Id,
            ParentOffset = clip.Start - parent.Start, OrphanNotice = false
        };
    }

    public static CommandResult SetIndependent(TimelineState state, long version, Guid id)
    {
        Check(state, version);
        var clip = state.Find(id);
        if (clip.Track == MainTrack) throw new TimelineException("NOT_SECONDARY", "Select a secondary clip.");
        return Commit(state, state.Clips.Select(c => c.Id == id ? Detach(c) with { OrphanNotice = false } : c).ToList());
    }

    public static CommandResult AssociateAtStart(TimelineState state, long version, Guid id)
    {
        Check(state, version);
        var clip = state.Find(id);
        if (clip.Track == MainTrack) throw new TimelineException("NOT_SECONDARY", "Select a secondary clip.");
        var associated = AutoAssociate(state, Detach(clip) with { OrphanNotice = false });
        if (associated.Association == AssociationKind.Independent)
            throw new TimelineException("NO_MAIN_AT_START", "No main clip contains the secondary start.");
        return Commit(state, state.Clips.Select(c => c.Id == id ? associated : c).ToList());
    }

    public static CommandResult Insert(TimelineState state, long version, Clip clip)
    {
        Check(state, version);
        return Commit(state, [..state.Clips, AutoAssociate(state, clip)]);
    }

    /// <summary>Duplicate a secondary clip at its current time on the first non-colliding
    /// secondary track. Preserve media, effects and explicit independence.</summary>
    public static CommandResult DuplicateSecondary(TimelineState state, long version, Guid id, Guid newId)
    {
        Check(state, version);
        var source = state.Find(id);
        if (source.Track == MainTrack)
            throw new TimelineException("NOT_SECONDARY", "Only secondary clips can be duplicated.");
        if (newId == Guid.Empty || state.Clips.Any(c => c.Id == newId))
            throw new TimelineException("INVALID_ID", "Duplicate identifier.");
        bool Fits(int lane) => state.Clips.Where(c => c.Track == lane)
            .All(c => source.End.Ticks <= c.Start.Ticks || source.Start.Ticks >= c.End.Ticks);
        var destination = Enumerable.Range(1, MaxTracks - 1).Where(Fits).DefaultIfEmpty(-1).First();
        if (destination < 0)
            throw new TimelineException("NO_FREE_TRACK", "No free secondary track.");
        var copy = source with { Id = newId, Track = destination };
        return Commit(state, [..state.Clips, copy]);
    }

    /// <summary>Read-only preview of secondary drag, including automatic lane selection.
    /// Returns a new state but never mutates the supplied state or undo history.</summary>
    public static CommandResult PreviewDragSecondary(TimelineState state, long version, Guid id, int track, MediaTime start)
        => DragSecondary(state, version, id, track, start);

    /// <summary>Move a secondary clip. Association is based on its start after manual drag.</summary>
    public static CommandResult DragSecondary(TimelineState state, long version, Guid id, int track, MediaTime start)
    {
        Check(state, version);
        var current = state.Find(id);
        if (current.Track == MainTrack || track is <= MainTrack or >= MaxTracks || start.Ticks < 0)
            throw new TimelineException("INVALID_DRAG", "Secondary clip must remain on secondary tracks.");
        // A warning is cleared only by a real drag, not by a click or a drag
        // released at exactly the same track/time.
        if (current.Track == track && current.Start == start)
            return Commit(state, state.Clips.ToList());
        var parent = state.Clips.Where(c => c.Track == MainTrack && c.Start.Ticks <= start.Ticks && start.Ticks < c.End.Ticks)
            .SingleOrDefault();
        var moved = current with { Track = track, Start = start, OrphanNotice = false,
            Association = parent is null ? AssociationKind.Independent : AssociationKind.Positioned,
            ParentId = parent?.Id,
            ParentOffset = parent is null ? MediaTime.Zero : start - parent.Start };
        // A manually dragged secondary clip takes the first available lane when
        // its requested lane overlaps another clip. Preserve its horizontal time
        // and parent association; never displace the clip already occupying it.
        bool Fits(int lane) => state.Clips.Where(other => other.Id != id && other.Track == lane)
            .All(other => moved.End.Ticks <= other.Start.Ticks || moved.Start.Ticks >= other.End.Ticks);
        if (!Fits(moved.Track))
        {
            var firstFree = Enumerable.Range(1, MaxTracks - 1)
                .Where(lane => lane != moved.Track && Fits(lane))
                .DefaultIfEmpty(-1).First();
            if (firstFree < 0)
                throw new TimelineException("NO_FREE_TRACK", "No free secondary track.");
            moved = moved with { Track = firstFree };
        }
        return Commit(state, state.Clips.Select(c => c.Id == id ? moved : c).ToList());
    }

    /// <summary>Read-only previews for the resize gesture. The caller must not
    /// put these results into TimelineSession until the pointer is released.</summary>
    public static CommandResult PreviewResizeMain(TimelineState state, long version, Guid id, MediaTime duration)
        => ResizeMain(state, version, id, duration);

    public static CommandResult PreviewResizeSecondary(TimelineState state, long version, Guid id, MediaTime duration)
        => ResizeSecondary(state, version, id, duration);

    /// <summary>Pure preview: left-edge trim without mutating session or history.</summary>
    public static CommandResult PreviewTrimMainLeft(TimelineState state, long version, Guid id, MediaTime start)
        => TrimMainLeft(state, version, id, start);

    public static CommandResult PreviewTrimSecondaryLeft(TimelineState state, long version, Guid id, MediaTime start)
        => TrimSecondaryLeft(state, version, id, start);

    /// <summary>
    /// Ripple-trim the LEFT/source edge of a main clip without touching the previous clip.
    /// The requested time is the virtual source-edge position measured in the OLD
    /// timeline. Its delta against the old start changes ONLY this clip's duration:
    ///   newDuration = oldEnd - requestedLeftEdge.
    /// Main-track clips are contiguous, so this clip remains anchored immediately
    /// after its predecessor; following main clips shift by the duration delta.
    /// Positioned secondaries keep their offset to their own parent, and move when
    /// that parent moves. Shortening can orphan a child outside its parent duration.
    /// This is an ordinary one-step undoable main resize, not a rolling edit of
    /// the boundary between two clips.
    /// </summary>
    public static CommandResult TrimMainLeft(TimelineState state, long version, Guid id, MediaTime requestedLeftEdge)
    {
        Check(state, version);
        var current = state.Find(id);
        if (current.Track != MainTrack)
            throw new TimelineException("NOT_MAIN", "Clip is not on main track.");
        // For the *first* main clip the requested edge is a virtual source
        // position: negative values can restore frames trimmed off its left
        // side while the clip itself stays anchored to timeline time zero.
        // The session checks the actual source start/end before committing.
        // Later main clips cannot request a negative absolute timeline edge.
        if ((current.Start.Ticks > 0 && requestedLeftEdge.Ticks < 0) ||
            requestedLeftEdge.Ticks >= current.End.Ticks)
            throw new TimelineException("INVALID_RESIZE", "Left trim must preserve a positive duration.");
        if (requestedLeftEdge == current.Start)
            return Commit(state, state.Clips.ToList());

        // ReflowMain keeps every preceding clip unchanged and repositions only
        // the selected clip's right edge, subsequent clips and their children.
        var delta = requestedLeftEdge - current.Start;
        var result = ResizeMain(state, version, id, current.End - requestedLeftEdge);
        var clips = result.State.Clips.Select(c => c.Id == id
            ? c with { SourceStart = current.Kind == MediaKind.Image ? current.SourceStart : current.SourceStart + delta }
            : c).ToArray();
        var updated = result.State with { Clips = clips };
        Validate(updated);
        return result with { State = updated };
    }

    /// <summary>
    /// Trim a secondary clip from the LEFT, preserving its absolute right edge.
    /// A manual trim of an explicitly independent clip does not silently attach it;
    /// positioned clips update their parent and offset, or become orphaned if the
    /// new start falls outside the main track. Collision fallback is atomic.
    /// </summary>
    public static CommandResult TrimSecondaryLeft(TimelineState state, long version, Guid id, MediaTime start)
    {
        Check(state, version);
        var original = state.Find(id);
        if (original.Track == MainTrack || start.Ticks < 0 || start.Ticks >= original.End.Ticks)
            throw new TimelineException("INVALID_RESIZE", "Secondary left trim requires a valid start before the end.");
        if (start == original.Start) return Commit(state, state.Clips.ToList());
        var candidate = original with
        {
            Start = start, Duration = original.End - start,
            SourceStart = original.Kind == MediaKind.Image ? original.SourceStart :
                original.SourceStart + (start - original.Start)
        };
        if (candidate.Association == AssociationKind.Positioned)
        {
            var parent = state.Clips.SingleOrDefault(c => c.Track == MainTrack &&
                c.Start.Ticks <= start.Ticks && start.Ticks < c.End.Ticks);
            candidate = parent is null
                ? Detach(candidate, notify: true)
                : candidate with { ParentId = parent.Id, ParentOffset = start - parent.Start };
        }
        bool Fits(int lane) => state.Clips.Where(c => c.Id != id && c.Track == lane)
            .All(c => candidate.End.Ticks <= c.Start.Ticks || candidate.Start.Ticks >= c.End.Ticks);
        if (!Fits(candidate.Track))
        {
            var lane = Enumerable.Range(1, MaxTracks - 1)
                .Where(t => t != candidate.Track && Fits(t)).DefaultIfEmpty(-1).First();
            if (lane < 0)
                throw new TimelineException("NO_FREE_TRACK", "No free secondary track.");
            candidate = candidate with { Track = lane };
        }
        return Commit(state, state.Clips.Select(c => c.Id == id ? candidate : c).ToList());
    }

    /// <summary>Move main clip to a new place in the ordered sequence; main track remains gapless.</summary>
    public static CommandResult ReorderMain(TimelineState state, long version, Guid id, int targetIndex)
    {
        Check(state, version);
        var mains = state.Clips.Where(c => c.Track == MainTrack).OrderBy(c => c.Start.Ticks).ToList();
        var index = mains.FindIndex(c => c.Id == id);
        if (index < 0 || targetIndex < 0 || targetIndex >= mains.Count)
            throw new TimelineException("INVALID_REORDER", "Invalid main clip or target index.");
        mains.RemoveAt(index);
        mains.Insert(targetIndex, state.Find(id));
        return ReflowMain(state, mains);
    }

    /// <summary>
    /// Resize an occurrence on the primary timeline. For media-backed clips,
    /// TimelineSession verifies that source start + duration does not exceed
    /// the measured original source (including when re-extending a cut).
    /// </summary>
    public static CommandResult ResizeMain(TimelineState state, long version, Guid id, MediaTime duration)
    {
        Check(state, version);
        if (duration.Ticks <= 0) throw new TimelineException("INVALID_DURATION", "Duration must be positive.");
        var mains = state.Clips.Where(c => c.Track == MainTrack).OrderBy(c => c.Start.Ticks).ToList();
        var index = mains.FindIndex(c => c.Id == id);
        if (index < 0) throw new TimelineException("NOT_MAIN", "Clip is not on main track.");
        mains[index] = mains[index] with { Duration = duration };
        return ReflowMain(state, mains);
    }

    /// <summary>Used for explicit recovery; source range is reset by TimelineSession.</summary>
    public static CommandResult RestoreMainDuration(TimelineState state, long version, Guid id, MediaTime duration)
        => ResizeMain(state, version, id, duration);

    private static CommandResult ReflowMain(TimelineState state, List<Clip> orderedMains)
    {
        var updates = new Dictionary<Guid, Clip>();
        var position = MediaTime.Zero;
        foreach (var main in orderedMains)
        {
            updates[main.Id] = main with { Start = position };
            position += main.Duration;
        }
        foreach (var c in state.Clips.Where(c => c.Association == AssociationKind.Positioned))
        {
            var parent = updates[c.ParentId!.Value];
            updates[c.Id] = c.ParentOffset.Ticks >= parent.Duration.Ticks
                ? Detach(c, notify: true) // Preserve absolute time on orphaning.
                : c with { Start = parent.Start + c.ParentOffset };
        }
        // Fixed clips (independent or unchanged) reserve their lanes first.
        // Moving associated clips keep their lane whenever possible; otherwise
        // choose the lowest available secondary lane, deterministically.
        var candidates = state.Clips.Select(c => updates.GetValueOrDefault(c.Id, c)).ToList();
        var movingIds = state.Clips.Where(c => c.Association == AssociationKind.Positioned &&
            updates.TryGetValue(c.Id, out var next) && next.Start != c.Start).Select(c => c.Id).ToHashSet();
        var occupied = candidates.Where(c => c.Track != MainTrack && !movingIds.Contains(c.Id)).ToList();
        var moving = candidates.Where(c => movingIds.Contains(c.Id))
            .OrderBy(c => c.Start.Ticks).ThenBy(c => c.Track).ThenBy(c => c.Id).ToList();
        foreach (var clip in moving)
        {
            bool Fits(int track) => occupied.Where(other => other.Track == track)
                .All(other => clip.End.Ticks <= other.Start.Ticks || clip.Start.Ticks >= other.End.Ticks);
            var targetTrack = Fits(clip.Track) ? clip.Track :
                Enumerable.Range(1, MaxTracks - 1).Where(Fits).DefaultIfEmpty(-1).First();
            if (targetTrack < 0)
                throw new TimelineException("NO_FREE_TRACK", "No secondary track can accommodate the associated clip.");
            occupied.Add(clip with { Track = targetTrack });
            updates[clip.Id] = clip with { Track = targetTrack };
        }
        return Commit(state, state.Clips.Select(c => updates.GetValueOrDefault(c.Id, c)).ToList());
    }

    public static CommandResult DeleteMain(TimelineState state, long version, Guid id)
    {
        Check(state, version);
        var deleted = state.Find(id);
        if (deleted.Track != MainTrack) throw new TimelineException("NOT_MAIN", "Clip is not main.");
        var rest = state.Clips.Where(c => c.Id != id).Select(c => c.ParentId == id ? Detach(c) : c).ToList();
        // Reflow remaining main clips while preserving independent clips at absolute positions.
        var interim = new TimelineState(state.Version, rest, state.MarkerItems.ToArray());
        var result = ReflowMain(interim, rest.Where(c => c.Track == MainTrack).OrderBy(c => c.Start.Ticks).ToList());
        var oldClips = state.Clips.ToDictionary(c => c.Id);
        var newClips = result.State.Clips.ToDictionary(c => c.Id);
        var changed = oldClips.Keys.Union(newClips.Keys)
            .Where(key => !oldClips.TryGetValue(key, out var before) ||
                !newClips.TryGetValue(key, out var after) || before != after)
            .ToArray();
        return new CommandResult(result.State, changed);
    }

    /// <summary>
    /// Change the secondary duration. Source bounds are checked by the session
    /// when real source metadata is attached; images remain freely resizable.
    /// </summary>
    public static CommandResult ResizeSecondary(TimelineState state, long version, Guid id, MediaTime duration)
    {
        Check(state, version);
        var c = state.Find(id);
        if (c.Track == MainTrack || duration.Ticks <= 0)
            throw new TimelineException("INVALID_RESIZE", "Secondary clip and positive duration required.");
        var candidate = c with { Duration = duration };
        bool Fits(int track) => state.Clips.Where(other => other.Id != id && other.Track == track)
            .All(other => candidate.End.Ticks <= other.Start.Ticks || candidate.Start.Ticks >= other.End.Ticks);
        if (!Fits(candidate.Track))
        {
            int firstFree = Enumerable.Range(1, MaxTracks - 1).Where(t => t != candidate.Track).FirstOrDefault(Fits);
            if (firstFree == 0) throw new TimelineException("NO_FREE_TRACK", "No free secondary track.");
            candidate = candidate with { Track = firstFree };
        }
        return Commit(state, state.Clips.Select(x => x.Id == id ? candidate : x).ToList());
    }

    /// <summary>Explicit recovery uses the same collision-safe timeline operation.</summary>
    public static CommandResult RestoreSecondaryDuration(TimelineState state, long version, Guid id, MediaTime duration)
        => ResizeSecondary(state, version, id, duration);

    /// <summary>Split a clip at an absolute time, retaining the original ID for the left half.
    /// The right half receives a new ID and inherits the same media and compatible metadata.
    /// For main clips, positioned secondaries whose starts fall in the right half are
    /// reassociated with the new main clip; absolute positions remain unchanged.
    /// A positioned secondary split retains its original parent and adjusted offset.
    /// </summary>
    public static CommandResult Split(TimelineState state, long version, Guid id, MediaTime at, Guid rightId)
    {
        Check(state, version);
        var original = state.Find(id);
        if (rightId == Guid.Empty || state.Clips.Any(c => c.Id == rightId))
            throw new TimelineException("INVALID_ID", "New clip identifier must be unique and nonempty.");
        if (at.Ticks <= original.Start.Ticks || at.Ticks >= original.End.Ticks)
            throw new TimelineException("INVALID_SPLIT", "Split must be strictly inside the clip.");

        var leftDuration = at - original.Start;
        var right = original with
        {
            Id = rightId, Start = at, Duration = original.End - at,
            SourceStart = original.Kind == MediaKind.Image ? original.SourceStart :
                original.SourceStart + leftDuration
        };
        if (original.Association == AssociationKind.Positioned)
            right = right with { ParentOffset = original.ParentOffset + leftDuration };
        var left = original with { Duration = leftDuration };
        var clips = new List<Clip>(state.Clips.Count + 1);
        foreach (var clip in state.Clips)
        {
            if (clip.Id == id)
            {
                clips.Add(left);
                clips.Add(right);
            }
            else if (original.Track == MainTrack && clip.ParentId == id && clip.Start.Ticks >= at.Ticks)
            {
                clips.Add(clip with { ParentId = rightId, ParentOffset = clip.Start - at });
            }
            else clips.Add(clip);
        }
        return Commit(state, clips);
    }

    /// <summary>Delete a secondary clip without changing other clips.</summary>
    public static CommandResult DeleteSecondary(TimelineState state, long version, Guid id)
    {
        Check(state, version);
        if (state.Find(id).Track == MainTrack)
            throw new TimelineException("NOT_SECONDARY", "Use DeleteMain for main clips.");
        return Commit(state, state.Clips.Where(c => c.Id != id).ToList());
    }
}
