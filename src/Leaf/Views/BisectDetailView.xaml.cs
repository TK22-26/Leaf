using System.Windows.Controls;

namespace Leaf.Views;

/// <summary>
/// Full-content view shown during a <c>git bisect</c> session. Slots
/// into the center carousel alongside the graph / PR detail / PR
/// create views; <c>MainViewModel.IsBisectMode</c> drives the
/// takeover. The verdict dropdown is a <see cref="Controls.DropdownButton"/>
/// which manages its own popup state (open/close, debounce,
/// auto-dismiss on inner-button click), so this code-behind has no
/// dropdown plumbing of its own.
/// </summary>
public partial class BisectDetailView : UserControl
{
    public BisectDetailView()
    {
        InitializeComponent();
    }
}
