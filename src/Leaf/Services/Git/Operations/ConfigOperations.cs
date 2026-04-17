using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for managing git configuration.
/// </summary>
internal class ConfigOperations
{
    private readonly IGitOperationContext _context;

    public ConfigOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Set a git config value. Scope defaults to Local (<c>.git/config</c>);
    /// pass <see cref="GitConfigScope.Global"/> to target <c>~/.gitconfig</c>.
    /// </summary>
    public async Task SetConfigAsync(string repoPath, string key, string value, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
    {
        var args = BuildConfigArgs(scope, [key, value]);
        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? $"Failed to set config '{key}'"
                : result.StandardError);
        }
    }

    /// <summary>
    /// Get a git config value. Scope defaults to Local (reads from
    /// <c>.git/config</c>, falling back to inherited global/system values);
    /// pass <see cref="GitConfigScope.Global"/> to read only
    /// <c>~/.gitconfig</c>.
    /// </summary>
    public async Task<string?> GetConfigAsync(string repoPath, string key, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
    {
        var args = BuildConfigArgs(scope, ["--get", key]);
        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        return result.Success ? result.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// Remove a git config value. Scope defaults to Local.
    /// </summary>
    public async Task UnsetConfigAsync(string repoPath, string key, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
    {
        var args = BuildConfigArgs(scope, ["--unset", key]);
        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        // --unset returns error if key doesn't exist, which is OK
        if (!result.Success && !result.StandardError.Contains("not exist"))
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    private static string[] BuildConfigArgs(GitConfigScope scope, IReadOnlyList<string> tail)
    {
        // Scope flag (if any) goes immediately after "config". Global
        // writes need --global explicitly; local is the default and
        // doesn't need a flag.
        var args = new List<string>(tail.Count + 2) { "config" };
        if (scope == GitConfigScope.Global)
        {
            args.Add("--global");
        }
        args.AddRange(tail);
        return args.ToArray();
    }
}
