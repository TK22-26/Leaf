using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Controls;

/// <summary>
/// Control for displaying a single diff hunk with action buttons.
/// </summary>
public partial class HunkItemControl : UserControl
{
    public HunkItemControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Bubbles vertical scroll events to the parent ScrollViewer.
    /// </summary>
    private void LinesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Find the parent ScrollViewer and forward the scroll event
        var parentScrollViewer = FindParentScrollViewer(this);
        if (parentScrollViewer != null)
        {
            // Create a new event with the same delta and raise it on the parent
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = sender
            };
            parentScrollViewer.RaiseEvent(eventArg);
            e.Handled = true;
        }
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    /// <summary>
    /// Whether to show the Stage Hunk button.
    /// </summary>
    public static readonly DependencyProperty ShowStageButtonProperty =
        DependencyProperty.Register(
            nameof(ShowStageButton),
            typeof(bool),
            typeof(HunkItemControl),
            new PropertyMetadata(true));

    public bool ShowStageButton
    {
        get => (bool)GetValue(ShowStageButtonProperty);
        set => SetValue(ShowStageButtonProperty, value);
    }

    /// <summary>
    /// Event raised when the Revert Hunk button is clicked.
    /// </summary>
    public event EventHandler<DiffHunk>? RevertHunkRequested;

    /// <summary>
    /// Event raised when the Stage Hunk button is clicked.
    /// </summary>
    public event EventHandler<DiffHunk>? StageHunkRequested;

    private void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiffHunk hunk)
        {
            RevertHunkRequested?.Invoke(this, hunk);
        }
    }

    private void StageButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiffHunk hunk)
        {
            StageHunkRequested?.Invoke(this, hunk);
        }
    }
}
