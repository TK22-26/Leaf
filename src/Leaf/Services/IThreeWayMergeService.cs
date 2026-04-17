using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for performing three-way merges on file content.
/// </summary>
/// <remarks>
/// Phase 1 adapter surface. The implementation delegates to
/// <see cref="Leaf.Services.Merge.IMergeEngine"/> (backed by <c>git merge-file</c>)
/// and translates the result into the legacy <see cref="FileMergeResult"/> shape so
/// existing UI continues to function. This interface is scheduled for deletion in
/// Phase 2c once the new merge view consumes <see cref="Leaf.Models.Merge.MergeDocument"/>
/// directly.
/// </remarks>
public interface IThreeWayMergeService
{
    /// <summary>
    /// Perform a three-way merge on file content.
    /// </summary>
    /// <param name="baseContent">The common ancestor content (before both branches diverged).</param>
    /// <param name="oursContent">Content from the current branch (HEAD / "ours").</param>
    /// <param name="theirsContent">Content from the incoming branch ("theirs").</param>
    /// <param name="ignoreWhitespace">When <c>true</c>, passes <c>--ignore-all-space</c> to the engine.</param>
    /// <param name="cancellationToken">Cancels the merge. The underlying git process is killed on cancel.</param>
    Task<FileMergeResult> PerformMergeAsync(
        string baseContent,
        string oursContent,
        string theirsContent,
        bool ignoreWhitespace = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform a three-way merge with file path metadata.
    /// </summary>
    Task<FileMergeResult> PerformMergeAsync(
        string filePath,
        string baseContent,
        string oursContent,
        string theirsContent,
        bool ignoreWhitespace = false,
        CancellationToken cancellationToken = default);
}
