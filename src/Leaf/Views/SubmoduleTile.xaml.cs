using System.Windows;
using System.Windows.Controls;

namespace Leaf.Views;

/// <summary>
/// View for a single workspace tile. Logic-free apart from the
/// overflow-button click handler — WPF's ContextMenu doesn't open on
/// a normal left-click by default, so we route the kebab button's
/// click event into showing its own ContextMenu programmatically.
/// </summary>
public partial class SubmoduleTile : UserControl
{
    public SubmoduleTile()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Open the kebab button's attached ContextMenu on a normal click.
    /// Anchoring it to the button as its placement target keeps the
    /// menu glued to the same chrome the user pressed, regardless of
    /// where the cursor was inside the button.
    /// </summary>
    private void OverflowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }
}
