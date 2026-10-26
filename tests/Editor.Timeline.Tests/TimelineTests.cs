using Editor.Timeline;
using Xunit;

namespace Editor.Timeline.Tests;

public sealed class TimelineTests
{
    private static MediaTime T(int seconds) => MediaTime.FromSeconds(seconds);
    private static Clip C(int track, int start, int duration, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), MediaKind.Video, track, T(start), T(duration));
    private static TimelineState S(params Clip[] clips) => new(0, clips);

    [Fact] public void InsertAutomaticallyAssociatesSecondaryAtMainStart()
    {
        var main = C(0, 0, 10);
        var secondary = C(5, 3, 2);
        var result = TimelineEngine.Insert(S(main), 0, secondary).State.Find(secondary.Id);
        Assert.Equal(AssociationKind.Positioned, result.Association);
        Assert.Equal(main.Id, result.ParentId);
        Assert.Equal(T(3), result.ParentOffset);
    }

    [Fact] public void ReorderMovesAssociationsAcrossMultipleTracks()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var first = C(1, 2, 2) with { Association = AssociationKind.Positioned, ParentId = a.Id, ParentOffset = T(2) };
        var second = C(5, 4, 2) with { Association = AssociationKind.Positioned, ParentId = a.Id, ParentOffset = T(4) };
        var third = C(9, 12, 2) with { Association = AssociationKind.Positioned, ParentId = b.Id, ParentOffset = T(2) };
        var result = TimelineEngine.ReorderMain(S(a,b,first,second,third), 0, b.Id, 0).State;
        Assert.Equal(T(12), result.Find(first.Id).Start);
        Assert.Equal(T(14), result.Find(second.Id).Start);
        Assert.Equal(T(2), result.Find(third.Id).Start);
    }

    [Fact] public void IndependentSecondaryStaysFixedAndCanBeAssociatedAgain()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var sec = C(4, 2, 2) with { Association = AssociationKind.Positioned, ParentId = a.Id, ParentOffset = T(2) };
        var detached = TimelineEngine.SetIndependent(S(a,b,sec), 0, sec.Id).State;
        var moved = TimelineEngine.ReorderMain(detached, detached.Version, b.Id, 0).State;
        Assert.Equal(T(2), moved.Find(sec.Id).Start);
        Assert.Equal(AssociationKind.Independent, moved.Find(sec.Id).Association);
        var reassociated = TimelineEngine.AssociateAtStart(moved, moved.Version, sec.Id).State.Find(sec.Id);
        Assert.Equal(b.Id, reassociated.ParentId);
    }

    [Fact] public void AdjacentClipsDoNotOverlap()
    {
        TimelineEngine.Validate(S(C(0, 0, 10), C(0, 10, 5)));
    }

    [Fact] public void DragSecondaryCollisionMovesToNextFreeTrack()
    {
        var first = C(1, 0, 10); var second = C(1, 10, 5); var original = S(first, second);
        var moved = TimelineEngine.DragSecondary(original, 0, second.Id, 1, T(9)).State.Find(second.Id);
        Assert.Equal(2, moved.Track);
        Assert.Equal(T(9), moved.Start);
        Assert.Equal(T(10), original.Find(second.Id).Start);
    }

    [Fact] public void DragCollisionKeepsAssociationAndDoesNotMoveObstacle()
    {
        var main = C(0, 0, 20);
        var obstacle = C(2, 3, 10);
        var moving = C(2, 15, 3);
        var result = TimelineEngine.DragSecondary(S(main, obstacle, moving), 0, moving.Id, 2, T(5)).State;
        Assert.Equal(1, result.Find(moving.Id).Track);
        Assert.Equal(T(5), result.Find(moving.Id).Start);
        Assert.Equal(AssociationKind.Positioned, result.Find(moving.Id).Association);
        Assert.Equal(main.Id, result.Find(moving.Id).ParentId);
        Assert.Equal(T(3), result.Find(obstacle.Id).Start);
    }

    [Fact] public void DragCollisionFailsAtomicallyWhenAllSecondaryTracksBlocked()
    {
        var moving = C(1, 15, 2);
        var blockers = Enumerable.Range(1, 9).Select(t => C(t, 0, 10)).ToArray();
        var original = S([moving, ..blockers]);
        var ex = Assert.Throws<TimelineException>(() => TimelineEngine.DragSecondary(original, 0, moving.Id, 1, T(5)));
        Assert.Equal("NO_FREE_TRACK", ex.Code);
        Assert.Equal(T(15), original.Find(moving.Id).Start);
    }

    [Fact] public void ManualDragWithinMainCreatesPositionedAssociation()
    {
        var main = C(0, 0, 10); var secondary = C(1, 15, 2);
        var result = TimelineEngine.DragSecondary(S(main, secondary), 0, secondary.Id, 1, T(4));
        Assert.Equal(AssociationKind.Positioned, result.State.Find(secondary.Id).Association);
        Assert.Equal(main.Id, result.State.Find(secondary.Id).ParentId);
        Assert.Equal(T(4), result.State.Find(secondary.Id).ParentOffset);
    }

    [Fact] public void MainReorderMovesPositionedButNotIndependent()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var positioned = C(1, 12, 2) with { Association = AssociationKind.Positioned, ParentId = b.Id, ParentOffset = T(2) };
        var independent = C(2, 14, 2);
        var result = TimelineEngine.ReorderMain(S(a,b,positioned,independent), 0, b.Id, 0).State;
        Assert.Equal(T(0), result.Find(b.Id).Start);
        Assert.Equal(T(2), result.Find(positioned.Id).Start);
        Assert.Equal(T(14), result.Find(independent.Id).Start);
    }

    [Fact] public void ShorteningMainOrphansAndPreservesAbsoluteTime()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var secondary = C(1, 8, 2) with { Association = AssociationKind.Positioned, ParentId = a.Id, ParentOffset = T(8) };
        var result = TimelineEngine.ResizeMain(S(a,b,secondary), 0, a.Id, T(6)).State;
        var orphan = result.Find(secondary.Id);
        Assert.Equal(AssociationKind.Independent, orphan.Association);
        Assert.Equal(T(8), orphan.Start);
        Assert.True(orphan.OrphanNotice);
        Assert.Equal(T(6), result.Find(b.Id).Start);
        // The warning belongs to the media. There is no acknowledgement action. 
    }

    [Fact] public void MovingOrphanClearsNoticeAndUndoRestoresIt()
    {
        var main = C(0, 0, 10);
        var secondary = C(1, 8, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = main.Id, ParentOffset = T(8)
        };
        var session = new TimelineSession(S(main, secondary));
        session.Execute(s => TimelineEngine.ResizeMain(s, s.Version, main.Id, T(6)));
        var orphan = session.Current.Find(secondary.Id);
        Assert.True(orphan.OrphanNotice);
        Assert.Equal(AssociationKind.Independent, orphan.Association);
        Assert.Equal(T(8), orphan.Start);

        session.Execute(s => TimelineEngine.DragSecondary(s, s.Version, secondary.Id, 1, T(9)));
        Assert.False(session.Current.Find(secondary.Id).OrphanNotice);
        Assert.Equal(T(9), session.Current.Find(secondary.Id).Start);

        session.Undo();
        Assert.True(session.Current.Find(secondary.Id).OrphanNotice);
        Assert.Equal(T(8), session.Current.Find(secondary.Id).Start);
        Assert.Equal(AssociationKind.Independent, session.Current.Find(secondary.Id).Association);

        session.Redo();
        Assert.False(session.Current.Find(secondary.Id).OrphanNotice);
        Assert.Equal(T(9), session.Current.Find(secondary.Id).Start);
    }

    [Fact] public void MovingOrphanToAnotherTrackDismissesNoticeAndUndoBringsItBack()
    {
        var orphan = C(1, 8, 2) with { OrphanNotice = true };
        var session = new TimelineSession(S(orphan));
        session.Execute(s => TimelineEngine.DragSecondary(s, s.Version, orphan.Id, 2, T(8)));
        Assert.Equal(2, session.Current.Find(orphan.Id).Track);
        Assert.False(session.Current.Find(orphan.Id).OrphanNotice);
        session.Undo();
        Assert.Equal(1, session.Current.Find(orphan.Id).Track);
        Assert.True(session.Current.Find(orphan.Id).OrphanNotice);
    }

    [Fact] public void DragOrphanWithoutChangingTrackOrTimeKeepsNotice()
    {
        var orphan = C(3, 8, 2) with { OrphanNotice = true };
        var result = TimelineEngine.DragSecondary(S(orphan), 0, orphan.Id, 3, T(8)).State;
        Assert.True(result.Find(orphan.Id).OrphanNotice);
        Assert.Equal(T(8), result.Find(orphan.Id).Start);
    }

    [Fact] public void FailedOrphanMovementPreservesNoticeAndSessionHistory()
    {
        var orphan = C(1, 12, 2) with { OrphanNotice = true };
        var blockers = Enumerable.Range(1, 9).Select(track => C(track, 0, 10)).ToArray();
        var session = new TimelineSession(S([orphan, ..blockers]));
        var error = Assert.Throws<TimelineException>(() =>
            session.Execute(s => TimelineEngine.DragSecondary(s, s.Version, orphan.Id, 1, T(3))));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.False(session.CanUndo);
        Assert.True(session.Current.Find(orphan.Id).OrphanNotice);
        Assert.Equal(T(12), session.Current.Find(orphan.Id).Start);
    }

    [Fact] public void UndoAndRedoOfResizeRestoreNoticeCreation()
    {
        var main = C(0, 0, 10);
        var secondary = C(1, 8, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = main.Id, ParentOffset = T(8)
        };
        var session = new TimelineSession(S(main, secondary));
        Assert.False(session.Current.Find(secondary.Id).OrphanNotice);
        session.Execute(s => TimelineEngine.ResizeMain(s, s.Version, main.Id, T(6)));
        Assert.True(session.Current.Find(secondary.Id).OrphanNotice);
        session.Undo();
        Assert.False(session.Current.Find(secondary.Id).OrphanNotice);
        Assert.Equal(AssociationKind.Positioned, session.Current.Find(secondary.Id).Association);
        session.Redo();
        Assert.True(session.Current.Find(secondary.Id).OrphanNotice);
    }

    [Fact] public void DeleteMainDetachesAndPreservesSecondaryTime()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var secondary = C(1, 3, 2) with { Association = AssociationKind.Positioned, ParentId = a.Id, ParentOffset = T(3) };
        var result = TimelineEngine.DeleteMain(S(a,b,secondary), 0, a.Id).State;
        Assert.Equal(T(0), result.Find(b.Id).Start);
        Assert.Equal(T(3), result.Find(secondary.Id).Start);
        Assert.Equal(AssociationKind.Independent, result.Find(secondary.Id).Association);
    }

    [Fact] public void SecondaryExtensionMovesToFirstAvailableTrack()
    {
        var a = C(1, 0, 3, MediaKind.Image); var obstacle = C(1, 4, 5);
        var result = TimelineEngine.ResizeSecondary(S(a,obstacle), 0, a.Id, T(6)).State;
        Assert.Equal(2, result.Find(a.Id).Track);
        Assert.Equal(T(6), result.Find(a.Id).Duration);
    }

    [Fact] public void SecondaryExtensionFailsAtomicallyWithAllTracksBlocked()
    {
        var a = C(1, 0, 3, MediaKind.Image); var obstacle = C(1, 4, 5);
        var blockers = Enumerable.Range(2,8).Select(t => C(t,0,10)).ToArray();
        var original = S([a,obstacle,..blockers]);
        var error = Assert.Throws<TimelineException>(() => TimelineEngine.ResizeSecondary(original, 0, a.Id, T(6)));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.Equal(T(3), original.Find(a.Id).Duration);
    }

    [Fact] public void SessionSupportsUndoRedoAndRejectsStaleVersions()
    {
        var clip = C(1,0,2); var session = new TimelineSession(S(clip));
        session.Execute(s => TimelineEngine.DragSecondary(s,s.Version,clip.Id,1,T(4)));
        Assert.Equal(T(4), session.Current.Find(clip.Id).Start);
        session.Undo(); Assert.Equal(T(0), session.Current.Find(clip.Id).Start);
        session.Redo(); Assert.Equal(T(4), session.Current.Find(clip.Id).Start);
        var err = Assert.Throws<TimelineException>(() => TimelineEngine.DragSecondary(session.Current,0,clip.Id,1,T(5)));
        Assert.Equal("VERSION_CONFLICT",err.Code);
    }

    [Fact] public void OverlapDoesNotAutoAssociateIndependentClip()
    {
        var a = C(0,0,10); var b = C(0,10,10); var independent = C(1,12,2);
        var result = TimelineEngine.ReorderMain(S(a,b,independent),0,b.Id,0).State;
        Assert.Equal(AssociationKind.Independent,result.Find(independent.Id).Association);
        Assert.Equal(T(12),result.Find(independent.Id).Start);
    }
    [Fact] public void SplitMainReassociatesSecondariesStartingInRightHalf()
    {
        var main = C(0, 0, 10);
        var leftChild = C(1, 2, 2) with { Association = AssociationKind.Positioned, ParentId = main.Id, ParentOffset = T(2) };
        var rightChild = C(2, 7, 2) with { Association = AssociationKind.Positioned, ParentId = main.Id, ParentOffset = T(7) };
        var newId = Guid.NewGuid();
        var result = TimelineEngine.Split(S(main, leftChild, rightChild), 0, main.Id, T(5), newId).State;
        Assert.Equal(T(5), result.Find(main.Id).Duration);
        Assert.Equal(T(5), result.Find(newId).Start);
        Assert.Equal(T(5), result.Find(newId).Duration);
        Assert.Equal(main.Id, result.Find(leftChild.Id).ParentId);
        Assert.Equal(newId, result.Find(rightChild.Id).ParentId);
        Assert.Equal(T(2), result.Find(rightChild.Id).ParentOffset);
        Assert.Equal(T(7), result.Find(rightChild.Id).Start);
        TimelineEngine.Validate(result);
    }

    [Fact] public void SplitPositionedSecondaryPreservesParentOffsets()
    {
        var main = C(0, 0, 12);
        var child = C(1, 3, 6) with { Association = AssociationKind.Positioned, ParentId = main.Id, ParentOffset = T(3) };
        var id = Guid.NewGuid();
        var result = TimelineEngine.Split(S(main, child), 0, child.Id, T(5), id).State;
        Assert.Equal(T(2), result.Find(child.Id).Duration);
        Assert.Equal(T(4), result.Find(id).Duration);
        Assert.Equal(T(5), result.Find(id).ParentOffset);
        Assert.Equal(main.Id, result.Find(id).ParentId);
        TimelineEngine.Validate(result);
    }

    [Fact] public void InvalidSplitIsAtomic()
    {
        var main = C(0, 0, 10);
        var state = S(main);
        var error = Assert.Throws<TimelineException>(() => TimelineEngine.Split(state, 0, main.Id, T(10), Guid.NewGuid()));
        Assert.Equal("INVALID_SPLIT", error.Code);
        Assert.Single(state.Clips);
        Assert.Equal(0, state.Version);
    }

    [Fact] public void DeleteMainReportsAllAffectedClipIds()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var attached = C(1, 12, 2) with { Association = AssociationKind.Positioned, ParentId = b.Id, ParentOffset = T(2) };
        var result = TimelineEngine.DeleteMain(S(a, b, attached), 0, a.Id);
        Assert.Contains(a.Id, result.ChangedClipIds);
        Assert.Contains(b.Id, result.ChangedClipIds);
        Assert.Contains(attached.Id, result.ChangedClipIds);
    }

    [Fact] public void DeleteSecondaryDoesNotMoveOtherClips()
    {
        var a = C(0, 0, 10); var b = C(1, 2, 3); var c = C(2, 4, 2);
        var result = TimelineEngine.DeleteSecondary(S(a, b, c), 0, b.Id);
        Assert.Equal(T(4), result.State.Find(c.Id).Start);
        Assert.DoesNotContain(result.State.Clips, clip => clip.Id == b.Id);
    }

    [Fact] public void ReorderMainToSameIndexKeepsPositions()
    {
        var a = C(0, 0, 4); var b = C(0, 4, 6);
        var result = TimelineEngine.ReorderMain(S(a,b), 0, a.Id, 0).State;
        Assert.Equal(T(0), result.Find(a.Id).Start);
        Assert.Equal(T(4), result.Find(b.Id).Start);
        TimelineEngine.Validate(result);
    }

    [Fact] public void ReorderLastMainToFirstRipplesAndPreservesIndependent()
    {
        var a = C(0, 0, 4); var b = C(0, 4, 6); var c = C(0, 10, 5);
        var positioned = C(1, 11, 2) with { Association = AssociationKind.Positioned, ParentId = c.Id, ParentOffset = T(1) };
        var independent = C(2, 3, 2);
        var result = TimelineEngine.ReorderMain(S(a,b,c,positioned,independent), 0, c.Id, 0).State;
        Assert.Equal(T(0), result.Find(c.Id).Start);
        Assert.Equal(T(5), result.Find(a.Id).Start);
        Assert.Equal(T(9), result.Find(b.Id).Start);
        Assert.Equal(T(1), result.Find(positioned.Id).Start);
        Assert.Equal(T(3), result.Find(independent.Id).Start);
        TimelineEngine.Validate(result);
    }

    [Fact] public void ReorderMainCollisionRelocatesAssociatedClip()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var attached = C(1, 11, 4) with { Association = AssociationKind.Positioned, ParentId = b.Id, ParentOffset = T(1) };
        var obstacle = C(1, 2, 4);
        var initial = S(a,b,attached,obstacle);
        var result = TimelineEngine.ReorderMain(initial, 0, b.Id, 0).State;
        Assert.Equal(2, result.Find(attached.Id).Track);
        Assert.Equal(T(1), result.Find(attached.Id).Start);
        Assert.Equal(b.Id, result.Find(attached.Id).ParentId);
        Assert.Equal(T(2), result.Find(obstacle.Id).Start);
        Assert.Equal(T(10), initial.Find(b.Id).Start);
        Assert.Equal(T(11), initial.Find(attached.Id).Start);
    }

    [Fact] public void ReorderMainWithStaleVersionIsRejected()
    {
        var a = C(0, 0, 5); var b = C(0, 5, 5);
        var error = Assert.Throws<TimelineException>(() => TimelineEngine.ReorderMain(S(a,b), 7, b.Id, 0));
        Assert.Equal("VERSION_CONFLICT", error.Code);
    }

    [Fact] public void CollisionRelocationPreservesAssociationAndEffects()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var attached = C(1, 11, 3) with { Association = AssociationKind.Positioned,
            ParentId = b.Id, ParentOffset = T(1), EffectsToken = "fade-in" };
        var obstacle = C(1, 1, 4);
        var result = TimelineEngine.ReorderMain(S(a,b,attached,obstacle), 0, b.Id, 0).State;
        Assert.Equal(2, result.Find(attached.Id).Track);
        Assert.Equal(AssociationKind.Positioned, result.Find(attached.Id).Association);
        Assert.Equal("fade-in", result.Find(attached.Id).EffectsToken);
        TimelineEngine.Validate(result);
    }

    [Fact] public void CollisionWithNoAvailableSecondaryTrackIsAtomic()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var attached = C(1, 11, 3) with { Association = AssociationKind.Positioned,
            ParentId = b.Id, ParentOffset = T(1) };
        var obstacles = Enumerable.Range(1, 9).Select(track => C(track, 1, 4)).ToArray();
        var original = S([a,b,attached,..obstacles]);
        var error = Assert.Throws<TimelineException>(() => TimelineEngine.ReorderMain(original, 0, b.Id, 0));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.Equal(T(11), original.Find(attached.Id).Start);
        Assert.Equal(1, original.Find(attached.Id).Track);
    }

    [Fact] public void UndoRedoRestoresAutomaticTrackRelocation()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var attached = C(1, 11, 3) with { Association = AssociationKind.Positioned,
            ParentId = b.Id, ParentOffset = T(1) };
        var session = new TimelineSession(S(a,b,attached,C(1, 1, 4)));
        session.Execute(s => TimelineEngine.ReorderMain(s, s.Version, b.Id, 0));
        Assert.Equal(2, session.Current.Find(attached.Id).Track);
        session.Undo();
        Assert.Equal(1, session.Current.Find(attached.Id).Track);
        Assert.Equal(T(11), session.Current.Find(attached.Id).Start);
        session.Redo();
        Assert.Equal(2, session.Current.Find(attached.Id).Track);
        Assert.Equal(T(1), session.Current.Find(attached.Id).Start);
    }

    [Fact] public void AdjacentMovingClipsCanShareOriginalTrack()
    {
        var a = C(0, 0, 10); var b = C(0, 10, 10);
        var x = C(1, 11, 3) with { Association = AssociationKind.Positioned,
            ParentId = b.Id, ParentOffset = T(1) };
        var y = C(1, 14, 3) with { Association = AssociationKind.Positioned,
            ParentId = b.Id, ParentOffset = T(4) };
        var result = TimelineEngine.ReorderMain(S(a,b,x,y), 0, b.Id, 0).State;
        Assert.Equal(1, result.Find(x.Id).Track);
        Assert.Equal(1, result.Find(y.Id).Track);
        Assert.Equal(T(1), result.Find(x.Id).Start);
        Assert.Equal(T(4), result.Find(y.Id).Start);
    }

    [Fact] public void SecondaryPreviewSelectsNextFreeTrackWithoutChangingOriginal()
    {
        var main = C(0, 0, 20); var blocker = C(1, 2, 8); var moving = C(2, 12, 3);
        var original = S(main, blocker, moving);
        var preview = TimelineEngine.PreviewDragSecondary(original, 0, moving.Id, 1, T(4));
        Assert.Equal(2, preview.State.Find(moving.Id).Track);
        Assert.Equal(T(4), preview.State.Find(moving.Id).Start);
        Assert.Equal(T(12), original.Find(moving.Id).Start);
        Assert.Equal(0, original.Version);
    }

    [Fact] public void SecondaryPreviewFailureDoesNotMutateState()
    {
        var main = C(0, 0, 20); var moving = C(1, 12, 3);
        var blockers = Enumerable.Range(1, 9).Select(i => C(i, 2, 8)).ToArray();
        var original = S([main, moving, ..blockers]);
        var error = Assert.Throws<TimelineException>(() => TimelineEngine.PreviewDragSecondary(original, 0, moving.Id, 1, T(4)));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.Equal(T(12), original.Find(moving.Id).Start);
    }

    [Fact] public void SecondaryPreviewDoesNotAffectUndoHistory()
    {
        var main = C(0, 0, 20); var moving = C(1, 12, 3);
        var session = new TimelineSession(S(main, moving));
        _ = TimelineEngine.PreviewDragSecondary(session.Current, session.Current.Version, moving.Id, 1, T(4));
        Assert.False(session.CanUndo);
        Assert.Equal(T(12), session.Current.Find(moving.Id).Start);
    }

    [Fact] public void SecondaryDragRelocationCanUndoAndRedo()
    {
        var main = C(0, 0, 20); var blocker = C(1, 2, 8); var moving = C(1, 12, 3);
        var session = new TimelineSession(S(main, blocker, moving));
        session.Execute(s => TimelineEngine.DragSecondary(s, s.Version, moving.Id, 1, T(4)));
        Assert.Equal(2, session.Current.Find(moving.Id).Track);
        session.Undo();
        Assert.Equal(1, session.Current.Find(moving.Id).Track);
        Assert.Equal(T(12), session.Current.Find(moving.Id).Start);
        session.Redo();
        Assert.Equal(2, session.Current.Find(moving.Id).Track);
    }

    [Fact] public void DuplicateSecondaryChoosesFirstAvailableTrackAndKeepsAssociation()
    {
        var main = C(0, 0, 20);
        var source = C(1, 2, 4) with { Association = AssociationKind.Positioned,
            ParentId = main.Id, ParentOffset = T(2), EffectsToken = "efeito" };
        var id = Guid.NewGuid();
        var result = TimelineEngine.DuplicateSecondary(S(main, source), 0, source.Id, id).State;
        var copy = result.Find(id);
        Assert.Equal(2, copy.Track);
        Assert.Equal(source.MediaId, copy.MediaId);
        Assert.Equal(source.ParentId, copy.ParentId);
        Assert.Equal(source.ParentOffset, copy.ParentOffset);
        Assert.Equal("efeito", copy.EffectsToken);
        TimelineEngine.Validate(result);
    }

    [Fact] public void DuplicateSecondaryPreservesIndependence()
    {
        var main = C(0, 0, 20); var source = C(1, 3, 5);
        var result = TimelineEngine.DuplicateSecondary(S(main, source), 0, source.Id, Guid.NewGuid()).State;
        Assert.All(result.Clips.Where(c => c.Track > 0), c => Assert.Equal(AssociationKind.Independent, c.Association));
    }

    [Fact] public void DuplicateSecondaryFailsAtomicallyWhenAllTracksOccupied()
    {
        var main = C(0, 0, 20);
        var source = C(1, 3, 5);
        var blockers = Enumerable.Range(2, 8).Select(i => C(i, 3, 5)).ToArray();
        var state = S([main, source, ..blockers]);
        var error = Assert.Throws<TimelineException>(() => TimelineEngine.DuplicateSecondary(state, 0, source.Id, Guid.NewGuid()));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.Equal(0, state.Version);
        Assert.Equal(10, state.Clips.Count);
    }

    [Fact] public void DuplicateSecondaryCanUndoAndRedo()
    {
        var main = C(0, 0, 20); var source = C(1, 3, 5);
        var session = new TimelineSession(S(main, source));
        var id = Guid.NewGuid();
        session.Execute(s => TimelineEngine.DuplicateSecondary(s, s.Version, source.Id, id));
        Assert.Equal(3, session.Current.Clips.Count);
        session.Undo();
        Assert.Equal(2, session.Current.Clips.Count);
        session.Redo();
        Assert.Equal(3, session.Current.Clips.Count);
        Assert.Equal(2, session.Current.Find(id).Track);
    }


    [Fact] public void AudioCanBeSplitAtCursorAndKeepsMediaReference()
    {
        var audio = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Audio, 2, T(2), T(10));
        var rightId = Guid.NewGuid();
        var state = TimelineEngine.Split(S(audio), 0, audio.Id, T(6), rightId).State;
        var left = state.Find(audio.Id);
        var right = state.Find(rightId);
        Assert.Equal(MediaKind.Audio, right.Kind);
        Assert.Equal(audio.MediaId, right.MediaId);
        Assert.Equal(T(4), left.Duration);
        Assert.Equal(T(6), right.Start);
        Assert.Equal(T(6), right.Duration);
    }

    [Fact] public void SplitPositionedAudioPreservesParentAndRelativeOffset()
    {
        var main = C(0, 0, 20);
        var audio = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Audio, 2, T(3), T(10),
            AssociationKind.Positioned, main.Id, T(3));
        var rightId = Guid.NewGuid();
        var state = TimelineEngine.Split(S(main, audio), 0, audio.Id, T(8), rightId).State;
        Assert.Equal(main.Id, state.Find(rightId).ParentId);
        Assert.Equal(T(8), state.Find(rightId).ParentOffset);
        Assert.Equal(T(5), state.Find(audio.Id).Duration);
    }
    // v5.6.1 — navigation must follow the spatial arrangement seen by the user.
    [Fact] public void CtrlRightFromAudioSelectsImageOnSameTrackNotMainClip()
    {
        var audio = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Audio, 3, T(1), T(6));
        var image = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image, 3, T(9), T(3));
        var main = C(0, 7, 2); // chronologically between audio and image
        var next = TimelineNavigation.FindNeighbor(S(audio, main, image), audio.Id, NavigationDirection.Right);
        Assert.Equal(image.Id, next?.Id);
        Assert.Equal(3, next?.Track);
    }

    [Fact] public void CtrlLeftSelectsPreviousClipOnlyOnSameTrack()
    {
        var first = C(3, 0, 3);
        var second = C(3, 8, 4);
        var earlierMain = C(0, 7, 1);
        var next = TimelineNavigation.FindNeighbor(S(first, earlierMain, second), second.Id, NavigationDirection.Left);
        Assert.Equal(first.Id, next?.Id);
    }

    [Fact] public void CtrlRightAtEndOfTrackGoesToFirstClipOfNextOccupiedTrack()
    {
        var audio = C(3, 4, 3);
        var firstNext = C(5, 2, 2);
        var secondNext = C(5, 12, 2);
        var earlierMain = C(0, 8, 3);
        var state = S(audio, firstNext, secondNext, earlierMain);
        Assert.Equal(firstNext.Id,
            TimelineNavigation.FindNeighbor(state, audio.Id, NavigationDirection.Right)?.Id);
    }

    [Fact] public void CtrlLeftAtStartOfTrackGoesToLastClipOfPreviousOccupiedTrack()
    {
        var first = C(2, 1, 2);
        var last = C(2, 13, 2);
        var current = C(5, 1, 4);
        Assert.Equal(last.Id,
            TimelineNavigation.FindNeighbor(S(first, last, current), current.Id, NavigationDirection.Left)?.Id);
    }

    [Fact] public void CtrlRightPrefersCurrentTrackBeforeProceedingToNext()
    {
        var current = C(3, 1, 5);
        var neighbor = C(3, 8, 2);
        var nextTrack = C(4, 2, 3);
        Assert.Equal(neighbor.Id,
            TimelineNavigation.FindNeighbor(S(current, neighbor, nextTrack), current.Id, NavigationDirection.Right)?.Id);
    }

    [Fact] public void CtrlLeftPrefersCurrentTrackBeforeProceedingToPrevious()
    {
        var previousTrack = C(2, 4, 2);
        var neighbor = C(3, 5, 3);
        var current = C(3, 12, 3);
        Assert.Equal(neighbor.Id,
            TimelineNavigation.FindNeighbor(S(previousTrack, neighbor, current), current.Id, NavigationDirection.Left)?.Id);
    }

    [Fact] public void HorizontalNavigationSkipsEmptyTracksAndCanGoBack()
    {
        var top = C(1, 6, 2);
        var lower = C(8, 1, 2);
        var state = S(top, lower);
        var down = TimelineNavigation.FindNeighbor(state, top.Id, NavigationDirection.Right);
        Assert.Equal(lower.Id, down?.Id);
        Assert.Equal(top.Id, TimelineNavigation.FindNeighbor(state, lower.Id, NavigationDirection.Left)?.Id);
    }

    [Fact] public void HorizontalNavigationFromMainTrackReachesSecondary()
    {
        var main = C(0, 0, 8);
        var secondary = C(1, 2, 4);
        Assert.Equal(secondary.Id,
            TimelineNavigation.FindNeighbor(S(main, secondary), main.Id, NavigationDirection.Right)?.Id);
    }

    [Fact] public void HorizontalNavigationNeverCyclesFromLastRowToFirst()
    {
        var top = C(0, 0, 8);
        var bottom = C(9, 0, 8);
        var state = S(top, bottom);
        Assert.Null(TimelineNavigation.FindNeighbor(state, bottom.Id, NavigationDirection.Right));
        Assert.Null(TimelineNavigation.FindNeighbor(state, top.Id, NavigationDirection.Left));
    }

    [Fact] public void CtrlUpPrefersHorizontalOverlapOverCloserStartTime()
    {
        var current = C(4, 10, 5);  // spans 10..15
        var outside = C(2, 8, 1);   // starts closer than overlapping clip but ends at 9
        var aligned = C(2, 12, 5);  // overlaps the current clip
        Assert.Equal(aligned.Id,
            TimelineNavigation.FindNeighbor(S(current, outside, aligned), current.Id, NavigationDirection.Up)?.Id);
    }

    [Fact] public void CtrlDownSkipsEmptyRowsAndChoosesNearestOccupiedTrack()
    {
        var current = C(1, 10, 5);
        var nearTrack = C(4, 13, 2);
        var farTrack = C(7, 10, 5);
        Assert.Equal(nearTrack.Id,
            TimelineNavigation.FindNeighbor(S(current, nearTrack, farTrack), current.Id, NavigationDirection.Down)?.Id);
    }

    [Fact] public void NavigationWithoutSelectionDoesNothing()
    {
        var state = S(C(0, 0, 8), C(2, 3, 2));
        foreach (var direction in Enum.GetValues<NavigationDirection>())
            Assert.Null(TimelineNavigation.FindNeighbor(state, null, direction));
    }

    [Fact] public void NavigationAtOuterEdgeDoesNotWrapAround()
    {
        var first = C(0, 0, 4);
        var last = C(9, 5, 3);
        var state = S(first, last);
        Assert.Null(TimelineNavigation.FindNeighbor(state, first.Id, NavigationDirection.Left));
        Assert.Null(TimelineNavigation.FindNeighbor(state, last.Id, NavigationDirection.Right));
        Assert.Null(TimelineNavigation.FindNeighbor(state, last.Id, NavigationDirection.Down));
    }

    [Fact] public void VisualNavigationDoesNotMutateTimeline()
    {
        var source = C(3, 0, 4);
        var target = C(3, 5, 4);
        var state = S(source, target);
        var selected = TimelineNavigation.FindNeighbor(state, source.Id, NavigationDirection.Right);
        Assert.Equal(target.Id, selected?.Id);
        Assert.Equal(0, state.Version);
        Assert.Equal(T(0), state.Find(source.Id).Start);
        Assert.Equal(T(5), state.Find(target.Id).Start);
    }


    // v5.7 — resize by the right edge: the UI previews these pure domain
    // commands, then commits exactly one operation when the mouse is released.

    [Fact] public void ResizeMainRipplesFollowingMainAndAssociatedClips()
    {
        var a = C(0, 0, 10);
        var b = C(0, 10, 8);
        var audio = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Audio, 4,
            T(12), T(3), AssociationKind.Positioned, b.Id, T(2));
        var fixedImage = C(5, 21, 3);
        var state = TimelineEngine.RestoreMainDuration(S(a, b, audio, fixedImage), 0, a.Id, T(14)).State;
        Assert.Equal(T(14), state.Find(a.Id).Duration);
        Assert.Equal(T(14), state.Find(b.Id).Start);
        Assert.Equal(T(16), state.Find(audio.Id).Start);
        Assert.Equal(AssociationKind.Positioned, state.Find(audio.Id).Association);
        Assert.Equal(T(21), state.Find(fixedImage.Id).Start);
        TimelineEngine.Validate(state);
    }

    [Fact] public void ResizeMainToShorterDurationGeneratesOrphanNoticeOnSecondary()
    {
        var a = C(0, 0, 10);
        var secondary = C(2, 8, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = a.Id, ParentOffset = T(8)
        };
        var result = TimelineEngine.ResizeMain(S(a, secondary), 0, a.Id, T(6)).State;
        Assert.Equal(T(6), result.Find(a.Id).Duration);
        Assert.True(result.Find(secondary.Id).OrphanNotice);
        Assert.Equal(AssociationKind.Independent, result.Find(secondary.Id).Association);
        Assert.Equal(T(8), result.Find(secondary.Id).Start);
    }

    [Fact] public void ResizeSecondaryInsideFreeSpacePreservesTrackAndParentOffset()
    {
        var main = C(0, 0, 20);
        var image = new Clip(Guid.NewGuid(), Guid.NewGuid(), MediaKind.Image, 6,
            T(3), T(2), AssociationKind.Positioned, main.Id, T(3), EffectsToken: "crop");
        var state = TimelineEngine.ResizeSecondary(S(main, image), 0, image.Id, T(7)).State;
        var edited = state.Find(image.Id);
        Assert.Equal(6, edited.Track);
        Assert.Equal(T(7), edited.Duration);
        Assert.Equal(main.Id, edited.ParentId);
        Assert.Equal(T(3), edited.ParentOffset);
        Assert.Equal("crop", edited.EffectsToken);
    }

    [Fact] public void ResizeSecondaryCollisionMovesToFirstAvailableTrack()
    {
        var moved = C(2, 1, 2, MediaKind.Image);
        var blocker = C(2, 5, 6);
        var firstLaneBusy = C(1, 4, 1);
        var result = TimelineEngine.ResizeSecondary(S(moved, blocker, firstLaneBusy), 0,
            moved.Id, T(6)).State;
        Assert.Equal(3, result.Find(moved.Id).Track);
        Assert.Equal(T(1), result.Find(moved.Id).Start);
        Assert.Equal(T(6), result.Find(moved.Id).Duration);
        Assert.Equal(2, result.Find(blocker.Id).Track);
    }

    [Fact] public void ResizeSecondaryFailsAtomicallyWhenAllTenTracksUnavailable()
    {
        var moved = C(1, 0, 2, MediaKind.Image);
        var blockers = Enumerable.Range(1, 9).Select(t => C(t, 3, 4)).ToArray();
        var session = new TimelineSession(S([moved, ..blockers]));
        var error = Assert.Throws<TimelineException>(() => session.Execute(state =>
            TimelineEngine.ResizeSecondary(state, state.Version, moved.Id, T(5))));
        Assert.Equal("NO_FREE_TRACK", error.Code);
        Assert.Equal(0, session.Current.Version);
        Assert.Equal(T(2), session.Current.Find(moved.Id).Duration);
        Assert.Equal(1, session.Current.Find(moved.Id).Track);
        Assert.False(session.CanUndo);
    }

    [Theory]
    [InlineData(MediaKind.Audio)]
    [InlineData(MediaKind.Image)]
    [InlineData(MediaKind.Video)]
    public void ResizeRightEdgeCanShortenAudioImageAndVideo(MediaKind kind)
    {
        var clip = new Clip(Guid.NewGuid(), Guid.NewGuid(), kind, 7, T(2), T(3));
        var result = TimelineEngine.ResizeSecondary(S(clip), 0, clip.Id,
            MediaTime.FromSeconds(1.5m)).State.Find(clip.Id);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(MediaTime.FromSeconds(1.5m), result.Duration);
        Assert.Equal(T(2), result.Start);
    }

    [Fact] public void ResizePreviewDoesNotChangeSessionOrUndoHistory()
    {
        var first = C(0, 0, 6);
        var second = C(0, 6, 6);
        var session = new TimelineSession(S(first, second));
        var visual = TimelineEngine.PreviewResizeMain(session.Current,
            session.Current.Version, first.Id, T(4));
        Assert.Equal(T(4), visual.State.Find(first.Id).Duration);
        Assert.Equal(T(4), visual.State.Find(second.Id).Start);
        Assert.Equal(T(6), session.Current.Find(first.Id).Duration);
        Assert.Equal(T(6), session.Current.Find(second.Id).Start);
        Assert.False(session.CanUndo);
        Assert.Equal(0, session.Current.Version);
    }

    [Fact] public void ResizeSecondaryPreviewShowsAutomaticRelocationWithoutCommit()
    {
        var moving = C(1, 2, 2, MediaKind.Image);
        var fixedClip = C(1, 5, 3);
        var session = new TimelineSession(S(moving, fixedClip));
        var ghost = TimelineEngine.PreviewResizeSecondary(session.Current,
            session.Current.Version, moving.Id, T(5)).State.Find(moving.Id);
        Assert.Equal(2, ghost.Track);
        Assert.Equal(T(5), ghost.Duration);
        Assert.Equal(1, session.Current.Find(moving.Id).Track);
        Assert.False(session.CanUndo);
    }

    [Fact] public void ResizeSecondaryCanUndoAndRedoTrackAndDuration()
    {
        var moving = C(1, 0, 2, MediaKind.Image);
        var obstacle = C(1, 3, 3);
        var session = new TimelineSession(S(moving, obstacle));
        session.Execute(s => TimelineEngine.ResizeSecondary(s, s.Version, moving.Id, T(4)));
        Assert.Equal(2, session.Current.Find(moving.Id).Track);
        Assert.Equal(T(4), session.Current.Find(moving.Id).Duration);
        session.Undo();
        Assert.Equal(1, session.Current.Find(moving.Id).Track);
        Assert.Equal(T(2), session.Current.Find(moving.Id).Duration);
        session.Redo();
        Assert.Equal(2, session.Current.Find(moving.Id).Track);
        Assert.Equal(T(4), session.Current.Find(moving.Id).Duration);
    }

    [Fact] public void MainRightEdgeResizeUndoRestoresOrphanStatusAndNeighborPosition()
    {
        var a = C(0, 0, 10);
        var b = C(0, 10, 5);
        var linked = C(2, 8, 2) with
        {
            Association = AssociationKind.Positioned, ParentId = a.Id, ParentOffset = T(8)
        };
        var session = new TimelineSession(S(a, b, linked));
        session.Execute(s => TimelineEngine.ResizeMain(s, s.Version, a.Id, T(5)));
        Assert.Equal(T(5), session.Current.Find(b.Id).Start);
        Assert.True(session.Current.Find(linked.Id).OrphanNotice);
        session.Undo();
        Assert.Equal(T(10), session.Current.Find(b.Id).Start);
        Assert.False(session.Current.Find(linked.Id).OrphanNotice);
        Assert.Equal(AssociationKind.Positioned, session.Current.Find(linked.Id).Association);
        session.Redo();
        Assert.Equal(T(5), session.Current.Find(b.Id).Start);
        Assert.True(session.Current.Find(linked.Id).OrphanNotice);
    }

    [Fact] public void ResizeSecondaryDoesNotSilentlyDismissExistingOrphanNotice()
    {
        var orphan = C(5, 7, 2) with { OrphanNotice = true };
        var session = new TimelineSession(S(orphan));
        session.Execute(s => TimelineEngine.ResizeSecondary(s, s.Version, orphan.Id, T(1)));
        Assert.True(session.Current.Find(orphan.Id).OrphanNotice);
        session.Undo();
        Assert.True(session.Current.Find(orphan.Id).OrphanNotice);
        Assert.Equal(T(2), session.Current.Find(orphan.Id).Duration);
    }

    [Fact] public void ResizeAcceptsTenthsOfSecondsAndRejectsZeroDuration()
    {
        var clip = C(3, 0, 4);
        var state = S(clip);
        var result = TimelineEngine.ResizeSecondary(state, 0, clip.Id,
            MediaTime.FromSeconds(.1m)).State.Find(clip.Id);
        Assert.Equal(MediaTime.FromSeconds(.1m), result.Duration);
        Assert.Throws<TimelineException>(() => TimelineEngine.ResizeSecondary(state, 0,
            clip.Id, MediaTime.Zero));
    }
}
