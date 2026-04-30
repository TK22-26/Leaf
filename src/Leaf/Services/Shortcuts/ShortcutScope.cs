namespace Leaf.Services.Shortcuts;

/// <summary>
/// Where a shortcut is active. Determines which window's
/// <c>InputBindings</c> the gesture gets attached to and which conflict
/// rules apply (Alt+1 in the merge editor doesn't conflict with Alt+1
/// somewhere else, because the merge editor is modal).
/// </summary>
public enum ShortcutScope
{
    /// <summary>Application-wide — wired into <c>MainWindow.InputBindings</c>.</summary>
    Application,

    /// <summary>Active only when the merge editor window is open.</summary>
    MergeEditor,

    /// <summary>Active only when a specific dialog is open. Dialogs that opt into the registry register their own shortcuts under this scope.</summary>
    Dialog,
}
