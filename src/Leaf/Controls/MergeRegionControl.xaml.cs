using System.Windows;
using System.Windows.Controls;
using Leaf.Models;

namespace Leaf.Controls;

/// <summary>
/// Control for displaying and resolving a single merge region.
/// Supports unchanged/auto-merged content display and conflict resolution.
/// </summary>
public partial class MergeRegionControl : UserControl
{
    /// <summary>
    /// Event raised when the user accepts ours for this region.
    /// </summary>
    public event EventHandler? AcceptOursRequested;

    /// <summary>
    /// Event raised when the user accepts theirs for this region.
    /// </summary>
    public event EventHandler? AcceptTheirsRequested;

    /// <summary>
    /// Event raised when the resolution state changes.
    /// </summary>
    public event EventHandler? ResolutionChanged;

    public MergeRegionControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MergeRegion region && region.IsConflict)
        {
            // Initialize selectable lines for conflict regions
            region.InitializeSelectableLines();

            // Subscribe to line selection changes
            if (region.OursSelectableLines != null)
            {
                foreach (var line in region.OursSelectableLines)
                {
                    line.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == nameof(SelectableLine.IsSelected))
                        {
                            region.UpdateResolutionFromSelection();
                            ResolutionChanged?.Invoke(this, EventArgs.Empty);
                        }
                    };
                }
            }

            if (region.TheirsSelectableLines != null)
            {
                foreach (var line in region.TheirsSelectableLines)
                {
                    line.PropertyChanged += (s, args) =>
                    {
                        if (args.PropertyName == nameof(SelectableLine.IsSelected))
                        {
                            region.UpdateResolutionFromSelection();
                            ResolutionChanged?.Invoke(this, EventArgs.Empty);
                        }
                    };
                }
            }
        }
    }

    private void AcceptOurs_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MergeRegion region)
        {
            region.SelectAllOurs();
            AcceptOursRequested?.Invoke(this, EventArgs.Empty);
            ResolutionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AcceptTheirs_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MergeRegion region)
        {
            region.SelectAllTheirs();
            AcceptTheirsRequested?.Invoke(this, EventArgs.Empty);
            ResolutionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ManualEdit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MergeRegion region)
        {
            region.EnterManualEditMode();
            ResolutionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DoneEditing_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MergeRegion region)
        {
            region.ExitManualEditMode();
            ResolutionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
