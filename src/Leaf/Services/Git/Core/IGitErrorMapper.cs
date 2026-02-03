namespace Leaf.Services.Git.Core;

/// <summary>
/// Interface for mapping git CLI errors to user-friendly messages.
/// </summary>
internal interface IGitErrorMapper
{
    /// <summary>
    /// Maps a git CLI error to a user-friendly error message.
    /// </summary>
    /// <param name="error">The raw error output from git.</param>
    /// <param name="operation">The operation being performed (e.g., "push", "merge").</param>
    /// <returns>A user-friendly error message.</returns>
    string MapError(string error, string operation);

    /// <summary>
    /// Checks if the error indicates unrelated histories.
    /// </summary>
    bool IsUnrelatedHistoriesError(string error);

    /// <summary>
    /// Checks if the error indicates merge conflicts.
    /// </summary>
    bool IsConflictError(string output, string error);

    /// <summary>
    /// Checks if the error indicates fast-forward is not possible.
    /// </summary>
    bool IsFastForwardNotPossible(string output, string error);
}
