#nullable enable
namespace Leaf.ViewModels;

/// <summary>
/// Contract every palette view-model must satisfy to be hosted by
/// <see cref="Leaf.Views.CommandPaletteView"/>. The view's code-behind dispatches
/// keyboard + mouse gestures through this interface; without it, sibling
/// palette implementations (the main-window VM and the merge-editor VM) would
/// each need their own clone of the view.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately narrow — only the methods the view needs to invoke.
/// Data-bound surfaces (SearchText, FilteredResults, SelectedIndex, …) are
/// resolved through WPF binding reflection on the concrete VM and are NOT part
/// of this interface; both VMs expose them with matching shapes so existing
/// bindings in <c>CommandPaletteView.xaml</c> continue to work unchanged.
/// </para>
/// <para>
/// <b>Open() is intentionally NOT on this interface.</b> The main-window
/// palette opens from its own data (repositories/branches) with no arguments;
/// the merge palette's <c>Open</c> takes an item list produced by
/// <see cref="Leaf.ViewModels.Merge.MergeCommandCatalog"/>. Each call site
/// already knows its concrete VM, so forcing a common <c>Open</c> signature
/// would require an awkward marker type or option bag. Adding a third host
/// would warrant revisiting — at that point the opening protocol itself
/// becomes the shared concept worth contracting.
/// </para>
/// </remarks>
public interface ICommandPaletteHost
{
    /// <summary>Move selection up, wrapping at the top.</summary>
    void MoveUp();

    /// <summary>Move selection down, wrapping at the bottom.</summary>
    void MoveDown();

    /// <summary>Confirm the currently selected item and dismiss.</summary>
    void Confirm();

    /// <summary>
    /// Confirm a specific item (used by the list-box click path).
    /// Must handle <c>null</c> safely.
    /// </summary>
    void ConfirmItem(CommandPaletteItem? item);

    /// <summary>
    /// Handle an Escape key press. Returns <c>true</c> when the palette
    /// consumed the event (including self-dismissal); a <c>false</c> return
    /// would indicate the host wants the shell to keep the key for its own
    /// handling — neither existing implementation does this today, but the
    /// contract stays bool-typed so the view code remains uniform.
    /// </summary>
    bool HandleEscape();

    /// <summary>Dismiss the palette without confirming any item.</summary>
    void Close();
}
