namespace Leaf.Services;

/// <summary>
/// Result of a git command execution.
/// </summary>
/// <param name="ExitCode">Process exit code (0 = success).</param>
/// <param name="StandardOutput">Captured stdout.</param>
/// <param name="StandardError">Captured stderr.</param>
/// <param name="Success">True if ExitCode is 0.</param>
public record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool Success);

/// <summary>
/// Safe CLI execution interface using ProcessStartInfo.ArgumentList.
/// No string escaping needed - arguments are passed as-is.
/// </summary>
public interface IGitCommandRunner
{
    /// <summary>
    /// Runs a git command with the specified arguments.
    /// </summary>
    /// <param name="workingDirectory">Working directory for git command.</param>
    /// <param name="arguments">Argument tokens (NO escaping needed).</param>
    /// <param name="input">Optional input to write to stdin.</param>
    /// <param name="credentialKey">
    /// Optional credential storage key (e.g. "GitHub:microsoft"). When supplied,
    /// the git process is configured to use Leaf.AskPass.exe as GIT_ASKPASS so
    /// the PAT never enters the git URL or command line. When null, git falls
    /// back to its default credential helpers (Git Credential Manager).
    /// </param>
    /// <param name="cancellationToken">Cancellation token - will KILL git process if cancelled.</param>
    /// <returns>Command result with exit code, stdout, and stderr.</returns>
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? input = null,
        string? credentialKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a git command using a strongly-typed command object.
    /// </summary>
    /// <param name="workingDirectory">Working directory for git command.</param>
    /// <param name="command">Strongly-typed command object.</param>
    /// <param name="cancellationToken">Cancellation token - will KILL git process if cancelled.</param>
    /// <returns>Command result with exit code, stdout, and stderr.</returns>
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        GitCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for strongly-typed git commands.
/// Subclasses build argument lists safely without string escaping.
/// </summary>
public abstract class GitCommand
{
    /// <summary>
    /// Converts the command to an argument list.
    /// Arguments are NOT escaped - they're passed directly to ArgumentList.
    /// </summary>
    public abstract IReadOnlyList<string> ToArguments();
}
