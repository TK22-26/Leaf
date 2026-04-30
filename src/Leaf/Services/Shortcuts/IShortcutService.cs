using System.Windows.Input;

namespace Leaf.Services.Shortcuts;

/// <summary>
/// Central registry for keyboard shortcuts. Hosts (MainWindow, merge
/// editor, &#8230;) ask the service for the active gesture of a command and
/// build their <c>InputBindings</c> dynamically; the user's overrides
/// flow through the same surface.
/// </summary>
/// <remarks>
/// <para><b>Lifecycle:</b> the service is an app-lifetime singleton. The
/// definition table is populated once at startup via
/// <see cref="ShortcutDefaults.RegisterAll"/> and never mutated; user
/// overrides are layered on top in a separate dictionary that does
/// mutate.</para>
/// <para><b>Thread safety:</b> all public members are intended to be
/// called from the UI thread. The host re-applies bindings on
/// <see cref="GestureChanged"/>, which is fired on the same thread that
/// invoked <see cref="SetGesture"/>.</para>
/// </remarks>
public interface IShortcutService
{
    /// <summary>Every registered definition, in registration order. Stable across the lifetime of the service.</summary>
    IReadOnlyList<ShortcutDefinition> Definitions { get; }

    /// <summary>
    /// Currently-active gesture for <paramref name="commandId"/> — the
    /// user's override if set, otherwise the default. <c>null</c> means
    /// the command is intentionally unbound.
    /// </summary>
    KeyGesture? GetGesture(string commandId);

    /// <summary>
    /// Set the active gesture for <paramref name="commandId"/>:
    /// <list type="bullet">
    ///   <item>Pass a <see cref="KeyGesture"/> equal to the registered
    ///         default → the override entry is removed (settings file
    ///         stays minimal).</item>
    ///   <item>Pass any other <see cref="KeyGesture"/> → it becomes the
    ///         active gesture for this command.</item>
    ///   <item>Pass <c>null</c> → the command is explicitly unbound.
    ///         <see cref="GetGesture"/> returns null for it. To revert
    ///         to the default, pass the value of
    ///         <see cref="ShortcutDefinition.DefaultGesture"/>.</item>
    /// </list>
    /// If the new gesture conflicts with another shortcut in the same
    /// scope, the conflicting shortcut is unbound (its gesture moves to
    /// this command). Persists the change and fires
    /// <see cref="GestureChanged"/> for every affected id.
    /// </summary>
    void SetGesture(string commandId, KeyGesture? gesture);

    /// <summary>
    /// Drop every user override and persist. Fires
    /// <see cref="GestureChanged"/> with the changed ids so hosts can
    /// rebuild their bindings in one pass.
    /// </summary>
    void ResetAll();

    /// <summary>
    /// Find the command id whose currently-active gesture matches
    /// <paramref name="gesture"/> in <paramref name="scope"/>. Used by
    /// the Settings UI to surface conflicts when the user types a new
    /// binding into the rebind capture box.
    /// </summary>
    string? FindConflict(KeyGesture gesture, ShortcutScope scope);

    /// <summary>
    /// Raised after <see cref="SetGesture"/> or <see cref="ResetAll"/>.
    /// Carries the affected command id, or <c>null</c> when the change
    /// was an "all defaults" reset (host should rebuild every binding).
    /// </summary>
    event EventHandler<string?>? GestureChanged;
}
