using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - file watcher refresh handling.
/// </summary>
public partial class MainViewModel
{
    private async Task HandleWorkingDirectoryChangedAsync(IReadOnlyCollection<string> changedPaths)
    {
        var sw = Log.StartTimer();
        Log.Perf("FileWatcher", "WorkingDirectoryChanged fired");

        try
        {
            GitGraphViewModel? graphViewModel = null;
            RepositoryInfo? repository = null;
            string? repoPath = null;
            Task refreshTask = Task.CompletedTask;

            await _dispatcherService.InvokeAsync(() =>
            {
                graphViewModel = GitGraphViewModel;
                repository = SelectedRepository;
                repoPath = repository?.Path;

                if (graphViewModel != null && !string.IsNullOrEmpty(repoPath))
                {
                    refreshTask = graphViewModel.RefreshWorkingChangesAsync();
                }
            });

            // Dispatch helper: any changed path that lives inside a
            // submodule's working tree triggers a single-submodule
            // dirtiness re-probe (~10 ms each, parallel). Runs alongside
            // the working-changes refresh so we don't double the latency.
            var submoduleTask = repository != null && !string.IsNullOrEmpty(repoPath)
                ? RefreshSubmoduleDirtinessForChangedPathsAsync(repository, repoPath, changedPaths)
                : Task.CompletedTask;

            await Task.WhenAll(refreshTask, submoduleTask).ConfigureAwait(false);

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

    /// <summary>
    /// For each changed path that lives inside one of the repository's
    /// submodule working trees, re-probe that submodule's
    /// <see cref="SubmoduleInfo.HasWorkingTreeChanges"/> flag. Routes by
    /// longest-path match so a file inside a nested submodule is
    /// attributed to the innermost one. Submodules with no path matches
    /// are skipped — that's the surgical part: an AI edit to one
    /// submodule doesn't pay status-cost for the other nine.
    /// </summary>
    /// <remarks>
    /// On a wholesale signal (empty <paramref name="changedPaths"/> after
    /// a watcher overflow / restart) we re-probe every initialized
    /// submodule. Cost is bounded — ~30–60 ms parallel for ~10 entries.
    /// </remarks>
    private async Task RefreshSubmoduleDirtinessForChangedPathsAsync(
        RepositoryInfo repository,
        string repoPath,
        IReadOnlyCollection<string> changedPaths)
    {
        var submodules = CollectSubmodules(repository);
        if (submodules.Count == 0) return;

        var targets = changedPaths.Count == 0
            ? submodules // wholesale: refresh every initialized submodule
            : ResolveAffectedSubmodules(repoPath, submodules, changedPaths);

        if (targets.Count == 0) return;

        var ct = CurrentRepositoryToken;
        await Task.WhenAll(targets.Select(async submodule =>
        {
            try
            {
                var dirty = await _gitService.GetSubmoduleWorkingTreeDirtyAsync(repoPath, submodule.Path, ct);
                if (submodule.HasWorkingTreeChanges == dirty) return;
                // Re-check repo identity inside the dispatcher invoke
                // before mutating: the git call can take ~10-50 ms, and
                // the user may have switched repos during that window.
                // Without this, we'd write a stale dirty state onto a
                // SubmoduleInfo whose parent is no longer the active
                // repo — invisible until the user navigates back. Mirrors
                // the IsCurrentGraphContext pattern used by the working-
                // changes refresh path above.
                await _dispatcherService.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(SelectedRepository, repository)) return;
                    submodule.HasWorkingTreeChanges = dirty;
                });
            }
            catch (OperationCanceledException) { /* repo switch */ }
            catch (Exception ex)
            {
                Log.Warn("Submodule", $"Dirtiness refresh failed for {submodule.Path}: {ex.Message}");
            }
        })).ConfigureAwait(false);
    }

    /// <summary>
    /// All initialized submodules under <paramref name="repository"/>'s
    /// SUBMODULES sidebar category, snapshotted to a list so the caller
    /// can iterate without holding the UI thread.
    /// </summary>
    private static List<SubmoduleInfo> CollectSubmodules(RepositoryInfo repository)
    {
        var list = new List<SubmoduleInfo>();
        foreach (var category in repository.BranchCategories)
        {
            if (!category.IsSubmodulesCategory) continue;
            foreach (var s in category.Submodules)
            {
                if (s.IsInitialized) list.Add(s);
            }
        }
        return list;
    }

    /// <summary>
    /// Map each absolute changed path to the innermost submodule whose
    /// working tree contains it. Longest-prefix-wins so a file inside a
    /// nested submodule is attributed to that nested one rather than its
    /// outer parent. Returns the deduplicated set of affected submodules.
    /// </summary>
    private static List<SubmoduleInfo> ResolveAffectedSubmodules(
        string repoPath,
        List<SubmoduleInfo> submodules,
        IReadOnlyCollection<string> changedPaths)
    {
        // Pre-resolve each submodule's absolute working-tree root once
        // so the inner loop is a string-prefix check per path.
        var roots = submodules
            .Select(s => (Submodule: s, Root: NormalizeWithSeparator(Path.GetFullPath(Path.Combine(repoPath, s.Path)))))
            .OrderByDescending(t => t.Root.Length) // longest first → nested wins
            .ToList();

        var affected = new HashSet<SubmoduleInfo>();
        foreach (var changed in changedPaths)
        {
            string normalized;
            try { normalized = Path.GetFullPath(changed); }
            catch { continue; }

            foreach (var (submodule, root) in roots)
            {
                if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    affected.Add(submodule);
                    break; // longest-first means this is the innermost
                }
            }
        }
        return [.. affected];
    }

    private static string NormalizeWithSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

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
            RepositoryInfo? repository = null;
            string? repoPath = null;
            Task graphTask = Task.CompletedTask;
            Task branchTask = Task.CompletedTask;

            await _dispatcherService.InvokeAsync(() =>
            {
                graphViewModel = GitGraphViewModel;
                repository = SelectedRepository;
                repoPath = repository?.Path;

                if (graphViewModel != null && !string.IsNullOrEmpty(repoPath))
                {
                    graphTask = graphViewModel.LoadRepositoryAsync(repoPath);
                }

                // Reload the branch sidebar from git too. Without this, an
                // external `git branch -D` (CLI, AI tooling, another GUI)
                // updates refs/heads/ or packed-refs but Leaf's sidebar
                // list stays as-was — the deleted branch lingers as a
                // phantom and resists deletion from inside Leaf because
                // git itself no longer knows about it. forceReload=true
                // bypasses the BranchesLoaded short-circuit so the list
                // is rebuilt every time the .git dir changes.
                if (repository != null)
                {
                    branchTask = LoadBranchesForRepoAsync(repository, forceReload: true, skipFilterApplication: true);
                }
            });

            if (graphViewModel == null || string.IsNullOrEmpty(repoPath))
            {
                return;
            }

            var infoTask = _gitService.GetRepositoryInfoFastAsync(repoPath, cancellationToken: CurrentRepositoryToken);
            await Task.WhenAll(graphTask, branchTask, infoTask).ConfigureAwait(false);
            Log.Perf("FileWatcher", "GitDir: graph + branches + info parallel", sw.ElapsedMilliseconds);

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
