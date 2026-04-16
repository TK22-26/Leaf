using System.IO;
using System.Text;
using Leaf.Models;
using Leaf.Services.Git.Core;
using Leaf.Services.Git.Interfaces;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for staging and unstaging files.
/// </summary>
internal class StagingOperations : IStagingOperations
{
    public StagingOperations(IGitOperationContext context)
    {
    }

    /// <summary>
    /// Get working directory changes (staged and unstaged files).
    /// </summary>
    /// <remarks>
    /// LibGit2Sharp's <c>RetrieveStatus</c> supports rename detection, which
    /// we prefer for UX, but it can effectively hang on repos with many
    /// renames (O(n²) matching). We race it against a timeout and fall back
    /// to git CLI if it doesn't finish within 5 seconds.
    ///
    /// Plan §2.4: the previous implementation used
    /// <c>libgitTask.Wait(TimeSpan.FromSeconds(5))</c> inside an outer
    /// <c>Task.Run</c>, which blocked a thread-pool thread for up to five
    /// seconds per call. Under load that starves the pool and cascades into
    /// all other async git operations. <see cref="Task.WhenAny(Task[])"/>
    /// with a <see cref="Task.Delay(TimeSpan)"/> sentinel gives the same
    /// timeout semantics without blocking any thread — Task.Delay is
    /// timer-driven, not thread-bound.
    /// </remarks>
    public async Task<WorkingChangesInfo> GetWorkingChangesAsync(string repoPath)
    {
        var libgitTask = Task.Run(() => GetWorkingChangesViaLibGit2(repoPath));

        // The timeout CTS is cancelled as soon as we're done with it so the
        // underlying timer is released promptly even when libgit wins the
        // race — otherwise each abandoned Task.Delay holds a timer slot
        // until its 5 s elapse, which accumulates under high call frequency.
        using var timeoutCts = new CancellationTokenSource();
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), timeoutCts.Token);

        var completed = await Task.WhenAny(libgitTask, timeoutTask).ConfigureAwait(false);
        if (completed == libgitTask)
        {
            // Cancel the timer first so the `using` disposal doesn't have to
            // wait on a slow TimerQueue unregister.
            timeoutCts.Cancel();

            // Await instead of .Result so a LibGit2 exception propagates as
            // itself rather than wrapped in AggregateException.
            return await libgitTask.ConfigureAwait(false);
        }

        Log.Warn("Staging", $"LibGit2Sharp status timed out for '{repoPath}', falling back to git CLI");

        // The LibGit2 task keeps running in the background until it finishes
        // (LibGit2Sharp RetrieveStatus doesn't honour cancellation). We
        // discard its eventual result; the CLI fallback runs in parallel
        // and is what we hand back to the caller.
        return await Task.Run(() => GetWorkingChangesViaGitCli(repoPath)).ConfigureAwait(false);
    }

    /// <summary>
    /// Get working changes using LibGit2Sharp (supports rename detection).
    /// </summary>
    private static WorkingChangesInfo GetWorkingChangesViaLibGit2(string repoPath)
    {
        using var repo = new Repository(repoPath);

        if (repo.Info.IsBare)
            return new WorkingChangesInfo();

        var status = repo.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
            DetectRenamesInIndex = true,
            DetectRenamesInWorkDir = true
        });

        var isDetached = repo.Info.IsHeadDetached;
        var headSha = repo.Head?.Tip?.Sha;

        var workingChanges = new WorkingChangesInfo
        {
            BranchName = isDetached
                ? $"HEAD detached at {headSha?[..7] ?? "unknown"}"
                : (repo.Head?.FriendlyName ?? "HEAD"),
            IsDetachedHead = isDetached,
            DetachedHeadSha = isDetached ? headSha : null
        };

        foreach (var entry in status)
        {
            var fileStatus = entry.State;

            // Check for staged changes
            if (fileStatus.HasFlag(FileStatus.NewInIndex) ||
                fileStatus.HasFlag(FileStatus.ModifiedInIndex) ||
                fileStatus.HasFlag(FileStatus.DeletedFromIndex) ||
                fileStatus.HasFlag(FileStatus.RenamedInIndex) ||
                fileStatus.HasFlag(FileStatus.TypeChangeInIndex))
            {
                workingChanges.StagedFiles.Add(new FileStatusInfo
                {
                    Path = entry.FilePath,
                    OldPath = entry.HeadToIndexRenameDetails?.OldFilePath,
                    Status = MapFileStatus(fileStatus, staged: true),
                    IsStaged = true
                });
            }

            // Check for unstaged changes (working directory)
            if (fileStatus.HasFlag(FileStatus.ModifiedInWorkdir) ||
                fileStatus.HasFlag(FileStatus.DeletedFromWorkdir) ||
                fileStatus.HasFlag(FileStatus.TypeChangeInWorkdir) ||
                fileStatus.HasFlag(FileStatus.RenamedInWorkdir))
            {
                workingChanges.UnstagedFiles.Add(new FileStatusInfo
                {
                    Path = entry.FilePath,
                    OldPath = entry.IndexToWorkDirRenameDetails?.OldFilePath,
                    Status = MapFileStatus(fileStatus, staged: false),
                    IsStaged = false
                });
            }

            // Untracked files go to unstaged
            if (fileStatus.HasFlag(FileStatus.NewInWorkdir))
            {
                workingChanges.UnstagedFiles.Add(new FileStatusInfo
                {
                    Path = entry.FilePath,
                    Status = FileChangeStatus.Untracked,
                    IsStaged = false
                });
            }
        }

        return workingChanges;
    }

    /// <summary>
    /// Fast fallback: get working changes using git CLI (no rename detection, but always fast).
    /// </summary>
    private static WorkingChangesInfo GetWorkingChangesViaGitCli(string repoPath)
    {
        // Get branch info
        var headResult = GitCliHelpers.RunGit(repoPath, "symbolic-ref --short HEAD");
        bool isDetached = headResult.ExitCode != 0;
        string? headSha = null;
        string branchName;

        if (isDetached)
        {
            var shaResult = GitCliHelpers.RunGit(repoPath, "rev-parse HEAD");
            headSha = shaResult.ExitCode == 0 ? shaResult.Output.Trim() : null;
            branchName = $"HEAD detached at {headSha?[..7] ?? "unknown"}";
        }
        else
        {
            branchName = headResult.Output.Trim();
        }

        var workingChanges = new WorkingChangesInfo
        {
            BranchName = branchName,
            IsDetachedHead = isDetached,
            DetachedHeadSha = isDetached ? headSha : null
        };

        // Parse porcelain status output
        var statusResult = GitCliHelpers.RunGit(repoPath, "status --porcelain -uall");
        if (statusResult.ExitCode != 0)
            return workingChanges;

        foreach (var line in statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;

            var indexStatus = line[0];
            var workdirStatus = line[1];
            var filePath = line[3..].Trim();

            // Handle renames (format: "R  new -> old" or similar)
            string? oldPath = null;
            if (filePath.Contains(" -> "))
            {
                var parts = filePath.Split(" -> ", 2);
                oldPath = parts[0].Trim();
                filePath = parts[1].Trim();
            }

            // Staged changes (index column)
            if (indexStatus != ' ' && indexStatus != '?')
            {
                workingChanges.StagedFiles.Add(new FileStatusInfo
                {
                    Path = filePath,
                    OldPath = oldPath,
                    Status = MapPorcelainStatus(indexStatus),
                    IsStaged = true
                });
            }

            // Unstaged changes (workdir column)
            if (workdirStatus != ' ' && indexStatus != '?')
            {
                workingChanges.UnstagedFiles.Add(new FileStatusInfo
                {
                    Path = filePath,
                    Status = MapPorcelainStatus(workdirStatus),
                    IsStaged = false
                });
            }

            // Untracked files
            if (indexStatus == '?' && workdirStatus == '?')
            {
                workingChanges.UnstagedFiles.Add(new FileStatusInfo
                {
                    Path = filePath,
                    Status = FileChangeStatus.Untracked,
                    IsStaged = false
                });
            }
        }

        return workingChanges;
    }

    private static FileChangeStatus MapPorcelainStatus(char status) => status switch
    {
        'A' => FileChangeStatus.Added,
        'M' => FileChangeStatus.Modified,
        'D' => FileChangeStatus.Deleted,
        'R' => FileChangeStatus.Renamed,
        'C' => FileChangeStatus.Added,     // Copied — treat as added
        'T' => FileChangeStatus.TypeChanged,
        _ => FileChangeStatus.Modified
    };

    /// <summary>
    /// Get the combined diff of staged and unstaged changes.
    /// </summary>
    public Task<string> GetWorkingChangesPatchAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            var staged = GitCliHelpers.RunGit(repoPath, "diff --cached");
            var unstaged = GitCliHelpers.RunGit(repoPath, "diff");

            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(staged.Output))
            {
                builder.AppendLine("# Staged changes");
                builder.AppendLine(staged.Output.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(unstaged.Output))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("# Unstaged changes");
                builder.AppendLine(unstaged.Output.TrimEnd());
            }

            return builder.ToString();
        });
    }

    /// <summary>
    /// Get a compact summary of staged changes including diff content.
    /// </summary>
    public Task<string> GetStagedSummaryAsync(string repoPath, int maxFiles = 100, int maxDiffChars = 50000)
    {
        return Task.Run(() =>
        {
            var status = GitCliHelpers.RunGit(repoPath, "status -sb");
            var stat = GitCliHelpers.RunGit(repoPath, "diff --cached --stat");
            var names = GitCliHelpers.RunGit(repoPath, "diff --cached --name-only");
            var diff = GitCliHelpers.RunGit(repoPath, "diff --cached");

            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(status.Output))
            {
                builder.AppendLine("Status:");
                builder.AppendLine(status.Output.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stat.Output))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.AppendLine("Staged diff stats:");
                builder.AppendLine(stat.Output.TrimEnd());
            }

            var files = names.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .Take(Math.Max(1, maxFiles))
                .ToList();

            if (files.Count > 0)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.AppendLine("Staged files:");
                foreach (var file in files)
                {
                    builder.AppendLine($"- {file}");
                }
            }

            // Include actual diff content (truncated if too large)
            if (!string.IsNullOrWhiteSpace(diff.Output))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }
                builder.AppendLine("Staged diff:");
                var diffContent = diff.Output.TrimEnd();
                if (diffContent.Length > maxDiffChars)
                {
                    builder.AppendLine(diffContent[..maxDiffChars]);
                    builder.AppendLine($"... (truncated, {diffContent.Length - maxDiffChars} more characters)");
                }
                else
                {
                    builder.AppendLine(diffContent);
                }
            }

            return builder.ToString().TrimEnd();
        });
    }

    /// <inheritdoc />
    public Task StageFileAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            Commands.Stage(repo, filePath);
        });
    }

    /// <inheritdoc />
    public Task UnstageFileAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            Commands.Unstage(repo, filePath);
        });
    }

    /// <summary>
    /// Remove a tracked file from the index (git rm --cached) without deleting it from disk.
    /// </summary>
    public Task UntrackFileAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            if (repo.Index[filePath] == null)
                return;

            repo.Index.Remove(filePath);
            repo.Index.Write();
        });
    }

    /// <inheritdoc />
    public Task StageAllAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            Commands.Stage(repo, "*");
        });
    }

    /// <summary>
    /// Unstage all files (remove all from staging area).
    /// </summary>
    public Task UnstageAllAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            repo.Reset(ResetMode.Mixed);
        });
    }

    /// <summary>
    /// Discard all working directory changes (destructive - cannot be undone).
    /// </summary>
    public Task DiscardAllChangesAsync(string repoPath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);
            repo.Reset(ResetMode.Hard);
            repo.RemoveUntrackedFiles();
        });
    }

    /// <summary>
    /// Discard changes to a single file.
    /// </summary>
    public Task DiscardFileChangesAsync(string repoPath, string filePath)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repoPath);

            if (repo.Info.IsBare)
                return;

            var status = repo.RetrieveStatus(filePath);

            if (status == FileStatus.NewInWorkdir)
            {
                // Untracked file - delete it
                var fullPath = Path.Combine(repoPath, filePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            else
            {
                // Modified/deleted file - checkout from HEAD
                repo.CheckoutPaths("HEAD", new[] { filePath }, new CheckoutOptions
                {
                    CheckoutModifiers = CheckoutModifiers.Force
                });
            }
        });
    }

    private static FileChangeStatus MapFileStatus(FileStatus status, bool staged)
    {
        if (staged)
        {
            if (status.HasFlag(FileStatus.NewInIndex)) return FileChangeStatus.Added;
            if (status.HasFlag(FileStatus.ModifiedInIndex)) return FileChangeStatus.Modified;
            if (status.HasFlag(FileStatus.DeletedFromIndex)) return FileChangeStatus.Deleted;
            if (status.HasFlag(FileStatus.RenamedInIndex)) return FileChangeStatus.Renamed;
            if (status.HasFlag(FileStatus.TypeChangeInIndex)) return FileChangeStatus.TypeChanged;
        }
        else
        {
            if (status.HasFlag(FileStatus.NewInWorkdir)) return FileChangeStatus.Untracked;
            if (status.HasFlag(FileStatus.ModifiedInWorkdir)) return FileChangeStatus.Modified;
            if (status.HasFlag(FileStatus.DeletedFromWorkdir)) return FileChangeStatus.Deleted;
            if (status.HasFlag(FileStatus.RenamedInWorkdir)) return FileChangeStatus.Renamed;
            if (status.HasFlag(FileStatus.TypeChangeInWorkdir)) return FileChangeStatus.TypeChanged;
        }
        return FileChangeStatus.Modified;
    }
}
