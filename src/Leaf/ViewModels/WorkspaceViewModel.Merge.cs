using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// Merge-flow portion of <see cref="WorkspaceViewModel"/>. Hosts the
/// "Merge workspace → target branch" bulk command, the conflict-paused
/// resume state, and the persistence wrapper that keeps the
/// Continue/Cancel-merge affordances alive across an app restart.
/// Split out for readability — the main file owns tile lifecycle and
/// commit/sync flows, this one owns the merge state machine.
/// </summary>
public partial class WorkspaceViewModel
{
    /// <summary>
    /// Snapshot of a workspace merge that paused on a conflict.
    /// Carries the merge parameters + the tile we stopped on so
    /// <see cref="ContinueMergeAsync"/> can pick up without
    /// re-asking the user for branch + merge type.
    /// </summary>
    public sealed record PausedMergeState(string Target, MergeType MergeType, string PausedAtTilePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPausedMerge))]
    [NotifyCanExecuteChangedFor(nameof(ContinueMergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelPausedMergeCommand))]
    private PausedMergeState? _pausedMerge;

    /// <summary>
    /// True when the previous <see cref="MergeWorkspaceAsync"/> paused
    /// on a conflict. Drives the visibility of the Continue / Cancel
    /// merge buttons in the workspace action bar.
    /// </summary>
    public bool HasPausedMerge => PausedMerge is not null;

    /// <summary>
    /// Open the workspace merge dialog, then merge every repo's
    /// currently-checked-out branch into the chosen target. Submodules
    /// merge first (so their tips advance before the parent records
    /// new submodule SHAs), then the parent. A conflict in any repo
    /// pauses the workflow at that repo and stores the resume state
    /// so the user can hit "Continue merge" once they've resolved.
    /// </summary>
    [RelayCommand]
    public async Task MergeWorkspaceAsync()
    {
        var dialogVm = new WorkspaceMergeDialogViewModel();
        var dialog = new Views.WorkspaceMergeDialog { DataContext = dialogVm };
        if (!await _dialogService.ShowDialogAsync(dialog)) return;

        var target = dialogVm.TargetBranch.Trim();
        if (string.IsNullOrEmpty(target)) return;
        var mergeType = dialogVm.MergeType;

        // Fresh run — clear any prior pause state (in-memory + on-disk).
        await ClearPausedMergeAsync();
        await RunMergeLoopAsync(target, mergeType, resumeFromTilePath: null);
    }

    /// <summary>
    /// Resume a workspace merge that paused on a conflict. Re-enters
    /// the loop at the paused tile. Pre-checks the conflicted tile's
    /// MERGE_HEAD so a stray click on Continue without resolving the
    /// conflict surfaces a clear error rather than falling into a
    /// pause→pause loop.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanContinueMerge))]
    public async Task ContinueMergeAsync()
    {
        if (PausedMerge is null) return;
        var snapshot = PausedMerge;

        if (HasMergeInProgress(snapshot.PausedAtTilePath))
        {
            var pausedName = Tiles.FirstOrDefault(t =>
                string.Equals(t.RepositoryPath, snapshot.PausedAtTilePath, StringComparison.OrdinalIgnoreCase))?.Name
                ?? Path.GetFileName(snapshot.PausedAtTilePath);
            _notificationService.Show(
                "Cannot resume merge",
                $"Resolve conflicts in {pausedName} first (MERGE_HEAD still present), then click Continue merge.",
                NotificationType.Warning,
                Models.NotificationCategory.MergeAndRebase);
            return;
        }

        await RunMergeLoopAsync(snapshot.Target, snapshot.MergeType, resumeFromTilePath: snapshot.PausedAtTilePath);
    }

    private bool CanContinueMerge() => PausedMerge is not null;

    /// <summary>
    /// Discard the paused-merge state without resuming. Used when
    /// the user decides not to finish the workspace-wide merge —
    /// the individual repo-level merge state (MERGE_HEAD on the
    /// conflicted repo) is unaffected; only the workspace's
    /// "continue across all repos" intent is dropped.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanContinueMerge))]
    public async Task CancelPausedMergeAsync()
    {
        await ClearPausedMergeAsync();
    }

    internal async Task RunMergeLoopAsync(string target, MergeType mergeType, string? resumeFromTilePath)
    {
        await RunBulkAsync($"Merging workspace into {target}…", async (rootPath, token) =>
        {
            var tiles = WriteOrder().ToList();
            var startIndex = 0;
            if (resumeFromTilePath is not null)
            {
                var idx = tiles.FindIndex(t =>
                    string.Equals(t.RepositoryPath, resumeFromTilePath, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) startIndex = idx;
            }

            for (var i = startIndex; i < tiles.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var tile = tiles[i];
                BulkOperationStatus = $"Merging {tile.Name} into {target}…";
                MergeResult result;
                try
                {
                    result = mergeType switch
                    {
                        MergeType.Squash => await _gitService.SquashMergeAsync(tile.RepositoryPath, target, cancellationToken: tile.Token),
                        MergeType.FastForwardOnly => await _gitService.FastForwardAsync(tile.RepositoryPath, target, cancellationToken: tile.Token),
                        _ => await _gitService.MergeBranchAsync(tile.RepositoryPath, target, cancellationToken: tile.Token),
                    };
                }
                catch (Exception ex)
                {
                    _notificationService.Show(
                        "Workspace merge failed",
                        $"{tile.Name}: {ex.Message}",
                        NotificationType.Error);
                    return;
                }

                await LoadTileAsync(tile);

                if (result.HasConflicts)
                {
                    await SetPausedMergeAsync(tile, target, mergeType);
                    _notificationService.Show(
                        "Workspace merge paused",
                        $"Conflicts in {tile.Name}. Resolve them, then hit Continue merge to finish the remaining repos.",
                        NotificationType.Warning,
                        Models.NotificationCategory.MergeAndRebase);
                    return;
                }

                if (!result.Success)
                {
                    await SetPausedMergeAsync(tile, target, mergeType);
                    _notificationService.Show(
                        "Workspace merge halted",
                        $"{tile.Name}: {result.ErrorMessage ?? "unknown failure"}.",
                        NotificationType.Error);
                    return;
                }
            }

            await ClearPausedMergeAsync();
            _notificationService.Show(
                "Workspace merge complete",
                $"All repos merged into {target}.",
                NotificationType.Success,
                Models.NotificationCategory.MergeAndRebase);
        });
    }

    /// <summary>
    /// Capture pause state in-memory and into <c>.git/config</c> so the
    /// Continue merge affordance survives an app restart.
    /// </summary>
    private async Task SetPausedMergeAsync(SubmoduleTileViewModel pausedTile, string target, MergeType mergeType)
    {
        PausedMerge = new PausedMergeState(target, mergeType, pausedTile.RepositoryPath);
        if (Parent is not null)
        {
            var rel = ToRelativePath(Parent.Path, pausedTile.RepositoryPath);
            if (!string.IsNullOrEmpty(rel))
            {
                await _workspaceConfig.SetPausedMergeAsync(Parent.Path, target, mergeType.ToString(), rel);
            }
        }
    }

    private async Task ClearPausedMergeAsync()
    {
        PausedMerge = null;
        if (Parent is not null)
        {
            await _workspaceConfig.ClearPausedMergeAsync(Parent.Path);
        }
    }
}
