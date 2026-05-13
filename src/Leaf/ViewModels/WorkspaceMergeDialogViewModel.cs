using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// Backs <see cref="Leaf.Views.WorkspaceMergeDialog"/>. The user picks a
/// target branch and a uniform merge type (Normal / Squash / FF-only);
/// the workspace command then merges every repo into that target in
/// dependency order.
/// </summary>
public partial class WorkspaceMergeDialogViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    private string _targetBranch = string.Empty;

    /// <summary>
    /// Merge type applied uniformly to every repo. Per-repo merge type
    /// picking would multiply the dialog complexity for marginal value
    /// — power users who want a mixed merge can do it one repo at a
    /// time in single view.
    /// </summary>
    [ObservableProperty]
    private MergeType _mergeType = MergeType.Normal;

    public bool CanMerge => !string.IsNullOrWhiteSpace(TargetBranch);
}
