using System.Windows.Controls;

namespace Leaf.Views;

/// <summary>
/// View for a single workspace tile. Logic-free — the title bar is
/// data-bound to <see cref="ViewModels.SubmoduleTileViewModel"/> and
/// the body hosts <see cref="GitGraphView"/> driven by the tile's
/// graph VM. Quick-action commands hook up in Phase D.
/// </summary>
public partial class SubmoduleTile : UserControl
{
    public SubmoduleTile()
    {
        InitializeComponent();
    }
}
