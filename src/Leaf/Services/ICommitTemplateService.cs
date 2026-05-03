using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Source of truth for §5.15 commit templates. Composes:
/// <list type="bullet">
/// <item>Built-in presets (Conventional Commits, Angular, Gitmoji,
/// Ticket-based, Co-authored, Signed-off-by) — composed at runtime so
/// shipped updates apply automatically.</item>
/// <item>User-defined global templates from <c>AppSettings.CommitTemplates</c>.</item>
/// <item>Repo-scoped templates from <c>.git/leaf/commit-templates.json</c>
/// on the currently-active repository (or none, when no repo is open).</item>
/// </list>
///
/// <para>Single instance per app lifetime. Repo-scoped state is held
/// in-memory and refreshed via <see cref="SetActiveRepository"/> on every
/// repo switch — the service does not subscribe to repository events
/// itself.</para>
/// </summary>
public interface ICommitTemplateService
{
    /// <summary>
    /// All templates available right now: built-ins, then global, then
    /// repo-scoped (when a repo is active). Returned list is a fresh
    /// snapshot — callers must re-fetch on <see cref="TemplatesChanged"/>.
    /// </summary>
    IReadOnlyList<CommitTemplate> GetAll();

    /// <summary>
    /// Look up a template by id. Returns null when the id is unknown —
    /// callers (the picker, the apply path) decide whether the absence
    /// is fatal or skippable.
    /// </summary>
    CommitTemplate? GetById(string? id);

    /// <summary>
    /// Add or replace a template. Built-ins forward through this method
    /// when the user edits their body or ticket regex — the override gets
    /// stored as a custom entry that shadows the built-in.
    /// </summary>
    void AddOrUpdate(CommitTemplate template);

    /// <summary>
    /// Remove a template by id. Built-ins cannot be deleted; the call
    /// silently no-ops on built-in ids (the settings UI hides the Delete
    /// button for them).
    /// </summary>
    void Delete(string id);

    /// <summary>
    /// Reset every user-customised template back to the shipped defaults.
    /// Drops user global templates entirely and clears any per-repo file
    /// for the active repository.
    /// </summary>
    void ResetToDefaults();

    /// <summary>
    /// Switch the per-repo template store. Pass null when no repo is
    /// open. Always loads the repo-scoped file synchronously so the next
    /// <see cref="GetAll"/> call sees the new set immediately.
    /// </summary>
    void SetActiveRepository(string? repositoryPath);

    /// <summary>
    /// Resolve placeholders against the current state and return the
    /// final commit message. Honours <c>{cursor}</c> by recording its
    /// final character offset in <paramref name="cursorOffset"/>; when
    /// the body has no <c>{cursor}</c> token the offset is the end of
    /// the resolved string. Unknown tokens are preserved as-is.
    /// </summary>
    /// <param name="template">Template to apply.</param>
    /// <param name="branchName">Current branch name. May be null/empty when HEAD is detached.</param>
    /// <param name="userName">Git config <c>user.name</c>. May be null when unconfigured.</param>
    /// <param name="userEmail">Git config <c>user.email</c>. May be null when unconfigured.</param>
    /// <param name="cursorOffset">Out: index where the cursor should land after apply.</param>
    string Resolve(
        CommitTemplate template,
        string? branchName,
        string? userName,
        string? userEmail,
        out int cursorOffset);

    /// <summary>
    /// Fired when the set of available templates changes (added, edited,
    /// deleted, or repo switched). Listeners should re-call
    /// <see cref="GetAll"/>.
    /// </summary>
    event EventHandler? TemplatesChanged;
}
