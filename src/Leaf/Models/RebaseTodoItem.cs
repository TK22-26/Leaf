using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// One row in an interactive-rebase plan. Carries enough state to be
/// serialised to git's todo grammar and to drive the editor UI directly —
/// no parallel display model. <see cref="ObservableObject"/> base so WPF
/// bindings see live updates when the user changes <see cref="Action"/>,
/// types into <see cref="NewMessage"/>, or reorders rows (siblings refresh
/// derived bindings like CanMoveUp/CanMoveDown).
/// </summary>
public partial class RebaseTodoItem : ObservableObject
{
    /// <summary>Full SHA of the commit. Always present, even for <see cref="RebaseTodoAction.Exec"/> rows that don't reference a commit (set to empty).</summary>
    public string Sha { get; init; } = string.Empty;

    /// <summary>Short SHA (typically 7 chars) for display.</summary>
    public string ShortSha { get; init; } = string.Empty;

    /// <summary>Author identity at time of capture, "Name &lt;email&gt;" form.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Authored timestamp at time of capture.</summary>
    public DateTimeOffset AuthoredWhen { get; init; }

    /// <summary>First line of the commit message.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Full commit message body (subject + body), used as the seed when the user opens reword/squash.</summary>
    public string OriginalMessage { get; init; } = string.Empty;

    /// <summary>Action chosen by the user. Defaults to <see cref="RebaseTodoAction.Pick"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRewordOrSquash))]
    [NotifyPropertyChangedFor(nameof(IsExec))]
    [NotifyPropertyChangedFor(nameof(WillRewriteCommit))]
    [NotifyPropertyChangedFor(nameof(IsDropped))]
    private RebaseTodoAction _action = RebaseTodoAction.Pick;

    /// <summary>
    /// Replacement commit message for <see cref="RebaseTodoAction.Reword"/> /
    /// <see cref="RebaseTodoAction.Squash"/>. Null or empty falls back to
    /// <see cref="OriginalMessage"/> at materialisation time.
    /// </summary>
    [ObservableProperty]
    private string? _newMessage;

    /// <summary>Shell command for <see cref="RebaseTodoAction.Exec"/> rows.</summary>
    [ObservableProperty]
    private string? _execCommand;

    /// <summary>
    /// Tooltip shown on the row in the editor — surfaces the author and
    /// authored date that <see cref="LoadPlanAsync"/> captured. Empty for
    /// synthetic Exec rows that don't reference a commit.
    /// </summary>
    public string AuthorTooltip
    {
        get
        {
            if (string.IsNullOrEmpty(Sha)) return string.Empty;
            return AuthoredWhen == default
                ? Author
                : $"{Author} · {AuthoredWhen.LocalDateTime:yyyy-MM-dd HH:mm}";
        }
    }

    /// <summary>Convenience flag for view bindings — does this row need a message editor?</summary>
    public bool IsRewordOrSquash =>
        Action == RebaseTodoAction.Reword || Action == RebaseTodoAction.Squash;

    /// <summary>Convenience flag — does this row need a command editor?</summary>
    public bool IsExec => Action == RebaseTodoAction.Exec;

    /// <summary>True when the action mutates history (anything except a plain Pick or noop Drop). Used to drive a "you'll be rewriting commits" warning in the UI footer.</summary>
    public bool WillRewriteCommit =>
        Action is RebaseTodoAction.Reword
            or RebaseTodoAction.Edit
            or RebaseTodoAction.Squash
            or RebaseTodoAction.Fixup
            or RebaseTodoAction.Drop;

    /// <summary>Convenience flag — is this row excluded from the rewritten history?</summary>
    public bool IsDropped => Action == RebaseTodoAction.Drop;
}
