using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for staging/unstaging/reverting individual hunks.
/// </summary>
internal class HunkOperations
{
    private readonly IGitOperationContext _context;

    public HunkOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Revert a single hunk by applying a reverse patch to the working directory.
    /// </summary>
    public async Task RevertHunkAsync(string repoPath, string patchContent)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["apply", "--reverse"],
            patchContent);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to revert hunk: {result.StandardError}");
        }
    }

    /// <summary>
    /// Stage a single hunk by applying a patch to the index (staging area).
    /// </summary>
    public async Task StageHunkAsync(string repoPath, string patchContent)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["apply", "--cached"],
            patchContent);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to stage hunk: {result.StandardError}");
        }
    }

    /// <summary>
    /// Unstage a single hunk by applying a reverse patch to the index.
    /// </summary>
    public async Task UnstageHunkAsync(string repoPath, string patchContent)
    {
        var result = await _context.CommandRunner.RunAsync(
            repoPath,
            ["apply", "--cached", "--reverse"],
            patchContent);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to unstage hunk: {result.StandardError}");
        }
    }
}
