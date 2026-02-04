namespace Leaf.Services;

/// <summary>
/// Parses AI provider output to extract commit message and description.
/// </summary>
public interface ICommitMessageParser
{
    /// <summary>
    /// Parses the AI output to extract commit message and description.
    /// </summary>
    /// <param name="output">Raw output from AI provider</param>
    /// <returns>Tuple containing (message, description, error). Error is null on success.</returns>
    (string? message, string? description, string? error) Parse(string output);
}
