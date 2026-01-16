using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for computing textual differences between file versions.
/// </summary>
public interface IDiffService
{
    /// <summary>
    /// Compute the diff between old and new content.
    /// </summary>
    /// <param name="oldContent">The original file content.</param>
    /// <param name="newContent">The modified file content.</param>
    /// <param name="fileName">The file name (used for display).</param>
    /// <param name="filePath">The full file path.</param>
    /// <returns>A FileDiffResult containing the diff information.</returns>
    FileDiffResult ComputeDiff(string oldContent, string newContent, string fileName, string filePath);
}
