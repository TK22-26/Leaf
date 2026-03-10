using System.ComponentModel;
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

    private readonly List<(SelectableLine Line, PropertyChangedEventHandler Handler)> _subscribedLines = [];

    public MergeRegionControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe old handlers to prevent memory leaks on DataContext change
        foreach (var (line, handler) in _subscribedLines)
            line.PropertyChanged -= handler;
        _subscribedLines.Clear();

        if (e.NewValue is MergeRegion region && region.IsConflict)
        {
            // Initialize selectable lines for conflict regions
            region.InitializeSelectableLines();

            SubscribeLineChanges(region.OursSelectableLines, region);
            SubscribeLineChanges(region.TheirsSelectableLines, region);
        }
    }

    private void SubscribeLineChanges(IEnumerable<SelectableLine>? lines, MergeRegion region)
    {
        if (lines == null) return;
        foreach (var line in lines)
        {
            PropertyChangedEventHandler handler = (s, args) =>
            {
                if (args.PropertyName == nameof(SelectableLine.IsSelected))
                {
                    region.UpdateResolutionFromSelection();
                    ResolutionChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            line.PropertyChanged += handler;
            _subscribedLines.Add((line, handler));
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
