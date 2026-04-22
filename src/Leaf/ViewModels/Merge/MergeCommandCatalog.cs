#nullable enable
using Leaf.ViewModels;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// Static catalog mapping every user-invokable <see cref="MergeEditorViewModel"/>
/// action to a <see cref="CommandPaletteItem"/>. Consumed by
/// <see cref="MergeCommandPaletteViewModel"/> to populate the Ctrl+K merge
/// palette. Each item's <c>Tag</c> is the <see cref="System.Windows.Input.ICommand"/>
/// to invoke on confirm; <c>Detail</c> carries the human-readable keybinding
/// so the palette shows both the action and how to run it directly.
/// </summary>
public static class MergeCommandCatalog
{
    /// <summary>
    /// Build the palette entries for the given view-model. Re-created on every
    /// <see cref="MergeCommandPaletteViewModel.Open"/> call so the underlying
    /// command references stay fresh if the VM rebuilds them (e.g. after a
    /// document reload).
    /// </summary>
    public static IReadOnlyList<CommandPaletteItem> BuildFor(MergeEditorViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        return new CommandPaletteItem[]
        {
            // Navigation
            Item("Next conflict", "F8", vm.NextConflictCommand),
            Item("Previous conflict", "Shift+F8", vm.PreviousConflictCommand),
            Item("Next change span", "Alt+Right", vm.NextChangeSpanCommand),
            Item("Previous change span", "Alt+Left", vm.PreviousChangeSpanCommand),
            Item("Next auto-merged region", "Alt+Down", vm.NextAutoMergedRegionCommand),
            Item("Previous auto-merged region", "Alt+Up", vm.PreviousAutoMergedRegionCommand),

            // Current-range resolution (no index parameter)
            Item("Accept current conflict: Ours", "Alt+1", vm.AcceptCurrentConflictOursCommand),
            Item("Accept current conflict: Theirs", "Alt+2", vm.AcceptCurrentConflictTheirsCommand),
            Item("Accept current conflict: Both", "Alt+3", vm.AcceptCurrentConflictBothCommand),

            // Batch resolution
            Item("Accept all Ours", null, vm.AcceptAllOursCommand),
            Item("Accept all Theirs", null, vm.AcceptAllTheirsCommand),

            // Undo / redo
            Item("Undo", "Ctrl+Z", vm.UndoCommand),
            Item("Redo", "Ctrl+Y", vm.RedoCommand),

            // Finish / abort
            Item("Mark resolved", "Ctrl+Enter", vm.MarkResolvedCommand),
            Item("Complete merge", null, vm.CompleteMergeCommand),
            Item("Abort merge", null, vm.AbortMergeCommand),

            // AI assistance
            Item("Ask AI to propose a resolution", "Alt+A", vm.RequestAiResolutionCommand),

            // Misc
            Item("Copy composed text to clipboard", null, vm.CopyComposedTextCommand),
            Item("Use Ours (engine-error / binary)", null, vm.UseOursCommand),
            Item("Use Theirs (engine-error / binary)", null, vm.UseTheirsCommand),
        };
    }

    private static CommandPaletteItem Item(string displayName, string? keybinding, System.Windows.Input.ICommand command)
    {
        return new CommandPaletteItem
        {
            DisplayName = displayName,
            Detail = keybinding ?? string.Empty,
            Tag = command,
        };
    }
}
