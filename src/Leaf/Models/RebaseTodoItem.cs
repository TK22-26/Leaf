namespace Leaf.Models;

/// <summary>
/// One row in an interactive-rebase plan. Carries enough state to be
/// serialised to git's todo grammar and to drive the editor UI directly —
/// no parallel display model. Mutable because the user reorders rows and
/// edits actions / messages live; the ViewModel listens for property
/// changes to update the plan preview.
/// </summary>
public sealed class RebaseTodoItem
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

    /// <summary>Action chosen by the user. Pre-populates as <see cref="RebaseTodoAction.Pick"/>.</summary>
    public RebaseTodoAction Action { get; set; } = RebaseTodoAction.Pick;

    /// <summary>
    /// Replacement commit message for <see cref="RebaseTodoAction.Reword"/> /
    /// <see cref="RebaseTodoAction.Squash"/>. Null for actions that don't
    /// rewrite a message; empty string is treated as "use original" — the
    /// service substitutes <see cref="OriginalMessage"/> if this is null
    /// or empty.
    /// </summary>
    public string? NewMessage { get; set; }

    /// <summary>Shell command for <see cref="RebaseTodoAction.Exec"/> rows.</summary>
    public string? ExecCommand { get; set; }

    /// <summary>True when the user has expanded the row's reword/squash editor in the UI. Persisted on the item so reorder doesn't collapse open editors.</summary>
    public bool IsMessageEditorOpen { get; set; }
}
