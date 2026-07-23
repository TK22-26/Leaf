using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    // Cache ScrollViewer reference - found once, reused
    private ScrollViewer? _parentScrollViewer;
    private bool _scrollViewerSearched;
    private bool _scrollViewerHooked;
    private bool _viewportRefreshPending;
    private bool _layoutRefreshHooked;
    private int _layoutRefreshPassesRemaining;
    private double _lastEffectiveViewportHeight = -1;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResetScrollViewerCache();
        AttachToScrollViewer();
        BeginViewportTracking();
        ScheduleViewportRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        EndViewportTracking();
        DetachFromScrollViewer();
    }

    private void ResetScrollViewerCache()
    {
        DetachFromScrollViewer();
        _parentScrollViewer = null;
        _scrollViewerSearched = false;
    }

    private void AttachToScrollViewer()
    {
        var scrollViewer = FindParentScrollViewer();
        if (scrollViewer == null)
            return;

        if (!ReferenceEquals(_parentScrollViewer, scrollViewer))
            _parentScrollViewer = scrollViewer;

        if (_scrollViewerHooked)
            return;

        _parentScrollViewer.ScrollChanged += ParentScrollViewer_ScrollChanged;
        _parentScrollViewer.SizeChanged += ParentScrollViewer_SizeChanged;
        _scrollViewerHooked = true;
    }

    private void DetachFromScrollViewer()
    {
        if (_parentScrollViewer != null && _scrollViewerHooked)
        {
            _parentScrollViewer.ScrollChanged -= ParentScrollViewer_ScrollChanged;
            _parentScrollViewer.SizeChanged -= ParentScrollViewer_SizeChanged;
        }
        _scrollViewerHooked = false;
    }

    private void ParentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Re-render visible range when scrolling to keep culling accurate.
        if (Math.Abs(e.ViewportHeightChange) > 0.5)
            BeginViewportTracking(2);

        // Auto-fit sizes the lane area to the widest lane on screen, so a
        // vertical scroll (or viewport resize) can change the required width.
        // Re-measure to track it — WPF skips re-layout when the width is
        // unchanged, so a scroll within a same-width band costs nothing. A
        // user-pinned lock owns the width, so skip it there.
        if (AutoFitLanes && LockedMaxColumn < 0
            && (Math.Abs(e.VerticalChange) > 0.5 || Math.Abs(e.ViewportHeightChange) > 0.5))
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    private void ParentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Re-render when viewport size changes (window resize/maximize).
        BeginViewportTracking(3);
        ScheduleViewportRefresh();
    }

    /// <summary>
    /// Finds and caches the parent ScrollViewer for viewport calculations.
    /// </summary>
    private ScrollViewer? FindParentScrollViewer()
    {
        if (_scrollViewerSearched)
            return _parentScrollViewer;

        _scrollViewerSearched = true;
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent != null)
        {
            if (parent is ScrollViewer sv)
            {
                _parentScrollViewer = sv;
                return sv;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void ScheduleViewportRefresh()
    {
        if (!IsLoaded || _viewportRefreshPending)
            return;

        _viewportRefreshPending = true;
        Dispatcher.InvokeAsync(() =>
        {
            _viewportRefreshPending = false;
            AttachToScrollViewer();
            InvalidateMeasure();
            InvalidateVisual();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BeginViewportTracking(int passCount = 6)
    {
        _layoutRefreshPassesRemaining = Math.Max(_layoutRefreshPassesRemaining, passCount);

        if (_layoutRefreshHooked)
            return;

        LayoutUpdated += GitGraphCanvas_LayoutUpdated;
        _layoutRefreshHooked = true;
    }

    private void EndViewportTracking()
    {
        if (!_layoutRefreshHooked)
            return;

        LayoutUpdated -= GitGraphCanvas_LayoutUpdated;
        _layoutRefreshHooked = false;
        _layoutRefreshPassesRemaining = 0;
        _lastEffectiveViewportHeight = -1;
    }

    private void GitGraphCanvas_LayoutUpdated(object? sender, EventArgs e)
    {
        if (!IsLoaded)
            return;

        AttachToScrollViewer();

        var effectiveViewportHeight = GetEffectiveViewportHeight(_parentScrollViewer);
        if (effectiveViewportHeight <= 0 || double.IsNaN(effectiveViewportHeight))
            return;

        bool viewportChanged = Math.Abs(effectiveViewportHeight - _lastEffectiveViewportHeight) > 0.5;
        if (viewportChanged)
        {
            _lastEffectiveViewportHeight = effectiveViewportHeight;
            ScheduleViewportRefresh();
        }

        if (_layoutRefreshPassesRemaining > 0)
        {
            _layoutRefreshPassesRemaining--;

            if (!viewportChanged)
                ScheduleViewportRefresh();
        }

        if (_layoutRefreshPassesRemaining <= 0 && effectiveViewportHeight >= RowHeight * 8)
            EndViewportTracking();
    }
}
