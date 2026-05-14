using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.ViewModels;

/// <summary>
/// Backs <see cref="Leaf.Views.WorkspaceSwitchBranchDialog"/>. The user
/// types a branch name + picks two booleans; the workspace command
/// then iterates every repo and attempts a checkout, honouring the
/// flags for missing-branch creation and dirty-tree stashing.
/// </summary>
public partial class WorkspaceSwitchBranchDialogViewModel : ObservableObject
{
    /// <summary>
    /// The branch name to switch every repo to. Free-form text rather
    /// than a picker because branches differ across submodules — a
    /// picker would require enumerating + intersecting which is slow
    /// for a fresh-load workspace, and the typical use case is
    /// "switch all to &lt;known feature branch name&gt;".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitch))]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    private string _branchName = string.Empty;

    /// <summary>
    /// When checked, repos that don't have <see cref="BranchName"/>
    /// get the branch CREATED at their current HEAD and checked out.
    /// Off by default — the safer "skip + report" behaviour matches
    /// what a user expects from a pure "switch" verb.
    /// </summary>
    [ObservableProperty]
    private bool _createIfMissing;

    /// <summary>
    /// When checked, repos with uncommitted changes are stashed
    /// (<c>git stash push -u</c>) before the checkout, so the switch
    /// can proceed without git refusing to overwrite local
    /// modifications. We do NOT auto-pop after switching — popping
    /// would re-introduce dirty state on the new branch which is
    /// often not what the user wants. The summary toast lists which
    /// repos got stashes so the user can pop them later if needed.
    /// </summary>
    [ObservableProperty]
    private bool _stashChanges;

    public bool CanSwitch =>
        !string.IsNullOrWhiteSpace(BranchName) && IsValidBranchName(BranchName);

    /// <summary>
    /// Human-readable validation error for <see cref="BranchName"/>,
    /// shown in the dialog under the input. Empty string when the
    /// name is valid (or empty — the placeholder text covers that
    /// case and we don't want to scream at a user who hasn't typed
    /// yet).
    /// </summary>
    public string ValidationError
    {
        get
        {
            if (string.IsNullOrEmpty(BranchName)) return string.Empty;
            return IsValidBranchName(BranchName)
                ? string.Empty
                : "Invalid branch name. Avoid spaces, ASCII control characters, and the sequences ~ ^ : ? * [ \\ .. // and @{.";
        }
    }

    /// <summary>
    /// Validate against the same rules <c>git check-ref-format
    /// --branch</c> enforces. Not exhaustive — git's full rule set is
    /// substantial — but covers every common footgun (spaces, control
    /// chars, reserved tokens) so we don't ship malformed branch names
    /// to the workspace iteration.
    /// </summary>
    /// <remarks>
    /// Rules implemented (subset of git's):
    /// <list type="bullet">
    ///   <item><description>No ASCII control chars, space, ~, ^, :, ?, *, [, \</description></item>
    ///   <item><description>No leading or trailing slash, no <c>..</c>, no <c>//</c></description></item>
    ///   <item><description>No leading or trailing dot, no path component starting with a dot</description></item>
    ///   <item><description>No <c>@{</c> sequence</description></item>
    ///   <item><description>Not literally "@", not ending in <c>.lock</c></description></item>
    /// </list>
    /// </remarks>
    public static bool IsValidBranchName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name == "@") return false;
        if (name.StartsWith('/') || name.EndsWith('/')) return false;
        if (name.StartsWith('.') || name.EndsWith('.')) return false;
        if (name.EndsWith(".lock", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Contains("..", System.StringComparison.Ordinal)) return false;
        if (name.Contains("//", System.StringComparison.Ordinal)) return false;
        if (name.Contains("@{", System.StringComparison.Ordinal)) return false;

        foreach (var ch in name)
        {
            if (ch < 0x20 || ch == 0x7f) return false; // control chars
            if (ch == ' ' || ch == '~' || ch == '^' || ch == ':' ||
                ch == '?' || ch == '*' || ch == '[' || ch == '\\') return false;
        }

        // No path segment may start with a dot. Split on '/' and check
        // each segment for that pattern.
        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0) return false;
            if (segment.StartsWith('.')) return false;
            if (segment.EndsWith(".lock", System.StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
