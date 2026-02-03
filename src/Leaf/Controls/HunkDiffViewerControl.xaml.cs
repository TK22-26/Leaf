using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Controls;

/// <summary>
/// Control for displaying a hunk-based diff view with individual revert capabilities.
/// </summary>
public partial class HunkDiffViewerControl : UserControl
{
    public HunkDiffViewerControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Whether to show Stage Hunk buttons (for unstaged changes).
    /// </summary>
    public static readonly DependencyProperty ShowStageButtonsProperty =
        DependencyProperty.Register(
            nameof(ShowStageButtons),
            typeof(bool),
            typeof(HunkDiffViewerControl),
            new PropertyMetadata(true));

    public bool ShowStageButtons
    {
        get => (bool)GetValue(ShowStageButtonsProperty);
        set => SetValue(ShowStageButtonsProperty, value);
    }

    private async void HunkItem_RevertHunkRequested(object? sender, DiffHunk hunk)
    {
        if (DataContext is HunkDiffViewerViewModel viewModel)
        {
            await viewModel.RevertHunkAsync(hunk);
        }
    }

    private async void HunkItem_StageHunkRequested(object? sender, DiffHunk hunk)
    {
        if (DataContext is HunkDiffViewerViewModel viewModel)
        {
            await viewModel.StageHunkAsync(hunk);
        }
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is HunkDiffViewerViewModel viewModel)
            {
                viewModel.Close();
                e.Handled = true;
            }
        }
    }
}
