namespace Leaf.Models;

/// <summary>
/// Strategy used to merge a pull request.
/// </summary>
public enum MergeMethod
{
    Merge,
    Squash,
    Rebase
}
