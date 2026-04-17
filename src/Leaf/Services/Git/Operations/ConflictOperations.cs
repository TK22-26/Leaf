using System.IO;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Git.Core;
using Leaf.Services.Git.Interfaces;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for handling merge conflicts.
/// </summary>
internal class ConflictOperations : IConflictOperations
{
    private readonly IGitOperationContext _context;

    public ConflictOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public Task<List<string>> GetConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GitCliHelpers.GetConflictFiles(repoPath), cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> GetConflictCountAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GitCliHelpers.GetConflictCount(repoPath), cancellationToken);
    }

    /// <summary>
    /// Get list of conflicting files with detailed information.
    /// </summary>
    public Task<List<ConflictInfo>> GetConflictsAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Log.Info("Merge", $"GetConflictsAsync repo={Path.GetFileName(repoPath)}");
            var conflicts = new List<ConflictInfo>();
            var conflictPaths = new List<string>();

            // Use git diff to find unmerged files
            var result = GitCliHelpers.RunGitArgs(repoPath, "diff", "--name-only", "--diff-filter=U");
            if (result.ExitCode == 0)
            {
                conflictPaths.AddRange(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
            Log.Info("Merge", $"diff --name-only --diff-filter=U => {conflictPaths.Count}");

            if (conflictPaths.Count == 0)
            {
                var statusResult = GitCliHelpers.RunGitArgs(repoPath, "status", "--porcelain");
                if (statusResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(statusResult.Output))
                {
                    conflictPaths.AddRange(_context.OutputParser.ParseConflictFilesFromPorcelain(statusResult.Output));
                }
            }
            Log.Info("Merge", $"status --porcelain U => {conflictPaths.Count}");

            using var repo = new Repository(repoPath);

            if (conflictPaths.Count == 0)
            {
                conflictPaths.AddRange(repo.Index.Conflicts
                    .Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!));
            }
            Log.Info("Merge", $"index conflicts => {conflictPaths.Count}");

            foreach (var filePath in conflictPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var trimmedPath = filePath.Trim();
                if (string.IsNullOrEmpty(trimmedPath)) continue;

                var conflictInfo = new ConflictInfo
                {
                    FilePath = trimmedPath,
                    IsResolved = false
                };

                conflictInfo.BaseContent = GitCliHelpers.ReadConflictStage(repoPath, trimmedPath, 1);
                conflictInfo.OursContent = GitCliHelpers.ReadConflictStage(repoPath, trimmedPath, 2);
                conflictInfo.TheirsContent = GitCliHelpers.ReadConflictStage(repoPath, trimmedPath, 3);

                // Try to get content from LibGit2Sharp index conflicts
                var indexConflict = repo.Index.Conflicts[trimmedPath];
                if (indexConflict != null)
                {
                    if (indexConflict.Ours != null)
                    {
                        var blob = repo.Lookup<Blob>(indexConflict.Ours.Id);
                        var content = blob?.GetContentText();
                        if (!string.IsNullOrEmpty(content))
                        {
                            conflictInfo.OursContent = content;
                        }
                    }

                    if (indexConflict.Theirs != null)
                    {
                        var blob = repo.Lookup<Blob>(indexConflict.Theirs.Id);
                        var content = blob?.GetContentText();
                        if (!string.IsNullOrEmpty(content))
                        {
                            conflictInfo.TheirsContent = content;
                        }
                    }

                    if (indexConflict.Ancestor != null)
                    {
                        var blob = repo.Lookup<Blob>(indexConflict.Ancestor.Id);
                        var content = blob?.GetContentText();
                        if (!string.IsNullOrEmpty(content))
                        {
                            conflictInfo.BaseContent = content;
                        }
                    }
                }
                else
                {
                    // Fallback: read the file with conflict markers
                    var fullPath = Path.Combine(repoPath, trimmedPath);
                    if (File.Exists(fullPath))
                    {
                        conflictInfo.MergedContent = File.ReadAllText(fullPath);
                    }

                    // Try to get HEAD version as "ours"
                    try
                    {
                        var headCommit = repo.Head.Tip;
                        var treeEntry = headCommit?[trimmedPath];
                        if (treeEntry?.Target is Blob headBlob)
                        {
                            conflictInfo.OursContent = headBlob.GetContentText();
                        }
                    }
                    catch (Exception ex) { Log.Warn("Merge", $"Failed to read HEAD version: {ex.Message}"); }
                }

                conflicts.Add(conflictInfo);
            }

            return conflicts;
        }, cancellationToken);
    }

    /// <summary>
    /// Resolve a conflict by using the current branch version (ours).
    /// </summary>
    public Task ResolveConflictWithOursAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            GitCliHelpers.RunGitArgs(repoPath, "checkout", "--ours", filePath);
            Commands.Stage(repo, filePath);
        }, cancellationToken);
    }

    /// <summary>
    /// Resolve a conflict by using the incoming branch version (theirs).
    /// </summary>
    public Task ResolveConflictWithTheirsAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            GitCliHelpers.RunGitArgs(repoPath, "checkout", "--theirs", filePath);
            Commands.Stage(repo, filePath);
        }, cancellationToken);
    }

    /// <summary>
    /// Mark a conflict as resolved (after manual edit).
    /// </summary>
    public Task MarkConflictResolvedAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            Commands.Stage(repo, filePath);
        }, cancellationToken);
    }

    /// <summary>
    /// Reopen a resolved conflict by restoring the conflict state.
    /// </summary>
    public Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var baseResult = GitCliHelpers.RunGitWithInputArgs(repoPath, baseContent ?? string.Empty, "hash-object", "-w", "--stdin");
            var oursResult = GitCliHelpers.RunGitWithInputArgs(repoPath, oursContent ?? string.Empty, "hash-object", "-w", "--stdin");
            var theirsResult = GitCliHelpers.RunGitWithInputArgs(repoPath, theirsContent ?? string.Empty, "hash-object", "-w", "--stdin");

            if (baseResult.ExitCode != 0 || oursResult.ExitCode != 0 || theirsResult.ExitCode != 0)
            {
                Log.Error("Merge", $"ReopenConflict: failed to create blobs: {baseResult.Error} {oursResult.Error} {theirsResult.Error}");
                return;
            }

            var baseSha = baseResult.Output.Trim();
            var oursSha = oursResult.Output.Trim();
            var theirsSha = theirsResult.Output.Trim();

            var indexInfo = $"100644 {baseSha} 1\t{filePath}\n" +
                            $"100644 {oursSha} 2\t{filePath}\n" +
                            $"100644 {theirsSha} 3\t{filePath}\n";

            var indexResult = GitCliHelpers.RunGitWithInputArgs(repoPath, indexInfo, "update-index", "--index-info");
            if (indexResult.ExitCode != 0)
            {
                Log.Error("Merge", $"ReopenConflict: failed to restore index: {indexResult.Error}");
                return;
            }

            GitCliHelpers.RunGitArgs(repoPath, "checkout", "--conflict=merge", filePath);
        }, cancellationToken);
    }

    /// <summary>
    /// Get files that have been resolved during a merge.
    /// </summary>
    public Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var unresolvedResult = GitCliHelpers.RunGitArgs(repoPath, "diff", "--name-only", "--diff-filter=U");
            var unresolved = unresolvedResult.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var stagedResult = GitCliHelpers.RunGitArgs(repoPath, "diff", "--name-only", "--cached");
            var stagedFiles = stagedResult.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var conflictFilesFromMergeMsg = GetMergeConflictFilesFromMessage(repoPath);
            var storedFiles = GetStoredMergeConflictFiles(repoPath);
            var candidates = conflictFilesFromMergeMsg
                .Concat(storedFiles)
                .Concat(stagedFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(file => !unresolved.Contains(file));

            var resolvedFiles = new List<ConflictInfo>();
            foreach (var file in candidates)
            {
                var (baseContent, oursContent, theirsContent) = GitCliHelpers.GetMergeSideContents(repoPath, file);
                resolvedFiles.Add(new ConflictInfo
                {
                    FilePath = file,
                    BaseContent = baseContent,
                    OursContent = oursContent,
                    TheirsContent = theirsContent,
                    IsResolved = true
                });
            }

            return resolvedFiles;
        }, cancellationToken);
    }

    /// <summary>
    /// Drive a three-way merge through an external tool. The caller
    /// supplies a <paramref name="launch"/> delegate that knows how to
    /// invoke the tool and returns the tool's exit code — we handle the
    /// git-side concerns (extracting base/ours/theirs, writing temp files,
    /// copying the tool's output back, staging) so callers don't have to
    /// know about index stages or libgit2sharp. Returns true when the
    /// merge was accepted and staged; false when the tool reported
    /// failure or the user discarded the merge.
    /// </summary>
    public async Task<bool> OpenConflictInMergeToolAsync(
        string repoPath,
        string filePath,
        Func<string, string, string, string, CancellationToken, Task<int>> launch,
        CancellationToken cancellationToken = default)
    {
        var conflicts = await GetConflictsAsync(repoPath);
        var conflict = conflicts.FirstOrDefault(c => c.FilePath == filePath);

        if (conflict == null)
        {
            throw new InvalidOperationException($"Conflict for file '{filePath}' not found.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "LeafMerge", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);

        var basePath = Path.Combine(tempDir, $"{fileName}.base{extension}");
        var localPath = Path.Combine(tempDir, $"{fileName}.local{extension}");
        var remotePath = Path.Combine(tempDir, $"{fileName}.remote{extension}");
        var mergedPath = Path.Combine(tempDir, $"{fileName}{extension}");

        await File.WriteAllTextAsync(basePath, conflict.BaseContent, cancellationToken);
        await File.WriteAllTextAsync(localPath, conflict.OursContent, cancellationToken);
        await File.WriteAllTextAsync(remotePath, conflict.TheirsContent, cancellationToken);

        var repoFilePath = Path.Combine(repoPath, filePath);
        if (File.Exists(repoFilePath))
        {
            File.Copy(repoFilePath, mergedPath, true);
        }
        else
        {
            await File.WriteAllTextAsync(mergedPath, conflict.OursContent, cancellationToken);
        }

        try
        {
            var exitCode = await launch(basePath, localPath, remotePath, mergedPath, cancellationToken);
            if (exitCode != 0 || !File.Exists(mergedPath))
            {
                return false;
            }

            var mergedContent = await File.ReadAllTextAsync(mergedPath, cancellationToken);
            await File.WriteAllTextAsync(repoFilePath, mergedContent, cancellationToken);

            using var repo = new Repository(repoPath);
            Commands.Stage(repo, filePath);
            return true;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch (Exception ex) { Log.Warn("Merge", $"Failed to clean up temp directory: {ex.Message}"); }
        }
    }

    #region Storage for merge conflict files

    /// <summary>
    /// Get stored merge conflict files.
    /// </summary>
    public Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetStoredMergeConflictFiles(repoPath), cancellationToken);
    }

    /// <summary>
    /// Save merge conflict files to storage.
    /// </summary>
    public Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SaveStoredMergeConflictFiles(repoPath, files), cancellationToken);
    }

    /// <summary>
    /// Clear stored merge conflict files.
    /// </summary>
    public Task ClearStoredMergeConflictFilesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var path = GetStoredMergeConflictPath(repoPath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }, cancellationToken);
    }

    private static string GetStoredMergeConflictPath(string repoPath)
    {
        return Path.Combine(GetGitDirectoryPath(repoPath), "leaf-merge-conflicts.txt");
    }

    private static List<string> GetStoredMergeConflictFiles(string repoPath)
    {
        try
        {
            var path = GetStoredMergeConflictPath(repoPath);
            if (!File.Exists(path))
            {
                return [];
            }

            return File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Merge", $"Failed to read stored merge conflicts: {ex.Message}");
            return [];
        }
    }

    private static void SaveStoredMergeConflictFiles(string repoPath, IEnumerable<string> files)
    {
        try
        {
            var path = GetStoredMergeConflictPath(repoPath);
            var lines = files
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            File.WriteAllLines(path, lines);
        }
        catch (Exception ex)
        {
            Log.Error("Merge", $"Failed to store merge conflicts: {ex.Message}");
        }
    }

    private static List<string> GetMergeConflictFilesFromMessage(string repoPath)
    {
        try
        {
            var mergeMessagePath = Path.Combine(GetGitDirectoryPath(repoPath), "MERGE_MSG");
            if (!File.Exists(mergeMessagePath))
            {
                return [];
            }

            var lines = File.ReadAllLines(mergeMessagePath);
            var results = new List<string>();
            var inConflicts = false;

            foreach (var line in lines)
            {
                if (!inConflicts)
                {
                    if (line.StartsWith("Conflicts:", StringComparison.OrdinalIgnoreCase))
                    {
                        inConflicts = true;
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                results.Add(trimmed);
            }

            return results;
        }
        catch (Exception ex)
        {
            Log.Error("Merge", $"Failed to read MERGE_MSG: {ex.Message}");
            return [];
        }
    }

    private static string GetGitDirectoryPath(string repoPath)
    {
        var gitPath = Path.Combine(repoPath, ".git");
        if (Directory.Exists(gitPath))
        {
            return gitPath;
        }

        if (!File.Exists(gitPath))
        {
            return gitPath;
        }

        try
        {
            var firstLine = File.ReadLines(gitPath).FirstOrDefault()?.Trim();
            const string prefix = "gitdir:";
            if (!string.IsNullOrWhiteSpace(firstLine) &&
                firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var gitDir = firstLine[prefix.Length..].Trim();
                if (!string.IsNullOrEmpty(gitDir))
                {
                    return Path.GetFullPath(
                        Path.IsPathRooted(gitDir) ? gitDir : Path.Combine(repoPath, gitDir));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Merge", $"Failed to resolve git dir from {gitPath}: {ex.Message}");
        }

        return gitPath;
    }

    #endregion
}
