using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for parsing diff results into hunks and generating patch content.
/// </summary>
public interface IHunkService
{
    /// <summary>
    /// Parse a diff result into discrete hunks based on proximity of changes.
    /// </summary>
    /// <param name="diffResult">The diff result to parse.</param>
    /// <param name="contextLines">Number of context lines to include around changes (default: 3).</param>
    /// <returns>List of hunks, each representing a contiguous block of changes with context.</returns>
    IReadOnlyList<DiffHunk> ParseHunks(FileDiffResult diffResult, int contextLines = 3);

    /// <summary>
    /// Generate a unified diff patch for a single hunk.
    /// </summary>
    /// <param name="filePath">The file path (relative to repo root).</param>
    /// <param name="hunk">The hunk to generate a patch for.</param>
    /// <returns>A string containing the unified diff patch.</returns>
    string GenerateHunkPatch(string filePath, DiffHunk hunk);

    /// <summary>
    /// Generate a reverse patch for a single hunk (for reverting changes).
    /// </summary>
    /// <param name="filePath">The file path (relative to repo root).</param>
    /// <param name="hunk">The hunk to generate a reverse patch for.</param>
    /// <returns>A string containing the reverse unified diff patch.</returns>
    string GenerateReversePatch(string filePath, DiffHunk hunk);
}
