using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

/// <summary>Regressions for bidirectional (left/right) resizing on the timeline.</summary>
public sealed class LeftTrimTests
{
    private static MediaTime T(int seconds) => MediaTime.FromSeconds(seconds);
    private static Clip C(int lane, int start, int duration, MediaKind kind = MediaKind.Video) =>
        new(Guid.NewGuid(), Guid.NewGuid(), kind, lane, T(start), T(duration));
    private static TimelineState S(params Clip[] clips) => new(0, clips);

    [Fact] public void SecondaryLeftTrimMovesStartAndPreservesAbsoluteEndAndMediaMetadata()
    {
        var audio = C(2, 3, 8, MediaKind.Audio) with { EffectsToken = "gain" };
        var result = TimelineEngine.TrimSecondaryLeft(S(audio), 0, audio.Id, T(5)).State.Find(audio.Id);
        Assert.Equal(T(5), result.Start);
        Assert.Equal(T(6), result.Duration);
        Assert.Equal(T(11), result.End);
        Assert.Equal("gain", result.EffectsToken);
        Assert.Equal(MediaKind.Audio, result.Kind);
        Assert.Equal(2, result.Track);
    }

    [Theory]
    [InlineData(MediaKind.Video)]
    [InlineData(MediaKind.Image)]
    [InlineData(MediaKind.Audio)]
    public void SecondaryCanExtendFromLeftForEveryMediaKind(MediaKind kind)
    {
        var clip = C(3, 6, 4, kind);
        var after = TimelineEngine.TrimSecondaryLeft(S(clip), 0, clip.Id, T(2)).State.Find(clip.Id);
        Assert.Equal(T(2), after.Start);
        Assert.Equal(T(8), after.Duration);
        Assert.Equal(T(10), after.End);
        Assert.Equal(kind, after.Kind);
    }

    [Fact] public void LeftTrimOfPositionedSecondaryUpdatesOffsetWithoutMovingParent()
    {
        var main = C(0, 0, 20);
        var image = C(4, 4, 6, MediaKind.Image) with
        {
            Association = AssociationKind.Positioned, ParentId = main.Id, ParentOffset = T(4)
        };
        var result = TimelineEngine.TrimSecondaryLeft(S(main, image), 0, image.Id, T(7)).State;
        Assert.Equal(main.Id, result.Find(image.Id).ParentId);
        Assert.Equal(T(7), result.Find(image.Id).ParentOffset);
        Assert.Equal(T(10), result.Find(image.Id).End);
        Assert.Equal(T(0), result.Find(main.Id).Start);
        TimelineEngine.Validate(result);
    }

    [Fact] public void LeftTrimCanReassociatePositionedSecondaryAcrossMainBoundary()
    {
        var first = C(0, 0, 10);
        var second = C(0, 10, 10);
        var overlay = C(3, 8, 6) with
        {
            Association = AssociationKind.Positioned, ParentId = first.Id, ParentOffset = T(8)
        };
        var result = TimelineEngine.TrimSecondaryLeft(S(first, second, overlay), 0, overlay.Id, T(12)).State.Find(overlay.Id);
        Assert.Equal(second.Id, result.ParentId);
        Assert.Equal(T(2), result.ParentOffset);
        Assert.Equal(T(14), result.End);
    }

    [Fact] public void IndependentSecondaryRemainsIndependentEvenWhenNewStartIsInsideMain()
    {
        var main = C(0, 0, 20);
        var audio = C(4, 7, 4, MediaKind.Audio);
        var result = TimelineEngine.TrimSecondaryLeft(S(main, audio), 0, audio.Id, T(6)).State.Find(audio.Id);
        Assert.Equal(AssociationKind.Independent, result.Association);
        Assert.Null(result.ParentId);
        Assert.Equal(T(11), result.End);
    }

    [Fact] public void LeftTrimMovesToAvailableTrackWhenExtensionCollides()
    {
        var extending = C(3, 10, 4);
        var obstacle = C(3, 4, 5);
        var result = TimelineEngine.TrimSecondaryLeft(S(extending, obstacle), 0, extending.Id, T(7)).State;
        Assert.Equal(1, result.Find(extending.Id).Track);
        Assert.Equal(T(7), result.Find(extending.Id).Start);
        Assert.Equal(T(14), result.Find(extending.Id).End);
        Assert.Equal(3, result.Find(obstacle.Id).Track);
    }

    [Fact] public void NoFreeTrackBlocksSecondaryLeftTrimAtomically()
    {
        var extending = C(1, 10, 4);
        var blockers = Enumerable.Range(1, 9).Select(lane => C(lane, 1, 8)).ToArray();
        var session = new TimelineSession(S([extending, ..blockers]));
        var error = Assert.Throws<TimelineException>(() => session.Execute(s =>
            TimelineEngine.TrimSecondaryLeft(s, s.Version, extending.Id, T(6))));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.Equal(T(10), session.Current.Find(extending.Id).Start);
        Assert.Equal(T(4), session.Current.Find(extending.Id).Duration);
        Assert.Equal(0, session.Current.Version);
        Assert.False(session.CanUndo);
    }

    [Fact] public void MainLeftTrimRightwardKeepsPreviousAndRipplesOnlySelectedAndFollowing()
    {
        var previous = C(0, 0, 8);
        var selected = C(0, 8, 9);
        var following = C(0, 17, 7);
        var state = TimelineEngine.TrimMainLeft(S(previous, selected, following), 0, selected.Id, T(10)).State;

        Assert.Equal(previous, state.Find(previous.Id)); // no rolling edit!
        Assert.Equal(T(8), state.Find(selected.Id).Start);
        Assert.Equal(T(7), state.Find(selected.Id).Duration);
        Assert.Equal(T(15), state.Find(following.Id).Start);
        Assert.Equal(T(22), state.Duration);
        TimelineEngine.Validate(state);
    }

    [Fact] public void MainLeftTrimLeftwardDoesNotShrinkPreviousAndPushesFollowingRight()
    {
        var previous = C(0, 0, 8);
        var selected = C(0, 8, 9);
        var following = C(0, 17, 7);
        var state = TimelineEngine.TrimMainLeft(S(previous, selected, following), 0, selected.Id, T(6)).State;

        Assert.Equal(previous, state.Find(previous.Id));
        Assert.Equal(T(8), state.Find(selected.Id).Start);
        Assert.Equal(T(11), state.Find(selected.Id).Duration);
        Assert.Equal(T(19), state.Find(following.Id).Start);
        Assert.Equal(T(26), state.Duration);
        TimelineEngine.Validate(state);
    }

    [Fact] public void MainLeftTrimDoesNotAffectPreviousParentOrItsPositionedMedia()
    {
        var previous = C(0, 0, 8);
        var selected = C(0, 8, 9);
        var following = C(0, 17, 7);
        var previousChild = C(1, 7, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = previous.Id, ParentOffset = T(7)
        };
        var selectedChild = C(2, 9, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = selected.Id, ParentOffset = T(1)
        };
        var followingChild = C(3, 18, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = following.Id, ParentOffset = T(1)
        };
        var independent = C(4, 4, 14, MediaKind.Audio);
        var marker = new TimelineMarker(Guid.NewGuid(), T(20), "Ponto fixo");
        var old = new TimelineState(0,
            new[] { previous, selected, following, previousChild, selectedChild, followingChild, independent },
            new[] { marker });

        var state = TimelineEngine.TrimMainLeft(old, 0, selected.Id, T(10)).State;
        Assert.Equal(previous, state.Find(previous.Id));
        Assert.Equal(previousChild, state.Find(previousChild.Id));
        Assert.Equal(selectedChild, state.Find(selectedChild.Id));
        Assert.Equal(independent, state.Find(independent.Id));
        Assert.Equal(T(16), state.Find(followingChild.Id).Start);
        Assert.Equal(T(1), state.Find(followingChild.Id).ParentOffset);
        Assert.Equal(marker, Assert.Single(state.MarkerItems));
        TimelineEngine.Validate(state);
    }

    [Fact] public void ShorteningSelectedMainCanOrphanItsOwnChildWithoutTouchingPrevious()
    {
        var previous = C(0, 0, 8);
        var selected = C(0, 8, 9);
        var following = C(0, 17, 4);
        var child = C(2, 16, 3) with
        {
            Association = AssociationKind.Positioned, ParentId = selected.Id, ParentOffset = T(8)
        };
        var after = TimelineEngine.TrimMainLeft(S(previous, selected, following, child), 0,
            selected.Id, T(13)).State;

        Assert.Equal(previous, after.Find(previous.Id));
        Assert.Equal(T(4), after.Find(selected.Id).Duration);
        Assert.Equal(T(12), after.Find(following.Id).Start);
        Assert.Equal(AssociationKind.Independent, after.Find(child.Id).Association);
        Assert.True(after.Find(child.Id).OrphanNotice);
        Assert.Equal(T(16), after.Find(child.Id).Start);
        TimelineEngine.Validate(after);
    }

    [Fact] public void MainLeftTrimMovesFollowingAttachedMediaToFreeTrackOnCollision()
    {
        var previous = C(0, 0, 8);
        var selected = C(0, 8, 5);
        var following = C(0, 13, 5);
        var child = C(1, 14, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = following.Id, ParentOffset = T(1)
        };
        var blocker = C(1, 18, 3, MediaKind.Audio);
        var after = TimelineEngine.TrimMainLeft(S(previous, selected, following, child, blocker), 0,
            selected.Id, T(5)).State;

        Assert.Equal(previous, after.Find(previous.Id));
        Assert.Equal(T(8), after.Find(selected.Id).Duration);
        Assert.Equal(T(16), after.Find(following.Id).Start);
        Assert.Equal(T(17), after.Find(child.Id).Start);
        Assert.Equal(2, after.Find(child.Id).Track);
        Assert.Equal(blocker, after.Find(blocker.Id));
        TimelineEngine.Validate(after);
    }

    [Fact] public void MainLeftTrimBlocksAtomicallyWhenNoSecondaryTrackIsAvailable()
    {
        var previous = C(0, 0, 8);
        var selected = C(0, 8, 5);
        var following = C(0, 13, 5);
        var attached = C(1, 14, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = following.Id, ParentOffset = T(1)
        };
        var blockers = Enumerable.Range(1, 9).Select(lane => C(lane, 18, 3, MediaKind.Audio)).ToArray();
        var session = new TimelineSession(S([previous, selected, following, attached, ..blockers]));
        var error = Assert.Throws<TimelineException>(() => session.Execute(s =>
            TimelineEngine.TrimMainLeft(s, s.Version, selected.Id, T(5))));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.Equal(T(8), session.Current.Find(previous.Id).Duration);
        Assert.Equal(T(5), session.Current.Find(selected.Id).Duration);
        Assert.Equal(T(14), session.Current.Find(attached.Id).Start);
        Assert.Equal(0, session.Current.Version);
        Assert.False(session.CanUndo);
    }

    [Fact] public void FirstMainLeftTrimStillRipplesFollowingWithoutGap()
    {
        var first = C(0, 0, 8);
        var following = C(0, 8, 5);
        var linked = C(4, 9, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = following.Id, ParentOffset = T(1)
        };
        var after = TimelineEngine.TrimMainLeft(S(first, following, linked), 0, first.Id, T(2)).State;
        Assert.Equal(T(0), after.Find(first.Id).Start);
        Assert.Equal(T(6), after.Find(first.Id).Duration);
        Assert.Equal(T(6), after.Find(following.Id).Start);
        Assert.Equal(T(7), after.Find(linked.Id).Start);
        TimelineEngine.Validate(after);
    }

    [Fact] public void MainLeftTrimPreviewDoesNotChangeStateOrHistoryAndUndoRedoRestoreRipple()
    {
        var previous = C(0, 0, 8);
        var selected = C(0, 8, 9);
        var following = C(0, 17, 7);
        var child = C(2, 18, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = following.Id, ParentOffset = T(1)
        };
        var session = new TimelineSession(S(previous, selected, following, child));
        var preview = TimelineEngine.PreviewTrimMainLeft(session.Current, 0, selected.Id, T(10));
        Assert.Equal(previous, preview.State.Find(previous.Id));
        Assert.Equal(T(15), preview.State.Find(following.Id).Start);
        Assert.Equal(T(17), session.Current.Find(following.Id).Start);
        Assert.False(session.CanUndo);

        session.Execute(s => TimelineEngine.TrimMainLeft(s, s.Version, selected.Id, T(10)));
        Assert.Equal(T(15), session.Current.Find(following.Id).Start);
        Assert.Equal(T(16), session.Current.Find(child.Id).Start);
        Assert.True(session.CanUndo);
        session.Undo();
        Assert.Equal(previous, session.Current.Find(previous.Id));
        Assert.Equal(T(9), session.Current.Find(selected.Id).Duration);
        Assert.Equal(T(17), session.Current.Find(following.Id).Start);
        Assert.Equal(T(18), session.Current.Find(child.Id).Start);
        session.Redo();
        Assert.Equal(previous, session.Current.Find(previous.Id));
        Assert.Equal(T(7), session.Current.Find(selected.Id).Duration);
        Assert.Equal(T(15), session.Current.Find(following.Id).Start);
    }

    [Fact] public void MainLeftTrimUndoRedoRestoresOrphanNotice()
    {
        var previous = C(0, 0, 8);
        var selected = C(0, 8, 9);
        var child = C(1, 16, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = selected.Id, ParentOffset = T(8)
        };
        var session = new TimelineSession(S(previous, selected, child));
        session.Execute(s => TimelineEngine.TrimMainLeft(s, s.Version, selected.Id, T(13)));
        Assert.True(session.Current.Find(child.Id).OrphanNotice);
        Assert.Equal(previous, session.Current.Find(previous.Id));
        session.Undo();
        Assert.False(session.Current.Find(child.Id).OrphanNotice);
        Assert.Equal(AssociationKind.Positioned, session.Current.Find(child.Id).Association);
        Assert.Equal(T(9), session.Current.Find(selected.Id).Duration);
        session.Redo();
        Assert.True(session.Current.Find(child.Id).OrphanNotice);
        Assert.Equal(previous, session.Current.Find(previous.Id));
    }

    [Fact] public void LeftTrimRejectsNonpositiveDurationsAndNegativeVirtualBoundary()
    {
        var first = C(0, 0, 8);
        var second = C(0, 8, 9);
        var secondary = C(3, 4, 3);
        var state = S(first, second, secondary);
        Assert.Equal("INVALID_RESIZE", Assert.Throws<TimelineException>(() =>
            TimelineEngine.TrimSecondaryLeft(state, 0, secondary.Id, T(7))).Code);
        Assert.Equal("INVALID_RESIZE", Assert.Throws<TimelineException>(() =>
            TimelineEngine.TrimSecondaryLeft(state, 0, secondary.Id, T(8))).Code);
        Assert.Equal("INVALID_RESIZE", Assert.Throws<TimelineException>(() =>
            TimelineEngine.TrimMainLeft(state, 0, second.Id, T(-1))).Code);
        Assert.Equal("INVALID_RESIZE", Assert.Throws<TimelineException>(() =>
            TimelineEngine.TrimMainLeft(state, 0, second.Id, T(17))).Code);
        // t=0 is valid for a later clip and extends ONLY that clip.
        var after = TimelineEngine.TrimMainLeft(state, 0, second.Id, T(0)).State;
        Assert.Equal(first, after.Find(first.Id));
        Assert.Equal(T(17), after.Find(second.Id).Duration);
    }
}
