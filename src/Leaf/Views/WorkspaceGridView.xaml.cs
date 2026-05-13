using System.Windows.Controls;

namespace Leaf.Views;

/// <summary>
/// Host control for the workspace tile grid. Logic-free: the layout
/// rules live in <see cref="Controls.WorkspaceTilePanel"/>, which
/// computes a rectangle per tile based on the live tile count (so the
/// 3-tile parent-fills-left-half special case stays declarative).
/// </summary>
public partial class WorkspaceGridView : UserControl
{
    public WorkspaceGridView()
    {
        InitializeComponent();
    }
}
