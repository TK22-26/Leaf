namespace Leaf.Models;

/// <summary>
/// Represents the result of a merge operation.
/// </summary>
public class MergeResult
{
    /// <summary>
    /// Whether the merge completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the merge failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether there are unresolved merge conflicts.
    /// </summary>
    public bool HasConflicts { get; set; }

    /// <summary>
    /// List of files with merge conflicts.
    /// </summary>
    public List<string> ConflictingFiles { get; set; } = [];

    /// <summary>
    /// SHA of the new commit created by the merge (if successful).
    /// </summary>
    public string? CommitSha { get; set; }
}
