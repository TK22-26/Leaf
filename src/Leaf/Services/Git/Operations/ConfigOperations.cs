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
    /// Set a git config value.
    /// </summary>
    public async Task SetConfigAsync(string repoPath, string key, string value)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["config", key, value]);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(result.StandardError)
                ? $"Failed to set config '{key}'"
                : result.StandardError);
        }
    }

    /// <summary>
    /// Get a git config value.
    /// </summary>
    public async Task<string?> GetConfigAsync(string repoPath, string key)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["config", "--get", key]);
        return result.Success ? result.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// Remove a git config value.
    /// </summary>
    public async Task UnsetConfigAsync(string repoPath, string key)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, ["config", "--unset", key]);
        // --unset returns error if key doesn't exist, which is OK
        if (!result.Success && !result.StandardError.Contains("not exist"))
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }
}
