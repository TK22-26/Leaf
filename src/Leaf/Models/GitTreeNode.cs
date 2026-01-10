using System.Windows.Media;

namespace Leaf.Models;

/// <summary>
/// Represents a node in the Git graph visualization.
/// Pre-calculated coordinates for efficient rendering.
/// </summary>
public class GitTreeNode
{
    /// <summary>
    /// The commit SHA this node represents.
    /// </summary>
    public string Sha { get; set; } = string.Empty;

    /// <summary>
    /// Lane index (X position) - determined by LaneAllocator.
    /// Lane 0 is reserved for main/master/develop branches.
    /// </summary>
    public int ColumnIndex { get; set; }

    /// <summary>
    /// Row index (Y position) - determined by topological sort order.
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// Color for this node, derived from branch name hash.
    /// </summary>
    public Brush? NodeColor { get; set; }

    /// <summary>
    /// SHAs of parent commits - used to draw connection lines.
    /// </summary>
    public List<string> ParentShas { get; set; } = [];

    /// <summary>
    /// Column indices of parent nodes for drawing connections.
    /// Parallel array with ParentShas.
    /// </summary>
    public List<int> ParentColumns { get; set; } = [];

    /// <summary>
    /// Row indices of parent nodes for drawing connections.
    /// Parallel array with ParentShas.
    /// </summary>
    public List<int> ParentRows { get; set; } = [];

    /// <summary>
    /// True if this is a merge commit (multiple parents).
    /// </summary>
    public bool IsMerge => ParentShas.Count > 1;

    /// <summary>
    /// True if this commit is the current HEAD.
    /// </summary>
    public bool IsHead { get; set; }

    /// <summary>
    /// Branch names pointing to this commit.
    /// </summary>
    public List<string> BranchNames { get; set; } = [];

    /// <summary>
    /// Branch labels with local/remote info for display.
    /// </summary>
    public List<BranchLabel> BranchLabels { get; set; } = [];

    /// <summary>
    /// Tag names pointing to this commit.
    /// </summary>
    public List<string> TagNames { get; set; } = [];

    /// <summary>
    /// The primary branch name for this node (for color assignment).
    /// </summary>
    public string? PrimaryBranch { get; set; }

    /// <summary>
    /// True if this node matches the current search criteria.
    /// Defaults to true (all visible when no search active).
    /// </summary>
    public bool IsSearchMatch { get; set; } = true;
}
