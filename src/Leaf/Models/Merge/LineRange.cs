namespace Leaf.Models.Merge;

/// <summary>
/// A half-open range of lines in a text document. Lines are 1-based.
/// <c>StartLine=5, EndLineExclusive=8</c> represents lines 5, 6, 7 (three lines).
/// <c>StartLine == EndLineExclusive</c> is a zero-length range anchored at that line —
/// used to represent an insertion point (e.g. an empty side of a diff mapping).
/// </summary>
public readonly record struct LineRange(int StartLine, int EndLineExclusive)
{
    public static readonly LineRange Empty = default;

    public int Length => EndLineExclusive - StartLine;

    public bool IsEmpty => Length == 0;

    public int LastLineInclusive => EndLineExclusive - 1;

    public bool Contains(int line) => line >= StartLine && line < EndLineExclusive;

    public bool Overlaps(LineRange other)
        => StartLine < other.EndLineExclusive && other.StartLine < EndLineExclusive;

    public override string ToString()
        => IsEmpty
            ? $"[{StartLine},{EndLineExclusive}) (empty)"
            : $"[{StartLine},{EndLineExclusive})";
}
