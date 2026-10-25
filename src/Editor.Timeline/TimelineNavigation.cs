using System;
using System.Linq;

namespace Editor.Timeline;

/// <summary>
/// Navigates clips by their visual positions on the timeline, without modifying it.
/// Horizontal arrows prioritize the adjacent clip on the selected track. At the
/// edge of a track, Right continues at the first clip of the next occupied track
/// below; Left continues at the last clip of the previous occupied track above.
/// Vertical arrows jump to the nearest occupied track in that direction and
/// prioritize horizontal overlap. Navigation never wraps beyond the outer tracks.
/// </summary>
public enum NavigationDirection { Left, Right, Up, Down }

public static class TimelineNavigation
{
    public static Clip? FindNeighbor(TimelineState state, Guid? selectedId, NavigationDirection direction)
    {
        if (selectedId is not Guid id) return null;
        var current = state.Clips.FirstOrDefault(c => c.Id == id);
        if (current is null) return null;

        if (direction == NavigationDirection.Right)
        {
            // Continue horizontally along the current row before moving to the next one.
            var sameTrack = state.Clips.Where(c => c.Track == current.Track && c.Id != id
                    && c.Start.Ticks >= current.End.Ticks)
                .OrderBy(c => c.Start.Ticks).ThenBy(c => c.Id)
                .FirstOrDefault();
            return sameTrack ?? FindNextTrackBoundary(state, current.Track, forward: true);
        }
        if (direction == NavigationDirection.Left)
        {
            var sameTrack = state.Clips.Where(c => c.Track == current.Track && c.Id != id
                    && c.End.Ticks <= current.Start.Ticks)
                .OrderByDescending(c => c.End.Ticks).ThenByDescending(c => c.Start.Ticks)
                .ThenBy(c => c.Id).FirstOrDefault();
            return sameTrack ?? FindNextTrackBoundary(state, current.Track, forward: false);
        }

        if (direction != NavigationDirection.Up && direction != NavigationDirection.Down)
            return null;

        var step = direction == NavigationDirection.Up ? -1 : 1;
        for (var track = current.Track + step;
             track >= 0 && track < TimelineEngine.MaxTracks;
             track += step)
        {
            var sameRow = state.Clips.Where(c => c.Track == track).ToList();
            if (sameRow.Count == 0) continue;
            return sameRow.OrderBy(c => HorizontalGapTicks(current, c))
                .ThenBy(c => Math.Abs(CenterTicks(current) - CenterTicks(c)))
                .ThenBy(c => c.Start.Ticks).ThenBy(c => c.Id)
                .First();
        }
        return null;
    }

    private static Clip? FindNextTrackBoundary(TimelineState state, int currentTrack, bool forward)
    {
        var step = forward ? 1 : -1;
        for (var track = currentTrack + step;
             track >= 0 && track < TimelineEngine.MaxTracks;
             track += step)
        {
            var candidates = state.Clips.Where(c => c.Track == track);
            // Natural reading order across rows: from the last clip, go to the
            // first (leftmost) clip below; from the first, go to the last above.
            var target = forward
                ? candidates.OrderBy(c => c.Start.Ticks).ThenBy(c => c.Id).FirstOrDefault()
                : candidates.OrderByDescending(c => c.Start.Ticks).ThenBy(c => c.Id).FirstOrDefault();
            if (target is not null) return target;
        }
        return null;
    }

    // Intervals use [start,end): touching boundaries count as zero visual gap.
    private static decimal HorizontalGapTicks(Clip a, Clip b)
    {
        if (a.End.Ticks <= b.Start.Ticks) return (decimal)b.Start.Ticks - a.End.Ticks;
        if (b.End.Ticks <= a.Start.Ticks) return (decimal)a.Start.Ticks - b.End.Ticks;
        return 0m;
    }

    private static decimal CenterTicks(Clip c) => ((decimal)c.Start.Ticks + c.End.Ticks) / 2m;
}
