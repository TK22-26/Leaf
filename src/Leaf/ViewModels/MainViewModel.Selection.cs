using Leaf.Models;

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

        // Clear worktree and PR selections to avoid mixed selection types.
        repo.ClearPullRequestSelection();
        foreach (var category in repo.BranchCategories)
        {
            if (category.IsWorktreesCategory)
            {
                foreach (var wt in category.Worktrees)
                    wt.IsSelected = false;
            }
        }

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

        if (toggle)
        {
            tag.IsSelected = !tag.IsSelected;
            return;
        }

        foreach (var category in repo.BranchCategories)
        {
            if (category.IsTagsCategory)
            {
                foreach (var t in category.Tags)
                    t.IsSelected = false;
            }
        }
        tag.IsSelected = true;
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

        if (toggle)
        {
            worktree.IsSelected = !worktree.IsSelected;
            return;
        }

        foreach (var category in repo.BranchCategories)
        {
            if (category.IsWorktreesCategory)
            {
                foreach (var wt in category.Worktrees)
                    wt.IsSelected = false;
            }
        }
        worktree.IsSelected = true;
    }

    /// <summary>
    /// Select a pull request in the sidebar. PR selection is always
    /// exclusive (no toggle mode) — clicking another PR replaces the
    /// selection and fires through to the detail view via the
    /// SelectPullRequestCommand (called separately by the click handler).
    /// </summary>
    public void SelectPullRequestInSidebar(PullRequestInfo pr)
    {
        var repo = SelectedRepository;
        if (repo == null) return;

        repo.ClearBranchSelection();
        repo.ClearPullRequestSelection();

        pr.IsSelected = true;
        repo.SelectedPullRequest = pr;
    }
}
