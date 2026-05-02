using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Shipped commit-template presets (§5.15 Phase 3). Each id is stable —
/// renaming would silently drop a user's tweaks because
/// <see cref="ICommitTemplateService.AddOrUpdate"/> keys overrides off
/// these strings.
/// </summary>
internal static class CommitTemplatePresets
{
    public const string ConventionalCommitsId = "preset.conventional-commits";
    public const string AngularId = "preset.angular";
    public const string GitmojiId = "preset.gitmoji";
    public const string TicketBasedId = "preset.ticket-based";
    public const string CoAuthoredId = "preset.co-authored";
    public const string SignedOffById = "preset.signed-off-by";

    /// <summary>
    /// Build the shipped preset list. Order in the returned list matches
    /// the order rendered in the picker — Conventional Commits first
    /// because it's the most common request, then a freeform Ticket-based
    /// helper, then the trailers (Co-authored, Signed-off-by) that get
    /// appended to existing messages rather than replacing them.
    /// </summary>
    public static IReadOnlyList<CommitTemplate> All() =>
    [
        new CommitTemplate
        {
            Id = ConventionalCommitsId,
            Name = "Conventional Commits",
            // Spec: type(scope): description / body / footer.
            // Cursor lands inside the parens so the user types scope
            // first, tabs out, and starts the description.
            Body =
                "{cursor}: \n\n" +
                "\n\n" +
                "Refs: {ticket}",
            // Most teams that adopt Conventional Commits follow JIRA-style
            // branch names — extract the leading project key + number.
            TicketRegex = @"^(?:feature|fix|hotfix|chore|release)/([A-Z]{2,10}-\d+)",
            Scope = CommitTemplateScope.Global,
            IsBuiltIn = true,
        },
        new CommitTemplate
        {
            Id = AngularId,
            Name = "Angular",
            // Angular's commit format is Conventional Commits with a
            // restricted type list — same body shape, but the preset
            // hint reminds the user about the allowed types.
            Body =
                "feat({cursor}): \n\n" +
                "# Allowed types: feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert\n" +
                "\n",
            TicketRegex = string.Empty,
            Scope = CommitTemplateScope.Global,
            IsBuiltIn = true,
        },
        new CommitTemplate
        {
            Id = GitmojiId,
            Name = "Gitmoji",
            Body = ":sparkles: {cursor}",
            TicketRegex = string.Empty,
            Scope = CommitTemplateScope.Global,
            IsBuiltIn = true,
        },
        new CommitTemplate
        {
            Id = TicketBasedId,
            Name = "Ticket-based",
            // For teams whose convention is "[TICKET-123] short summary".
            // Ticket goes in brackets up front; cursor lands after the
            // closing bracket.
            Body = "[{ticket}] {cursor}",
            TicketRegex = @"([A-Z]{2,10}-\d+)",
            Scope = CommitTemplateScope.Global,
            IsBuiltIn = true,
        },
        new CommitTemplate
        {
            Id = CoAuthoredId,
            Name = "Co-authored-by trailer",
            // Designed to be appended to an existing message, not used
            // standalone — the picker offers both Replace and Append on
            // apply so this works the same way as the trailer-style
            // presets in GitKraken.
            Body = "\n\nCo-authored-by: {cursor} <email@example.com>",
            TicketRegex = string.Empty,
            Scope = CommitTemplateScope.Global,
            IsBuiltIn = true,
        },
        new CommitTemplate
        {
            Id = SignedOffById,
            Name = "Signed-off-by trailer",
            // Project-policy footer for DCO-required repos. Author info
            // pulled from git config so the user only has to apply the
            // template — no manual typing.
            Body = "\n\nSigned-off-by: {user.name} <{user.email}>",
            TicketRegex = string.Empty,
            Scope = CommitTemplateScope.Global,
            IsBuiltIn = true,
        },
    ];
}
