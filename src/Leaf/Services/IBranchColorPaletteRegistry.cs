using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Source of truth for which branch-colour palettes are available. Composes
/// the shipped built-in palettes with user-defined custom palettes loaded
/// from <see cref="AppSettings.CustomBranchColorPalettes"/>. Single
/// instance per app lifetime (registered as singleton in
/// <see cref="Composition.ServiceRegistry"/>).
///
/// <para>The registry is the only writer of <c>CustomBranchColorPalettes</c>
/// — settings UI calls <see cref="AddOrUpdateCustom"/>/<see cref="DeleteCustom"/>
/// rather than mutating the list directly so the
/// <see cref="PalettesChanged"/> event fires consistently.</para>
/// </summary>
public interface IBranchColorPaletteRegistry
{
    /// <summary>
    /// All available palettes — built-ins first, then custom in user-defined
    /// order. Returned list is a fresh snapshot; callers must re-fetch on
    /// <see cref="PalettesChanged"/> to see new entries.
    /// </summary>
    IReadOnlyList<BranchColorPalette> GetAll();

    /// <summary>
    /// Look up a palette by id. Returns the default palette as a fallback
    /// when the id is unknown — used by the colour service when a settings
    /// file references a custom palette the user has since deleted.
    /// </summary>
    BranchColorPalette GetById(string? id);

    /// <summary>The shipped default palette. Always non-null.</summary>
    BranchColorPalette Default { get; }

    /// <summary>
    /// Add a new custom palette or update an existing one (matched by
    /// <see cref="BranchColorPalette.Id"/>). Persists to AppSettings and
    /// fires <see cref="PalettesChanged"/>. Built-in palettes cannot be
    /// modified — callers must clone first via
    /// <see cref="CloneBuiltInForEditing"/>.
    /// </summary>
    void AddOrUpdateCustom(BranchColorPalette palette);

    /// <summary>
    /// Remove a custom palette by id. No-op when the id refers to a built-in
    /// or doesn't exist. Persists and fires <see cref="PalettesChanged"/>.
    /// </summary>
    void DeleteCustom(string id);

    /// <summary>
    /// Create an editable copy of a palette (typically a built-in) with a
    /// fresh GUID id. Caller is responsible for setting a unique
    /// <see cref="BranchColorPalette.DisplayName"/> and persisting via
    /// <see cref="AddOrUpdateCustom"/>.
    /// </summary>
    BranchColorPalette CloneBuiltInForEditing(BranchColorPalette source);

    /// <summary>
    /// Fired when the set of available palettes changes (custom palette
    /// added/edited/deleted). Listeners should re-call <see cref="GetAll"/>.
    /// </summary>
    event EventHandler? PalettesChanged;
}
