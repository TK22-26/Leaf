using System.Windows.Controls;

namespace Leaf.Views;

/// <summary>
/// §5.17 — view-side shell for the tag detail pane. All behaviour is
/// driven from <see cref="ViewModels.TagDetailViewModel"/>; the
/// code-behind exists only because XAML's UserControl partial requires
/// one.
/// </summary>
public partial class TagDetailView : UserControl
{
    public TagDetailView()
    {
        InitializeComponent();
    }
}
