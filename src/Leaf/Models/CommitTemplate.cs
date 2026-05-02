namespace Leaf.Models;

/// <summary>
/// Persisted record of a single commit-message template (§5.15). A template
/// owns a name, a body with <c>{placeholder}</c> tokens, an optional
/// branch-name regex used to extract a ticket id, and a scope flag that
/// controls where it gets stored.
///
/// <para>POCO with public mutable properties because the template is round-
/// tripped through <c>System.Text.Json</c> and edited inline by the settings
/// UI. The service treats stored instances as immutable from the outside —
/// callers get a fresh copy through <c>ICommitTemplateService.GetAll</c>.</para>
/// </summary>
public sealed class CommitTemplate
{
    /// <summary>
    /// Stable identifier — GUID for user-created and built-in alike. Used as
    /// the registry key by <see cref="Services.ICommitTemplateService"/>.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown in the picker and the settings list. Required;
    /// the service rejects blank names on save.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Template body with <c>{placeholder}</c> tokens. Recognised tokens:
    /// <c>{branch}</c>, <c>{date}</c>, <c>{datetime}</c>, <c>{user.name}</c>,
    /// <c>{user.email}</c>, <c>{ticket}</c>, <c>{cursor}</c>. Unknown
    /// tokens are left as-is so a user-typed brace expression doesn't get
    /// silently swallowed.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Optional regex applied to the current branch name to extract a
    /// ticket id substituted into <c>{ticket}</c>. The first capture
    /// group's value wins; if the regex doesn't match or is empty, the
    /// <c>{ticket}</c> token resolves to an empty string. Per-template
    /// (rather than a single global pattern) because different teams use
    /// different formats — JIRA, GitHub issues, Azure DevOps work item ids.
    /// </summary>
    public string TicketRegex { get; set; } = string.Empty;

    /// <summary>
    /// Where this template lives. <see cref="CommitTemplateScope.Global"/>
    /// is persisted in <c>AppSettings.CommitTemplates</c> and visible across
    /// every repo. <see cref="CommitTemplateScope.Repository"/> is persisted
    /// in <c>.git/leaf/commit-templates.json</c> on the active repo and
    /// only appears when that repo is open.
    /// </summary>
    public CommitTemplateScope Scope { get; set; } = CommitTemplateScope.Global;

    /// <summary>
    /// True for the shipped presets. Built-ins can't be deleted or renamed
    /// from the UI (the body and ticket-regex stay editable so a user can
    /// tweak Conventional Commits for their workflow without recreating
    /// the whole template). Preserved through round-trip but always
    /// recomputed at load time so a hand-edited settings.json can't grant
    /// itself built-in status.
    /// </summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>
/// Storage scope for a <see cref="CommitTemplate"/>.
/// </summary>
public enum CommitTemplateScope
{
    /// <summary>Visible across every repo. Stored in app settings.</summary>
    Global = 0,

    /// <summary>Visible only when the originating repo is open. Stored in <c>.git/leaf/commit-templates.json</c>.</summary>
    Repository = 1,
}
