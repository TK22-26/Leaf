namespace Leaf.Services.Git.Core;

/// <summary>
/// Centralizes shared dependencies passed to all Git operation classes.
/// Created once per GitService instance and shared by all operations.
/// </summary>
internal class GitOperationContext : IGitOperationContext
{
    public IGitCommandRunner CommandRunner { get; }
    public GitCliHelpers CliHelpers { get; }
    public IGitOutputParser OutputParser { get; }
    public IGitErrorMapper ErrorMapper { get; }

    /// <summary>
    /// Event raised when a git command is executed for logging/debugging.
    /// </summary>
    public event EventHandler<GitCommandEventArgs>? GitCommandExecuted;

    public GitOperationContext(IGitCommandRunner commandRunner)
    {
        CommandRunner = commandRunner;
        CliHelpers = new GitCliHelpers(commandRunner);
        OutputParser = new GitOutputParser();
        ErrorMapper = new GitErrorMapper();
    }

    /// <summary>
    /// Raises the GitCommandExecuted event.
    /// </summary>
    internal void OnGitCommandExecuted(string workingDirectory, string arguments, int exitCode, string output, string error)
    {
        GitCommandExecuted?.Invoke(this, new GitCommandEventArgs(workingDirectory, arguments, exitCode, output, error));
    }
}
