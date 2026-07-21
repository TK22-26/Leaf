using Leaf.Models;
using Leaf.Services;
using Leaf.Utils;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial — sidebar selection state mutation.
///
/// These methods previously lived as static helpers in
/// <c>BranchListView.xaml.cs</c>. They mutate the <see cref="RepositoryInfo"/>
/// model graph (branch/tag/worktree/PR IsSelected flags and SelectedBranches
/// collection) and have no UI-layer coupling; the only reason they were in
/// code-behind was historical. Moved here per plan §2.7 so the code-behind
/// can stay focused on popup positioning and focus management.
///
/// Each method no-ops when <see cref="SelectedRepository"/> is null, so
/// callers don't need to guard before invoking.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Select a branch in the sidebar. When <paramref name="toggle"/> is
    /// true, flips this branch's selection without clearing others
    /// (Ctrl+click multi-select). Otherwise replaces the selection.
    /// Clears worktree and pull-request selections either way to keep
    /// the selection classes mutually exclusive.
    /// </summary>
    public void SelectBranch(BranchInfo branch, bool toggle)
    {
        var repo = SelectedRepository;
        if (repo == null) return;

        // Cross-class clear: any non-branch selection type drops first
        // so a branch click is always a clean transition. §5.17: tag
        // detail pane closes too so the right side returns to commit view.
        repo.ClearPullRequestSelection();
        repo.ClearSubmoduleSelection();
        repo.ClearWorktreeSelection();
        ClearTagDetailIfOpen();

        if (toggle)
        {
            if (branch.IsSelected)
            {
                branch.IsSelected = false;
                repo.SelectedBranches.Remove(branch);
            }
            else
            {
                branch.IsSelected = true;
                repo.SelectedBranches.Add(branch);
            }
            return;
        }

        repo.ClearBranchSelection();
        branch.IsSelected = true;
        repo.SelectedBranches.Add(branch);
    }

    /// <summary>
    /// Select a tag in the sidebar. Mirrors <see cref="SelectBranch"/>:
    /// <paramref name="toggle"/> flips this tag; otherwise clears other
    /// tag selections and selects this one. Always clears branch and PR
    /// selections to keep classes mutually exclusive.
    /// </summary>
    public void SelectTag(TagInfo tag, bool toggle)
    {
        var repo = SelectedRepository;
        if (repo == null) return;

        repo.ClearBranchSelection();
        repo.ClearPullRequestSelection();
        repo.ClearSubmoduleSelection();
        repo.ClearWorktreeSelection();

        if (toggle)
        {
            tag.IsSelected = !tag.IsSelected;
            // Update the detail pane to track the new selection state —
            // toggling off clears the pane so the user doesn't see stale
            // info, toggling on (re)populates with this tag.
            ShowTagDetail(tag.IsSelected ? tag : null);
            return;
        }

        repo.ClearTagSelection();
        tag.IsSelected = true;
        ShowTagDetail(tag);
    }

    /// <summary>
    /// §5.17 — push <paramref name="tag"/> (or null) into the detail
    /// pane. Lazily creates <see cref="TagDetailViewModel"/> on first
    /// use, wires its commands + navigate-to-commit hook, and kicks
    /// off an async load of the target commit so the mini-card hydrates.
    /// </summary>
    private void ShowTagDetail(TagInfo? tag)
    {
        if (tag is null)
        {
            if (TagDetailViewModel is not null) TagDetailViewModel.Tag = null;
            // Notify even when the field already pointed at the same
            // (null) tag so IsTagDetailMode re-evaluates and the right
            // pane swaps back to CommitDetailView cleanly.
            OnPropertyChanged(nameof(IsTagDetailMode));
            return;
        }

        TagDetailViewModel ??= new TagDetailViewModel
        {
            CheckoutTagCommand = CheckoutTagCommand,
            PushTagCommand = PushTagCommand,
            DeleteTagCommand = DeleteTagCommand,
            NavigateToCommit = sha =>
            {
                // Pages in more history when the tagged commit isn't
                // loaded yet; fire-and-forget keeps the click responsive.
                GitGraphViewModel?.SelectCommitByShaAsync(sha)
                    .FireAndForget(nameof(GitGraphViewModel.SelectCommitByShaAsync), isUserAction: true);
                // Switching to commit view means tag is no longer selected
                // for the detail pane — clear so the next selection cycle
                // can re-show.
                ShowTagDetail(null);
            },
        };
        TagDetailViewModel.Tag = tag;
        TagDetailViewModel.TargetCommit = null;
        OnPropertyChanged(nameof(IsTagDetailMode));

        if (SelectedRepository?.Path is { Length: > 0 } repoPath
            && !string.IsNullOrEmpty(tag.TargetSha))
        {
            LoadTagTargetCommitAsync(repoPath, tag).FireAndForget(
                nameof(LoadTagTargetCommitAsync), isUserAction: false);
        }
    }

    /// <summary>
    /// §5.17 — drop any open tag-detail state. No-op when no tag is
    /// selected. Public to the partial (called from
    /// <c>OnGitGraphViewModelPropertyChanged</c>) so commit-selection
    /// transitions can flush the tag pane.
    /// </summary>
    internal void ClearTagDetailIfOpen()
    {
        if (TagDetailViewModel?.Tag is null) return;
        ShowTagDetail(null);
        // Also flip IsSelected off on the tag itself so the sidebar's
        // selection visual matches the cleared detail pane.
        SelectedRepository?.ClearTagSelection();
    }

    private async Task LoadTagTargetCommitAsync(string repoPath, TagInfo tag)
    {
        try
        {
            var commit = await _gitService.GetCommitAsync(repoPath, tag.TargetSha,
                cancellationToken: CurrentRepositoryToken);
            // Late-arriving commit — only apply if the user is still
            // looking at this tag. Otherwise drop on the floor; another
            // tag's load is already in flight or the user moved on.
            if (TagDetailViewModel?.Tag == tag)
                TagDetailViewModel.TargetCommit = commit;
        }
        catch (OperationCanceledException) { /* repo switch */ }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException)
        {
            Log.Warn("Tag", $"Could not load target commit for {tag.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Select a worktree in the sidebar. Mirrors <see cref="SelectBranch"/>:
    /// <paramref name="toggle"/> flips this worktree; otherwise clears
    /// others in the same category. Clears branch and PR selections.
    /// </summary>
    public void SelectWorktree(WorktreeInfo worktree, bool toggle)
    {
        var repo = SelectedRepository;
        if (repo == null) return;

        repo.ClearBranchSelection();
        repo.ClearPullRequestSelection();
        repo.ClearSubmoduleSelection();
        ClearTagDetailIfOpen();

        if (toggle)
        {
            worktree.IsSelected = !worktree.IsSelected;
            return;
        }

        repo.ClearWorktreeSelection();
        worktree.IsSelected = true;
    }

    /// <summary>
    /// Select a pull request in the sidebar. PR selection is always
    /// exclusive (no toggle mode) — clicking another PR replaces the
    /// selection. Used by the right-click handler, which only needs the
    /// selection update before the context menu opens. Left-click uses
    /// <see cref="ActivatePullRequestAsync"/> which combines this with
    /// the navigation step.
    /// </summary>
    public void SelectPullRequestInSidebar(PullRequestInfo pr)
    {
        var repo = SelectedRepository;
        if (repo == null) return;

        repo.ClearBranchSelection();
        repo.ClearPullRequestSelection();
        repo.ClearSubmoduleSelection();
        repo.ClearWorktreeSelection();
        ClearTagDetailIfOpen();

        pr.IsSelected = true;
        repo.SelectedPullRequest = pr;
    }

    /// <summary>
    /// Select a submodule in the sidebar. Single-select model — clicking
    /// another row replaces the selection. Clears branch, worktree, PR,
    /// and tag selections to keep the cross-class selection mutually
    /// exclusive (matches every other Select* method's contract).
    /// </summary>
    public void SelectSubmodule(SubmoduleInfo submodule)
    {
        var repo = SelectedRepository;
        if (repo == null) return;

        repo.ClearBranchSelection();
        repo.ClearPullRequestSelection();
        repo.ClearSubmoduleSelection();
        repo.ClearWorktreeSelection();
        ClearTagDetailIfOpen();

        submodule.IsSelected = true;
    }

    /// <summary>
    /// Select a pull request in the sidebar and open its detail pane.
    /// Single entry point for left-click activation so the view doesn't
    /// have to sequence the sidebar-selection and navigate steps itself.
    /// </summary>
    public Task ActivatePullRequestAsync(PullRequestInfo pr)
    {
        SelectPullRequestInSidebar(pr);
        return SelectPullRequestCommand.ExecuteAsync(pr);
    }
}
