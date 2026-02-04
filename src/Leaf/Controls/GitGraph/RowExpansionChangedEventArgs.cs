namespace Leaf.Controls.GitGraph;

/// <summary>
/// Event args for row expansion changes.
/// </summary>
public class RowExpansionChangedEventArgs : EventArgs
{
    public int NodeIndex { get; }
    public bool IsExpanded { get; }
    public int ExtraRows { get; }
    public double TotalExpansionHeight { get; }

    public RowExpansionChangedEventArgs(int nodeIndex, bool isExpanded, int extraRows, double totalExpansionHeight)
    {
        NodeIndex = nodeIndex;
        IsExpanded = isExpanded;
        ExtraRows = extraRows;
        TotalExpansionHeight = totalExpansionHeight;
    }
}
