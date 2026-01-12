using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for performing three-way merges on file content.
/// Uses diff algorithms to identify unchanged, auto-mergeable, and conflicting regions.
/// </summary>
public interface IThreeWayMergeService
{
    /// <summary>
    /// Perform a three-way merge on file content.
    /// </summary>
    /// <param name="baseContent">The common ancestor content (before both branches diverged)</param>
    /// <param name="oursContent">Content from the current branch (HEAD / "ours")</param>
    /// <param name="theirsContent">Content from the incoming branch ("theirs")</param>
    /// <param name="ignoreWhitespace">Whether to ignore whitespace differences when detecting changes</param>
    /// <returns>A FileMergeResult containing all merge regions</returns>
    FileMergeResult PerformMerge(string baseContent, string oursContent, string theirsContent,
        bool ignoreWhitespace = false);

    /// <summary>
    /// Perform a three-way merge with file path metadata.
    /// </summary>
    /// <param name="filePath">Path to the file being merged (for display purposes)</param>
    /// <param name="baseContent">The common ancestor content</param>
    /// <param name="oursContent">Content from the current branch</param>
    /// <param name="theirsContent">Content from the incoming branch</param>
    /// <param name="ignoreWhitespace">Whether to ignore whitespace differences</param>
    /// <returns>A FileMergeResult containing all merge regions</returns>
    FileMergeResult PerformMerge(string filePath, string baseContent, string oursContent,
        string theirsContent, bool ignoreWhitespace = false);
}
