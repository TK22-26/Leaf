using System.Windows.Input;

namespace Leaf.Services.Shortcuts;

/// <summary>
/// Metadata for one shortcut Leaf understands. Stored in the registry's
/// definition table, joined with the user's optional override at lookup
/// time. The actual <see cref="ICommand"/> instance is resolved by the
/// host (MainWindow / merge editor view / etc.) when it builds its
/// <c>InputBindings</c>; the registry stays UI-agnostic.
/// </summary>
/// <param name="Id">
/// Stable string identifier. See <see cref="ShortcutCommandId"/>. Persisted
/// to disk — never rename without a migration.
/// </param>
/// <param name="Scope">Window scope the shortcut is bound to.</param>
/// <param name="Category">Display category for the Settings UI (e.g. "Repository", "Merge editor").</param>
/// <param name="Label">Human-readable label shown in the Settings UI.</param>
/// <param name="DefaultGesture">
/// Default key binding. <c>null</c> means the command has a stable id but
/// no default keystroke (only invokable via mouse/menu unless the user
/// rebinds).
/// </param>
public sealed record ShortcutDefinition(
    string Id,
    ShortcutScope Scope,
    string Category,
    string Label,
    KeyGesture? DefaultGesture);
