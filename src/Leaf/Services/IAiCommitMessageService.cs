namespace Leaf.Services;

/// <summary>
/// Service for generating commit messages using AI providers.
/// </summary>
public interface IAiCommitMessageService
{
    /// <summary>
    /// Generates a commit message and description from the staged diff text.
    /// </summary>
    /// <param name="diffText">The staged diff content</param>
    /// <param name="repoPath">Optional repository path for providers that need it</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple containing (message, description, error). Error is null on success.</returns>
    Task<(string? message, string? description, string? error)> GenerateCommitMessageAsync(
        string diffText,
        string? repoPath = null,
        CancellationToken cancellationToken = default);
}
