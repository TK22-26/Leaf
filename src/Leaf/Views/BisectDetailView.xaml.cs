using System.Windows;
using System.Windows.Controls;

namespace Leaf.Views;

/// <summary>
/// Right-pane view shown during a <c>git bisect</c> session. Slots
/// into the same right-column carousel as <see cref="CommitDetailView"/>
/// / <see cref="WorkingChangesView"/> / <see cref="MergeStatusView"/>;
/// the visibility trigger in <c>MainWindow.xaml</c> selects which is
/// shown based on the active git operation. Bound directly to
/// <c>MainViewModel</c> so it can read CurrentBisectState / BisectLog
/// / CurrentBisectChanges and invoke the verdict commands without an
/// extra adapter layer.
/// </summary>
public partial class BisectDetailView : UserControl
{
    public BisectDetailView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Toggle the verdict popup. The button's Click event fires; we
    /// open the Popup whose PlacementTarget is the button. WPF
    /// doesn't have a "menu button" primitive that ships with a
    /// matching dropdown, so this is the standard pattern for a
    /// hand-rolled split-button-style menu.
    /// </summary>
    private void VerdictDropdown_Click(object sender, RoutedEventArgs e)
    {
        VerdictPopup.IsOpen = !VerdictPopup.IsOpen;
    }

    /// <summary>
    /// Each verdict item inside the popup runs its bound Command and
    /// then closes the popup. Without this the popup stays open after
    /// a click, which feels broken — the menu pattern users expect is
    /// "click a choice, popup closes."
    /// </summary>
    private void VerdictItem_Click(object sender, RoutedEventArgs e)
    {
        VerdictPopup.IsOpen = false;
    }
}
