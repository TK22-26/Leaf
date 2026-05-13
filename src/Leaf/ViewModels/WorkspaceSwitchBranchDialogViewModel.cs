using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.ViewModels;

/// <summary>
/// Backs <see cref="Leaf.Views.WorkspaceSwitchBranchDialog"/>. The user
/// types a branch name; the workspace command then iterates every repo
/// and attempts a checkout, skipping repos that don't have that branch.
/// </summary>
public partial class WorkspaceSwitchBranchDialogViewModel : ObservableObject
{
    /// <summary>
    /// The branch name to switch every repo to. Free-form text rather
    /// than a picker because branches differ across submodules — a
    /// picker would require enumerating + intersecting which is slow
    /// for a fresh-load workspace, and the typical use case is
    /// "switch all to <known feature branch name>".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitch))]
    private string _branchName = string.Empty;

    public bool CanSwitch => !string.IsNullOrWhiteSpace(BranchName);
}
