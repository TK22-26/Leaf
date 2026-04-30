namespace Leaf.Services.Shortcuts;

/// <summary>
/// String-typed identifiers for every shortcut Leaf understands.
/// Grouped by category so the Settings UI can render them in sections
/// and so a future feature knows where its new shortcut belongs.
/// </summary>
/// <remarks>
/// Identifiers are stable strings — they're persisted in user settings.
/// Renaming an id will silently drop the user's customisation. If a
/// command is renamed in code, keep the old id and add the new one in
/// parallel until a future major version drops the old.
/// </remarks>
public static class ShortcutCommandId
{
    public static class View
    {
        public const string ToggleTerminal = "view.toggleTerminal";
        public const string ToggleCommandPalette = "view.toggleCommandPalette";
        public const string ReportIssue = "view.reportIssue";
    }

    public static class Repository
    {
        public const string Fetch = "repo.fetch";
        public const string Pull = "repo.pull";
        public const string Push = "repo.push";
        public const string Refresh = "repo.refresh";
    }

    public static class Branch
    {
        public const string Create = "branch.create";
        public const string Checkout = "branch.checkout";
    }

    public static class Commit
    {
        public const string CreateCommit = "commit.commit";
        public const string Stash = "commit.stash";
        public const string PopStash = "commit.popStash";
    }

    public static class Merge
    {
        public const string AcceptOurs = "merge.acceptOurs";
        public const string AcceptTheirs = "merge.acceptTheirs";
        public const string AcceptBoth = "merge.acceptBoth";
        public const string MarkResolved = "merge.markResolved";
        public const string NextConflict = "merge.nextConflict";
        public const string PreviousConflict = "merge.previousConflict";
        public const string NextChangeSpan = "merge.nextChangeSpan";
        public const string PreviousChangeSpan = "merge.previousChangeSpan";
        public const string NextAutoMergedRegion = "merge.nextAutoMergedRegion";
        public const string PreviousAutoMergedRegion = "merge.previousAutoMergedRegion";
        public const string OpenPalette = "merge.openPalette";
        public const string Undo = "merge.undo";
        public const string Redo = "merge.redo";
        public const string RequestAiResolution = "merge.requestAiResolution";
        public const string ShowBlamePeek = "merge.showBlamePeek";
        // Note: CompleteMerge / AbortMerge are intentionally NOT in the
        // registry. They live on the merge editor's footer buttons and
        // run via mouse, not keyboard. Adding them here would create
        // unbound rows in Settings that confuse more than they help.
    }
}
