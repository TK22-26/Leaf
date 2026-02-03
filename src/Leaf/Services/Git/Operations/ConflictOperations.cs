using System.Diagnostics;
using System.IO;
using Leaf.Models;
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
    public Task<List<string>> GetConflictFilesAsync(string repoPath)
    {
        return Task.Run(() => GitCliHelpers.GetConflictFiles(repoPath));
    }

    /// <inheritdoc />
    public Task<int> GetConflictCountAsync(string repoPath)
    {
        return Task.Run(() => GitCliHelpers.GetConflictCount(repoPath));
    }

    /// <summary>
    /// Get list of conflicting files with detailed information.
    /// </summary>
    public Task<List<ConflictInfo>> GetConflictsAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            Debug.WriteLine($"[GitService] GetConflictsAsync repo={repoPath}");
            var conflicts = new List<ConflictInfo>();
            var conflictPaths = new List<string>();

            // Use git diff to find unmerged files
            var result = GitCliHelpers.RunGit(repoPath, "diff --name-only --diff-filter=U");
            if (result.ExitCode == 0)
            {
                conflictPaths.AddRange(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
            Debug.WriteLine($"[GitService] diff --name-only --diff-filter=U => {conflictPaths.Count}");

            if (conflictPaths.Count == 0)
            {
                var statusResult = GitCliHelpers.RunGit(repoPath, "status --porcelain");
                if (statusResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(statusResult.Output))
                {
                    conflictPaths.AddRange(_context.OutputParser.ParseConflictFilesFromPorcelain(statusResult.Output));
                }
            }
            Debug.WriteLine($"[GitService] status --porcelain U => {conflictPaths.Count}");

            using var repo = new Repository(repoPath);

            if (conflictPaths.Count == 0)
            {
                conflictPaths.AddRange(repo.Index.Conflicts
                    .Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!));
            }
            Debug.WriteLine($"[GitService] index conflicts => {conflictPaths.Count}");

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
                    catch { /* Ignore - file might be new */ }
                }

                conflicts.Add(conflictInfo);
            }

            return conflicts;
        });
    }

    /// <summary>
    /// Resolve a conflict by using the current branch version (ours).
    /// </summary>
    public Task ResolveConflictWithOursAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            GitCliHelpers.RunGit(repoPath, $"checkout --ours \"{filePath}\"");
            Commands.Stage(repo, filePath);
        });
    }

    /// <summary>
    /// Resolve a conflict by using the incoming branch version (theirs).
    /// </summary>
    public Task ResolveConflictWithTheirsAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            GitCliHelpers.RunGit(repoPath, $"checkout --theirs \"{filePath}\"");
            Commands.Stage(repo, filePath);
        });
    }

    /// <summary>
    /// Mark a conflict as resolved (after manual edit).
    /// </summary>
    public Task MarkConflictResolvedAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            Commands.Stage(repo, filePath);
        });
    }

    /// <summary>
    /// Reopen a resolved conflict by restoring the conflict state.
    /// </summary>
    public Task ReopenConflictAsync(string repoPath, string filePath, string baseContent, string oursContent, string theirsContent)
    {
        return Task.Run(() =>
        {
            var baseResult = GitCliHelpers.RunGitWithInput(repoPath, "hash-object -w --stdin", baseContent ?? string.Empty);
            var oursResult = GitCliHelpers.RunGitWithInput(repoPath, "hash-object -w --stdin", oursContent ?? string.Empty);
            var theirsResult = GitCliHelpers.RunGitWithInput(repoPath, "hash-object -w --stdin", theirsContent ?? string.Empty);

            if (baseResult.ExitCode != 0 || oursResult.ExitCode != 0 || theirsResult.ExitCode != 0)
            {
                Debug.WriteLine($"[GitService] Failed to create conflict blobs: {baseResult.Error} {oursResult.Error} {theirsResult.Error}");
                return;
            }

            var baseSha = baseResult.Output.Trim();
            var oursSha = oursResult.Output.Trim();
            var theirsSha = theirsResult.Output.Trim();

            var indexInfo = $"100644 {baseSha} 1\t{filePath}\n" +
                            $"100644 {oursSha} 2\t{filePath}\n" +
                            $"100644 {theirsSha} 3\t{filePath}\n";

            var indexResult = GitCliHelpers.RunGitWithInput(repoPath, "update-index --index-info", indexInfo);
            if (indexResult.ExitCode != 0)
            {
                Debug.WriteLine($"[GitService] Failed to restore conflict index: {indexResult.Error}");
                return;
            }

            GitCliHelpers.RunGit(repoPath, $"checkout --conflict=merge \"{filePath}\"");
        });
    }

    /// <summary>
    /// Get files that have been resolved during a merge.
    /// </summary>
    public Task<List<ConflictInfo>> GetResolvedMergeFilesAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var unresolvedResult = GitCliHelpers.RunGit(repoPath, "diff --name-only --diff-filter=U");
            var unresolved = unresolvedResult.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var stagedResult = GitCliHelpers.RunGit(repoPath, "diff --name-only --cached");
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
        });
    }

    /// <summary>
    /// Open a conflict in VS Code for resolution.
    /// </summary>
    public async Task OpenConflictInVsCodeAsync(string repoPath, string filePath)
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

        await File.WriteAllTextAsync(basePath, conflict.BaseContent);
        await File.WriteAllTextAsync(localPath, conflict.OursContent);
        await File.WriteAllTextAsync(remotePath, conflict.TheirsContent);

        var repoFilePath = Path.Combine(repoPath, filePath);
        if (File.Exists(repoFilePath))
        {
            File.Copy(repoFilePath, mergedPath, true);
        }
        else
        {
            await File.WriteAllTextAsync(mergedPath, conflict.OursContent);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"-n --wait --merge \"{basePath}\" \"{localPath}\" \"{remotePath}\" \"{mergedPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to launch VS Code.");
            }

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                if (File.Exists(mergedPath))
                {
                    var mergedContent = await File.ReadAllTextAsync(mergedPath);
                    await File.WriteAllTextAsync(repoFilePath, mergedContent);

                    using var repo = new Repository(repoPath);
                    Commands.Stage(repo, filePath);
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    #region Storage for merge conflict files

    /// <summary>
    /// Get stored merge conflict files.
    /// </summary>
    public Task<List<string>> GetStoredMergeConflictFilesAsync(string repoPath)
    {
        return Task.Run(() => GetStoredMergeConflictFiles(repoPath));
    }

    /// <summary>
    /// Save merge conflict files to storage.
    /// </summary>
    public Task SaveStoredMergeConflictFilesAsync(string repoPath, IEnumerable<string> files)
    {
        return Task.Run(() => SaveStoredMergeConflictFiles(repoPath, files));
    }

    /// <summary>
    /// Clear stored merge conflict files.
    /// </summary>
    public Task ClearStoredMergeConflictFilesAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var path = GetStoredMergeConflictPath(repoPath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        });
    }

    private static string GetStoredMergeConflictPath(string repoPath)
    {
        using var repo = new Repository(repoPath);
        return Path.Combine(repo.Info.Path, "leaf-merge-conflicts.txt");
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
            Debug.WriteLine($"[GitService] Failed to read stored merge conflicts: {ex.Message}");
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
            Debug.WriteLine($"[GitService] Failed to store merge conflicts: {ex.Message}");
        }
    }

    private static List<string> GetMergeConflictFilesFromMessage(string repoPath)
    {
        try
        {
            using var repo = new Repository(repoPath);
            var mergeMessagePath = Path.Combine(repo.Info.Path, "MERGE_MSG");
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
            Debug.WriteLine($"[GitService] Failed to read MERGE_MSG: {ex.Message}");
            return [];
        }
    }

    #endregion
}
