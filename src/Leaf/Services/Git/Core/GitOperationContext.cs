namespace Leaf.Services.Git.Core;

/// <summary>
/// Centralizes shared dependencies passed to all Git operation classes.
/// Created once per GitService instance and shared by all operations.
/// </summary>
internal class GitOperationContext : IGitOperationContext
{
    public IGitCommandRunner CommandRunner { get; }
    public IGitOutputParser OutputParser { get; }
    public IGitErrorMapper ErrorMapper { get; }

    /// <summary>
    /// Forwarded directly from <see cref="IGitCommandRunner.CommandExecuted"/>
    /// — every git CLI invocation flows through the runner, so subscribing
    /// there gives this context (and ultimately <see cref="IGitService"/>)
    /// complete coverage without each operation class having to manually
    /// emit a notification after its own RunAsync call.
    /// </summary>
    public event EventHandler<GitCommandEventArgs>? GitCommandExecuted;

    public GitOperationContext(IGitCommandRunner commandRunner)
    {
        CommandRunner = commandRunner;
        OutputParser = new GitOutputParser();
        ErrorMapper = new GitErrorMapper();
        CommandRunner.CommandExecuted += OnRunnerCommandExecuted;
    }

    private void OnRunnerCommandExecuted(object? sender, GitCommandEventArgs e)
        => GitCommandExecuted?.Invoke(this, e);
}
