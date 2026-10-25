using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class MarkerTests
{
    private static MediaTime T(decimal s) => MediaTime.FromSeconds(s);
    private static TimelineMarker M(decimal time, string label = "Marcador 1") =>
        new(Guid.NewGuid(), T(time), label);
    private static Clip C(int track, decimal at, decimal duration) =>
        new(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, track, T(at), T(duration));
    private static TimelineState S(params Clip[] clips) => new(0, clips);

    [Fact] public void AddMarkerCommitsExactlyOnceAndPreservesClips()
    {
        var clip = C(0, 0, 8); var marker = M(2);
        var result = TimelineEngine.AddMarker(S(clip), 0, marker);
        Assert.Equal(1, result.State.Version);
        Assert.Single(result.State.MarkerItems);
        Assert.Equal(marker, result.State.MarkerItems[0]);
        Assert.Equal(clip, Assert.Single(result.State.Clips));
        Assert.Empty(result.ChangedClipIds);
    }

    [Fact] public void TwoMarkersAtSameTimeAreRejectedAtomically()
    {
        var initial = TimelineEngine.AddMarker(S(), 0, M(2)).State;
        var ex = Assert.Throws<TimelineException>(() =>
            TimelineEngine.AddMarker(initial, initial.Version, M(2, "Marcador 2")));
        Assert.Equal("DUPLICATE_MARKER", ex.Code);
        Assert.Single(initial.MarkerItems);
    }

    [Fact] public void DuplicateMarkerIdAndClipIdAreRejected()
    {
        var clip = C(0, 0, 9);
        Assert.Equal("INVALID_MARKER", Assert.Throws<TimelineException>(() =>
            TimelineEngine.AddMarker(S(clip), 0, new TimelineMarker(clip.Id, T(2), "x"))).Code);
        var marker = M(1);
        var state = TimelineEngine.AddMarker(S(), 0, marker).State;
        Assert.Equal("INVALID_MARKER", Assert.Throws<TimelineException>(() =>
            TimelineEngine.AddMarker(state, state.Version, marker with { At = T(3) })).Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.1)]
    public void MarkerCannotBeAtNegativeTime(double time)
    {
        var error = Assert.Throws<TimelineException>(() => TimelineEngine.AddMarker(S(), 0, M((decimal)time)));
        Assert.Equal("INVALID_MARKER", error.Code);
    }

    [Fact] public void MarkerLabelIsRequiredAndBounded()
    {
        Assert.Equal("INVALID_MARKER", Assert.Throws<TimelineException>(() =>
            TimelineEngine.AddMarker(S(), 0, M(1, " "))).Code);
        Assert.Equal("INVALID_MARKER", Assert.Throws<TimelineException>(() =>
            TimelineEngine.AddMarker(S(), 0, M(1, new string('x', 61)))).Code);
    }

    [Fact] public void MoveMarkerPreservesClipsAndIsUndoable()
    {
        var clip = C(0, 0, 8); var marker = M(1);
        var initial = S(clip);
        var session = new TimelineSession(initial);
        session.Execute(s => TimelineEngine.AddMarker(s, s.Version, marker));
        session.Execute(s => TimelineEngine.MoveMarker(s, s.Version, marker.Id, T(4)));
        Assert.Equal(T(4), Assert.Single(session.Current.MarkerItems).At);
        session.Undo();
        Assert.Equal(T(1), Assert.Single(session.Current.MarkerItems).At);
        session.Redo();
        Assert.Equal(T(4), Assert.Single(session.Current.MarkerItems).At);
        Assert.Equal(clip, Assert.Single(session.Current.Clips));
    }

    [Fact] public void MoveMarkerCannotOverlapAnother()
    {
        var a = M(1, "A"); var b = M(2, "B");
        var state = TimelineEngine.AddMarker(S(), 0, a).State;
        state = TimelineEngine.AddMarker(state, state.Version, b).State;
        var original = state;
        Assert.Equal("DUPLICATE_MARKER", Assert.Throws<TimelineException>(() =>
            TimelineEngine.MoveMarker(state, state.Version, b.Id, T(1))).Code);
        Assert.Equal(original, state);
    }

    [Fact] public void RemovingMarkerIsUndoableAndLeavesOthersIntact()
    {
        var marker1 = M(1, "A"); var marker2 = M(2, "B");
        var session = new TimelineSession(S());
        session.Execute(s => TimelineEngine.AddMarker(s, s.Version, marker1));
        session.Execute(s => TimelineEngine.AddMarker(s, s.Version, marker2));
        session.Execute(s => TimelineEngine.RemoveMarker(s, s.Version, marker1.Id));
        Assert.Equal(marker2.Id, Assert.Single(session.Current.MarkerItems).Id);
        session.Undo();
        Assert.Equal(2, session.Current.MarkerItems.Count);
        session.Redo();
        Assert.Equal(marker2.Id, Assert.Single(session.Current.MarkerItems).Id);
    }

    [Fact] public void RemovingUnknownMarkerFails()
    {
        var error = Assert.Throws<TimelineException>(() =>
            TimelineEngine.RemoveMarker(S(), 0, Guid.NewGuid()));
        Assert.Equal("MARKER_NOT_FOUND", error.Code);
    }

    [Fact] public void MovingUnknownMarkerFails()
    {
        var error = Assert.Throws<TimelineException>(() =>
            TimelineEngine.MoveMarker(S(), 0, Guid.NewGuid(), T(2)));
        Assert.Equal("MARKER_NOT_FOUND", error.Code);
    }

    [Fact] public void MarkerOperationsRejectStaleVersions()
    {
        var state = TimelineEngine.AddMarker(S(), 0, M(1)).State;
        Assert.Equal("VERSION_CONFLICT", Assert.Throws<TimelineException>(() =>
            TimelineEngine.AddMarker(state, 0, M(2))).Code);
    }

    [Fact] public void ClipSplitPreservesGuidesAtTheirAbsoluteTimes()
    {
        var clip = C(0, 0, 10);
        var state = TimelineEngine.AddMarker(S(clip), 0, M(5)).State;
        var result = TimelineEngine.Split(state, state.Version, clip.Id, T(4), Guid.NewGuid()).State;
        Assert.Equal(T(5), Assert.Single(result.MarkerItems).At);
        Assert.Equal(2, result.Clips.Count);
    }

    [Fact] public void DeletingMainClipDoesNotDropGuides()
    {
        var clip = C(0, 0, 8); var state = TimelineEngine.AddMarker(S(clip), 0, M(6)).State;
        var result = TimelineEngine.DeleteMain(state, state.Version, clip.Id).State;
        Assert.Empty(result.Clips);
        Assert.Equal(T(6), Assert.Single(result.MarkerItems).At);
    }

    [Fact] public void UndoRedoKeepsMarkerAndClipEditHistoryInOrder()
    {
        var clip = C(0, 0, 8); var marker = M(2);
        var session = new TimelineSession(S(clip));
        session.Execute(s => TimelineEngine.AddMarker(s, s.Version, marker));
        session.Execute(s => TimelineEngine.ResizeMain(s, s.Version, clip.Id, T(9)));
        session.Execute(s => TimelineEngine.MoveMarker(s, s.Version, marker.Id, T(4)));
        session.Undo();
        Assert.Equal(T(2), Assert.Single(session.Current.MarkerItems).At);
        session.Undo();
        Assert.Equal(T(8), session.Current.Find(clip.Id).Duration);
        session.Undo();
        Assert.Empty(session.Current.MarkerItems);
        session.Redo(); session.Redo(); session.Redo();
        Assert.Equal(T(4), Assert.Single(session.Current.MarkerItems).At);
        Assert.Equal(T(9), session.Current.Find(clip.Id).Duration);
    }
}
