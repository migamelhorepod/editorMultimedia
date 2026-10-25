namespace Editor.Timeline;

/// <summary>In-memory undo/redo. Persistence and UI are intentionally out of scope.</summary>
public sealed class TimelineSession
{
    private readonly Stack<TimelineState> undo = new();
    private readonly Stack<TimelineState> redo = new();
    public TimelineState Current { get; private set; }
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public TimelineSession(TimelineState initial)
    {
        TimelineEngine.Validate(initial);
        Current = initial;
    }

    public CommandResult Execute(Func<TimelineState, CommandResult> command)
    {
        var result = command(Current);
        if (result.State.Version != Current.Version + 1)
            throw new TimelineException("INVALID_COMMAND", "Command must produce next version.");
        undo.Push(Current);
        redo.Clear();
        Current = result.State;
        return result;
    }

    public TimelineState Undo()
    {
        if (!CanUndo) throw new TimelineException("NOTHING_TO_UNDO", "Undo stack is empty.");
        redo.Push(Current);
        Current = undo.Pop();
        return Current;
    }

    public TimelineState Redo()
    {
        if (!CanRedo) throw new TimelineException("NOTHING_TO_REDO", "Redo stack is empty.");
        undo.Push(Current);
        Current = redo.Pop();
        return Current;
    }
}
