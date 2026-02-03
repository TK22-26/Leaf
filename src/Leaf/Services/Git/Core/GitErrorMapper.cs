namespace Leaf.Services.Git.Core;

/// <summary>
/// Maps git CLI errors to user-friendly messages.
/// Stateless - safe to share across operations.
/// </summary>
internal class GitErrorMapper : IGitErrorMapper
{
    /// <inheritdoc />
    public string MapError(string error, string operation)
    {
        if (string.IsNullOrWhiteSpace(error))
            return $"{operation} failed";

        // Authentication errors
        if (error.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase))
            return "Authentication failed. Check your credentials.";

        // Network errors
        if (error.Contains("Could not resolve host", StringComparison.OrdinalIgnoreCase))
            return "Could not connect to remote server. Check your network connection.";

        if (error.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
            return "Connection refused by remote server.";

        // Permission errors
        if (error.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            return "Permission denied. Check your access rights.";

        // Branch errors
        if (error.Contains("not a valid branch", StringComparison.OrdinalIgnoreCase))
            return "The specified branch does not exist.";

        // Merge state errors
        if (error.Contains("You are in the middle of a merge", StringComparison.OrdinalIgnoreCase))
            return "A merge is in progress. Complete or abort it first.";

        // Detached HEAD
        if (error.Contains("detached HEAD", StringComparison.OrdinalIgnoreCase))
            return "Cannot perform this operation in detached HEAD state.";

        return error.Trim();
    }

    /// <inheritdoc />
    public bool IsUnrelatedHistoriesError(string error)
    {
        return error.Contains("refusing to merge unrelated histories", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsConflictError(string output, string error)
    {
        return output.Contains("CONFLICT", StringComparison.Ordinal) ||
               error.Contains("CONFLICT", StringComparison.Ordinal) ||
               error.Contains("conflict", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsFastForwardNotPossible(string output, string error)
    {
        return error.Contains("Not possible to fast-forward", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Not possible to fast-forward", StringComparison.OrdinalIgnoreCase);
    }
}
