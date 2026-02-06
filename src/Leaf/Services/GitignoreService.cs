using System.IO;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing .gitignore entries and untracking files.
/// </summary>
public class GitignoreService : IGitignoreService
{
    private readonly IGitService _gitService;

    public GitignoreService(IGitService gitService)
    {
        _gitService = gitService;
    }

    /// <inheritdoc/>
    public async Task IgnoreFileAsync(string repoPath, FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(repoPath) || file == null)
            return;

        var normalizedPath = NormalizePath(file.Path);
        await AddToGitignoreAsync(repoPath, normalizedPath);
        await UntrackIfTrackedAsync(repoPath, file);
    }

    /// <inheritdoc/>
    public async Task IgnoreExtensionAsync(string repoPath, FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(repoPath) || file == null || string.IsNullOrEmpty(file.Extension))
            return;

        await AddToGitignoreAsync(repoPath, $"*{file.Extension}");
        await UntrackIfTrackedAsync(repoPath, file);
    }

    /// <inheritdoc/>
    public async Task IgnoreDirectoryAsync(string repoPath, FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(repoPath) || file == null || string.IsNullOrEmpty(file.Directory))
            return;

        var normalizedDir = NormalizePath(file.Directory);
        // Add trailing slash for directory pattern
        await AddToGitignoreAsync(repoPath, $"{normalizedDir}/");
        await UntrackIfTrackedAsync(repoPath, file);
    }

    /// <inheritdoc/>
    public async Task IgnoreDirectoryPathAsync(string repoPath, string directoryPath, IEnumerable<FileStatusInfo> trackedFiles)
    {
        if (string.IsNullOrEmpty(repoPath) || string.IsNullOrEmpty(directoryPath))
            return;

        var normalizedDir = NormalizePath(directoryPath);
        await AddToGitignoreAsync(repoPath, $"{normalizedDir}/");

        foreach (var file in trackedFiles)
        {
            if (file.Status != FileChangeStatus.Untracked)
            {
                await _gitService.UntrackFileAsync(repoPath, file.Path);
            }
        }
    }

    /// <summary>
    /// Normalizes path separators from backslash to forward slash for .gitignore compatibility.
    /// </summary>
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    /// Adds a pattern to the repository's .gitignore file.
    /// </summary>
    private static async Task AddToGitignoreAsync(string repoPath, string pattern)
    {
        var gitignorePath = Path.Combine(repoPath, ".gitignore");

        await Task.Run(() =>
        {
            var lines = File.Exists(gitignorePath)
                ? File.ReadAllLines(gitignorePath).ToList()
                : new List<string>();

            // Check if pattern already exists
            if (lines.Any(l => l.Trim().Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                return;

            // Add blank line if file doesn't end with one
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");

            lines.Add(pattern);
            File.WriteAllLines(gitignorePath, lines);
        });
    }

    /// <summary>
    /// Untracks the file if it's currently tracked by git.
    /// </summary>
    private async Task UntrackIfTrackedAsync(string repoPath, FileStatusInfo file)
    {
        if (file.Status == FileChangeStatus.Untracked)
            return;

        await _gitService.UntrackFileAsync(repoPath, file.Path);
    }
}
