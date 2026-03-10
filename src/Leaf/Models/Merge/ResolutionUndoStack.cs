namespace Leaf.Models.Merge;

/// <summary>
/// Records a single resolution action for undo/redo.
/// </summary>
public record ResolutionAction(int RegionIndex, ConflictResolution PreviousChoice, ConflictResolution NewChoice);

/// <summary>
/// Undo/redo stack for conflict resolution actions.
/// Per-file — clear when switching files.
/// </summary>
public sealed class ResolutionUndoStack
{
    private readonly List<ResolutionAction> _undoStack = [];
    private readonly List<ResolutionAction> _redoStack = [];

    public event EventHandler? StackChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Push(int regionIndex, ConflictResolution previousChoice, ConflictResolution newChoice)
    {
        if (previousChoice == newChoice)
            return;

        _undoStack.Add(new ResolutionAction(regionIndex, previousChoice, newChoice));
        _redoStack.Clear();
        StackChanged?.Invoke(this, EventArgs.Empty);
    }

    public ResolutionAction? Undo()
    {
        if (_undoStack.Count == 0)
            return null;

        var action = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(action);
        StackChanged?.Invoke(this, EventArgs.Empty);
        return action;
    }

    public ResolutionAction? Redo()
    {
        if (_redoStack.Count == 0)
            return null;

        var action = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(action);
        StackChanged?.Invoke(this, EventArgs.Empty);
        return action;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StackChanged?.Invoke(this, EventArgs.Empty);
    }
}
