using System;
using System.Threading;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - file watcher refresh handling.
/// </summary>
public partial class MainViewModel
{
    private async Task HandleWorkingDirectoryChangedAsync()
    {
        var sw = Log.StartTimer();
        Log.Perf("FileWatcher", "WorkingDirectoryChanged fired");

        try
        {
            GitGraphViewModel? graphViewModel = null;
            string? repoPath = null;
            Task refreshTask = Task.CompletedTask;

            await _dispatcherService.InvokeAsync(() =>
            {
                graphViewModel = GitGraphViewModel;
                repoPath = SelectedRepository?.Path;

                if (graphViewModel != null && !string.IsNullOrEmpty(repoPath))
                {
                    refreshTask = graphViewModel.RefreshWorkingChangesAsync();
                }
            });

            await refreshTask.ConfigureAwait(false);

            if (graphViewModel == null || string.IsNullOrEmpty(repoPath))
            {
                return;
            }

            await _dispatcherService.InvokeAsync(() =>
            {
                if (!IsCurrentGraphContext(graphViewModel, repoPath))
                {
                    return;
                }

                SyncWorkingChangesUi(repoPath, graphViewModel.WorkingChanges);
            });
        }
        catch (Exception ex)
        {
            Log.Error("FileWatcher", "WorkingDirectoryChanged failed", ex);
        }
        finally
        {
            Log.Perf("FileWatcher", "WorkingDirectoryChanged complete", sw.ElapsedMilliseconds);
        }
    }

    private async Task HandleGitDirectoryChangedAsync()
    {
        if (Interlocked.Exchange(ref _isGitDirectoryChangeRunning, 1) == 1)
        {
            Log.Info("FileWatcher", "GitDirectoryChanged skipped (already running)");
            return;
        }

        var sw = Log.StartTimer();
        Log.Perf("FileWatcher", "GitDirectoryChanged fired");

        try
        {
            GitGraphViewModel? graphViewModel = null;
            string? repoPath = null;
            Task graphTask = Task.CompletedTask;

            await _dispatcherService.InvokeAsync(() =>
            {
                graphViewModel = GitGraphViewModel;
                repoPath = SelectedRepository?.Path;

                if (graphViewModel != null && !string.IsNullOrEmpty(repoPath))
                {
                    graphTask = graphViewModel.LoadRepositoryAsync(repoPath);
                }
            });

            if (graphViewModel == null || string.IsNullOrEmpty(repoPath))
            {
                return;
            }

            var infoTask = _gitService.GetRepositoryInfoFastAsync(repoPath);
            await Task.WhenAll(graphTask, infoTask).ConfigureAwait(false);
            Log.Perf("FileWatcher", "GitDir: graph + info parallel", sw.ElapsedMilliseconds);

            var info = await infoTask.ConfigureAwait(false);
            var shouldRefreshMergeUi = false;

            await _dispatcherService.InvokeAsync(() =>
            {
                if (!IsCurrentGraphContext(graphViewModel, repoPath))
                {
                    return;
                }

                ApplyRepositoryInfo(SelectedRepository!, info);
                SyncWorkingChangesUi(repoPath, graphViewModel.WorkingChanges);

                shouldRefreshMergeUi = info.IsMergeInProgress
                    || info.ConflictCount > 0
                    || MergeConflictResolutionViewModel != null;
            });

            if (shouldRefreshMergeUi)
            {
                var mergeSw = Log.StartTimer();
                Task mergeTask = Task.CompletedTask;

                await _dispatcherService.InvokeAsync(() =>
                {
                    if (!IsCurrentGraphContext(graphViewModel, repoPath))
                    {
                        return;
                    }

                    mergeTask = RefreshMergeConflictResolutionAsync();
                });

                await mergeTask.ConfigureAwait(false);
                Log.Perf("FileWatcher", "GitDir: RefreshMergeConflictResolutionAsync", mergeSw.ElapsedMilliseconds);
            }
            else
            {
                Log.Info("FileWatcher", "GitDir: skipped merge refresh (no merge/conflicts UI)");
            }
        }
        catch (Exception ex)
        {
            Log.Error("FileWatcher", "GitDirectoryChanged failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isGitDirectoryChangeRunning, 0);
            Log.Perf("FileWatcher", "GitDirectoryChanged complete", sw.ElapsedMilliseconds);
        }
    }

    private bool IsCurrentGraphContext(GitGraphViewModel? graphViewModel, string repoPath)
    {
        return graphViewModel != null
            && ReferenceEquals(graphViewModel, GitGraphViewModel)
            && SelectedRepository != null
            && string.Equals(SelectedRepository.Path, repoPath, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyRepositoryInfo(RepositoryInfo repository, RepositoryInfo info)
    {
        repository.CurrentBranch = info.CurrentBranch;
        repository.IsDirty = info.IsDirty;
        repository.AheadBy = info.AheadBy;
        repository.BehindBy = info.BehindBy;
        repository.IsMergeInProgress = info.IsMergeInProgress;
        repository.OperationType = info.OperationType;
        repository.MergingBranch = info.MergingBranch;
        repository.ConflictCount = info.ConflictCount;
        repository.IsDetachedHead = info.IsDetachedHead;
        repository.DetachedHeadSha = info.DetachedHeadSha;
    }

    private void SyncWorkingChangesUi(string repoPath, WorkingChangesInfo? workingChanges)
    {
        if (WorkingChangesViewModel != null && IsWorkingChangesSelected)
        {
            WorkingChangesViewModel.SetWorkingChanges(repoPath, workingChanges);
        }

        if (!IsDiffViewerVisible || !IsWorkingChangesSelected || DiffViewerViewModel == null)
        {
            return;
        }

        var viewedPath = DiffViewerViewModel.FilePath?.Replace('\\', '/');
        if (string.IsNullOrEmpty(viewedPath))
        {
            return;
        }

        var stillPresent = workingChanges != null && (
            workingChanges.StagedFiles.Any(f => string.Equals(f.Path, viewedPath, StringComparison.OrdinalIgnoreCase)) ||
            workingChanges.UnstagedFiles.Any(f => string.Equals(f.Path, viewedPath, StringComparison.OrdinalIgnoreCase)));

        if (!stillPresent)
        {
            CloseDiffViewer();
        }
    }
}
