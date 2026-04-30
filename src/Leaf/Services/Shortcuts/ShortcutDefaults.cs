using System.Windows.Input;

namespace Leaf.Services.Shortcuts;

/// <summary>
/// Single registration point for every shortcut Leaf ships with. Phase 1
/// covers the App-scope shortcuts that live on <see cref="MainWindow"/>
/// today. Phase 2 expands to the full operation set called out in the
/// audit plan §5.9; Phase 3 wires the merge editor's existing
/// <c>InputBindings</c> through the registry too.
/// </summary>
internal static class ShortcutDefaults
{
    private const string CategoryView = "View";
    private const string CategoryRepository = "Repository";
    private const string CategoryBranch = "Branch";
    private const string CategoryCommit = "Commit";
    private const string CategoryMerge = "Merge editor";

    public static void RegisterAll(ShortcutService registry)
    {
        // ----- View / window-chrome shortcuts ------------------------
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.View.ToggleTerminal,
            ShortcutScope.Application,
            CategoryView,
            "Toggle integrated terminal",
            new KeyGesture(Key.Oem3, ModifierKeys.Control)));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.View.ToggleCommandPalette,
            ShortcutScope.Application,
            CategoryView,
            "Toggle command palette",
            new KeyGesture(Key.Space, ModifierKeys.Alt)));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.View.ReportIssue,
            ShortcutScope.Application,
            CategoryView,
            "Report an issue",
            new KeyGesture(Key.F1)));

        // ----- Repository operations (Phase 2 will wire the commands) -
        // Defaults match the audit plan §5.9 list. Definitions are
        // registered now so Phase 3's Settings UI has the rows to render
        // even before the corresponding ICommand wiring lands.
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Repository.Fetch,
            ShortcutScope.Application,
            CategoryRepository,
            "Fetch from remote",
            new KeyGesture(Key.F5)));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Repository.Pull,
            ShortcutScope.Application,
            CategoryRepository,
            "Pull from remote",
            new KeyGesture(Key.L, ModifierKeys.Control | ModifierKeys.Shift)));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Repository.Push,
            ShortcutScope.Application,
            CategoryRepository,
            "Push to remote",
            new KeyGesture(Key.P, ModifierKeys.Control | ModifierKeys.Shift)));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Repository.Refresh,
            ShortcutScope.Application,
            CategoryRepository,
            "Refresh repository view",
            // F5 is already used for Fetch — Refresh shares it on the
            // theory that "fetch + refresh" is what users mean by F5
            // anyway. The Phase 2 wiring will wire Refresh to a single
            // command that performs both. Distinct ids let users
            // unbind one if they prefer.
            DefaultGesture: null));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Branch.Create,
            ShortcutScope.Application,
            CategoryBranch,
            "Create new branch…",
            new KeyGesture(Key.B, ModifierKeys.Control)));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Branch.Checkout,
            ShortcutScope.Application,
            CategoryBranch,
            "Checkout branch…",
            // Ctrl+K is the merge palette; we'd shadow it here. Default
            // unbound — user assigns from Settings.
            DefaultGesture: null));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Commit.CreateCommit,
            ShortcutScope.Application,
            CategoryCommit,
            "Commit staged changes",
            new KeyGesture(Key.Enter, ModifierKeys.Control)));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Commit.Stash,
            ShortcutScope.Application,
            CategoryCommit,
            "Stash changes",
            new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Alt)));

        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Commit.PopStash,
            ShortcutScope.Application,
            CategoryCommit,
            "Pop latest stash",
            DefaultGesture: null));

        // ----- Merge editor (replaces the hardcoded XAML KeyBindings) ----
        // VS Code's Alt+1 / Alt+2 / Alt+3 layout is the de-facto pattern;
        // F8 / Shift+F8 matches build-error navigation many users know.
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.AcceptOurs,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Accept current — Ours",
            new KeyGesture(Key.D1, ModifierKeys.Alt)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.AcceptTheirs,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Accept current — Theirs",
            new KeyGesture(Key.D2, ModifierKeys.Alt)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.AcceptBoth,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Accept current — Both",
            new KeyGesture(Key.D3, ModifierKeys.Alt)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.NextConflict,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Next conflict",
            new KeyGesture(Key.F8)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.PreviousConflict,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Previous conflict",
            new KeyGesture(Key.F8, ModifierKeys.Shift)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.NextChangeSpan,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Next change span (within conflict)",
            new KeyGesture(Key.Right, ModifierKeys.Alt)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.PreviousChangeSpan,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Previous change span (within conflict)",
            new KeyGesture(Key.Left, ModifierKeys.Alt)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.NextAutoMergedRegion,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Next auto-merged region",
            new KeyGesture(Key.Down, ModifierKeys.Alt)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.PreviousAutoMergedRegion,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Previous auto-merged region",
            new KeyGesture(Key.Up, ModifierKeys.Alt)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.OpenPalette,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Open merge command palette",
            new KeyGesture(Key.K, ModifierKeys.Control)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.MarkResolved,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Mark current file resolved",
            new KeyGesture(Key.Enter, ModifierKeys.Control)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.Undo,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Undo",
            new KeyGesture(Key.Z, ModifierKeys.Control)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.Redo,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Redo",
            new KeyGesture(Key.Y, ModifierKeys.Control)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.RequestAiResolution,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Ask AI for a resolution",
            new KeyGesture(Key.A, ModifierKeys.Alt)));
        registry.Register(new ShortcutDefinition(
            ShortcutCommandId.Merge.ShowBlamePeek,
            ShortcutScope.MergeEditor,
            CategoryMerge,
            "Show blame peek",
            new KeyGesture(Key.B, ModifierKeys.Alt)));
        // Note: Ctrl+Shift+Z (Redo alternative) is intentionally not in
        // the registry — it stays as a hardcoded alias inside the merge
        // editor view because the registry's one-id-one-gesture model
        // doesn't model dual-keybindings cleanly. If a user rebinds
        // Redo elsewhere, this alias stays bound to Redo for muscle
        // memory. Future: registry could grow an "alias" concept.
    }
}
