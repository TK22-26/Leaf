using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the merge dialog.
/// </summary>
public partial class MergeDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _sourceBranch = string.Empty;

    [ObservableProperty]
    private string _targetBranch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private bool _isSquashMerge;

    [ObservableProperty]
    private bool _isMerging;

    /// <summary>
    /// Title for the dialog (changes based on merge type).
    /// </summary>
    public string DialogTitle => IsSquashMerge ? "Squash and Merge" : "Merge Branch";

    /// <summary>
    /// Button text for the merge action.
    /// </summary>
    public string MergeButtonText => IsSquashMerge ? "Squash and Merge" : "Merge";

    /// <summary>
    /// True if the merge can proceed (non-empty message for squash merge).
    /// </summary>
    public bool CanMerge => !IsMerging && (!IsSquashMerge || !string.IsNullOrWhiteSpace(CommitMessage));
}
