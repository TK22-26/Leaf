#nullable enable
using System.Windows.Input;
using Leaf.Services.Shortcuts;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// Static catalog mapping every user-invokable <see cref="MergeEditorViewModel"/>
/// action to a <see cref="CommandPaletteItem"/>. Consumed by
/// <see cref="MergeCommandPaletteViewModel"/> to populate the Ctrl+K merge
/// palette. Each item's <c>Tag</c> is the <see cref="System.Windows.Input.ICommand"/>
/// to invoke on confirm; <c>Detail</c> carries the human-readable keybinding
/// so the palette shows both the action and how to run it directly.
/// </summary>
/// <remarks>
/// §5.9: keybinding strings are pulled from <see cref="IShortcutService"/>
/// so a user who rebinds Alt+1 sees their custom gesture next to the
/// command in the palette, not the stale default.
/// </remarks>
public static class MergeCommandCatalog
{
    private static readonly KeyGestureConverter GestureConverter = new();

    /// <summary>
    /// Build the palette entries for the given view-model. Re-created on every
    /// <see cref="MergeCommandPaletteViewModel.Open"/> call so the underlying
    /// command references stay fresh if the VM rebuilds them (e.g. after a
    /// document reload).
    /// </summary>
    public static IReadOnlyList<CommandPaletteItem> BuildFor(MergeEditorViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        // Resolve via the static service-locator pattern other merge-
        // editor views use. Leaf.App.Services THROWS when the provider
        // isn't built (e.g. unit tests instantiating the catalog
        // directly), so the try/catch keeps that surface friendly --
        // shortcuts == null falls back to the hardcoded defaults below.
        IShortcutService? shortcuts = null;
        try { shortcuts = Leaf.App.Services.GetService<IShortcutService>(); }
        catch (InvalidOperationException) { /* provider not built; fine */ }
        return new CommandPaletteItem[]
        {
            // Navigation
            Item("Next conflict", ShortcutCommandId.Merge.NextConflict, "F8", vm.NextConflictCommand, shortcuts),
            Item("Previous conflict", ShortcutCommandId.Merge.PreviousConflict, "Shift+F8", vm.PreviousConflictCommand, shortcuts),
            Item("Next change span", ShortcutCommandId.Merge.NextChangeSpan, "Alt+Right", vm.NextChangeSpanCommand, shortcuts),
            Item("Previous change span", ShortcutCommandId.Merge.PreviousChangeSpan, "Alt+Left", vm.PreviousChangeSpanCommand, shortcuts),
            Item("Next auto-merged region", ShortcutCommandId.Merge.NextAutoMergedRegion, "Alt+Down", vm.NextAutoMergedRegionCommand, shortcuts),
            Item("Previous auto-merged region", ShortcutCommandId.Merge.PreviousAutoMergedRegion, "Alt+Up", vm.PreviousAutoMergedRegionCommand, shortcuts),

            // Current-range resolution (no index parameter)
            Item("Accept current conflict: Ours", ShortcutCommandId.Merge.AcceptOurs, "Alt+1", vm.AcceptCurrentConflictOursCommand, shortcuts),
            Item("Accept current conflict: Theirs", ShortcutCommandId.Merge.AcceptTheirs, "Alt+2", vm.AcceptCurrentConflictTheirsCommand, shortcuts),
            Item("Accept current conflict: Both", ShortcutCommandId.Merge.AcceptBoth, "Alt+3", vm.AcceptCurrentConflictBothCommand, shortcuts),

            // Batch resolution
            Item("Accept all Ours", null, null, vm.AcceptAllOursCommand, shortcuts),
            Item("Accept all Theirs", null, null, vm.AcceptAllTheirsCommand, shortcuts),

            // Undo / redo
            Item("Undo", ShortcutCommandId.Merge.Undo, "Ctrl+Z", vm.UndoCommand, shortcuts),
            Item("Redo", ShortcutCommandId.Merge.Redo, "Ctrl+Y", vm.RedoCommand, shortcuts),

            // Finish / abort
            Item("Mark resolved", ShortcutCommandId.Merge.MarkResolved, "Ctrl+Enter", vm.MarkResolvedCommand, shortcuts),
            Item("Complete merge", null, null, vm.CompleteMergeCommand, shortcuts),
            Item("Abort merge", null, null, vm.AbortMergeCommand, shortcuts),

            // AI assistance
            Item("Ask AI to propose a resolution", ShortcutCommandId.Merge.RequestAiResolution, "Alt+A", vm.RequestAiResolutionCommand, shortcuts),

            // Misc
            Item("Copy composed text to clipboard", null, null, vm.CopyComposedTextCommand, shortcuts),
            Item("Copy Ours version of file", null, null, vm.CopyOursVersionCommand, shortcuts),
            Item("Copy Theirs version of file", null, null, vm.CopyTheirsVersionCommand, shortcuts),
            Item("Use Ours (engine-error / binary)", null, null, vm.UseOursCommand, shortcuts),
            Item("Use Theirs (engine-error / binary)", null, null, vm.UseTheirsCommand, shortcuts),
        };
    }

    private static CommandPaletteItem Item(
        string displayName,
        string? shortcutId,
        string? hardcodedFallback,
        ICommand command,
        IShortcutService? shortcuts)
    {
        return new CommandPaletteItem
        {
            DisplayName = displayName,
            Detail = ResolveDetail(shortcutId, hardcodedFallback, shortcuts),
            Tag = command,
        };
    }

    private static string ResolveDetail(string? shortcutId, string? fallback, IShortcutService? shortcuts)
    {
        if (shortcutId is null) return fallback ?? string.Empty;
        if (shortcuts is null) return fallback ?? string.Empty;
        var gesture = shortcuts.GetGesture(shortcutId);
        if (gesture is null) return string.Empty;
        return GestureConverter.ConvertToInvariantString(gesture) ?? fallback ?? string.Empty;
    }
}
