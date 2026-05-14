using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the rebase dialog. Mirrors <see cref="MergeDialogViewModel"/>
/// in shape so the UI feels consistent — radio group for the strategy, a couple
/// of pass-through flags, and the source/target labels for context.
/// </summary>
/// <remarks>
/// The two flags map directly to <c>git rebase</c> arguments:
/// <list type="bullet">
///   <item><description><see cref="Autosquash"/> → <c>--autosquash</c></description></item>
///   <item><description><see cref="UpdateRefs"/> → <c>--update-refs</c></description></item>
/// </list>
/// They are surfaced for both Standard and Interactive — git accepts them in
/// both modes and they're commonly wanted together (autosquash is most useful
/// during an interactive rebase but works fine non-interactively too).
/// </remarks>
public partial class RebaseDialogViewModel : ObservableObject
{
    /// <summary>Branch the user is currently on (the branch being moved).</summary>
    [ObservableProperty]
    private string _sourceBranch = string.Empty;

    /// <summary>The branch HEAD will be replayed onto.</summary>
    [ObservableProperty]
    private string _targetBranch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    [NotifyPropertyChangedFor(nameof(RebaseButtonText))]
    private RebaseMode _selectedMode = RebaseMode.Standard;

    /// <summary>Pass <c>--autosquash</c> when starting the rebase.</summary>
    [ObservableProperty]
    private bool _autosquash;

    /// <summary>
    /// Pass <c>--update-refs</c>. Off by default — it rewrites every ref that
    /// points at any commit in the rebased range, which is powerful but
    /// surprising for users who don't expect ancillary branches to move.
    /// </summary>
    [ObservableProperty]
    private bool _updateRefs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRebase))]
    private bool _isRebasing;

    /// <summary>True when the user can press the primary action button.</summary>
    public bool CanRebase => !IsRebasing;

    /// <summary>Title for the dialog (changes based on mode).</summary>
    public string DialogTitle => SelectedMode switch
    {
        RebaseMode.Interactive => "Interactive Rebase",
        _ => "Rebase Branch"
    };

    /// <summary>Primary button label (changes based on mode).</summary>
    public string RebaseButtonText => SelectedMode switch
    {
        RebaseMode.Interactive => "Continue…",
        _ => "Rebase"
    };
}
