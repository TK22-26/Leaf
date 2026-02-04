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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResetScrollViewerCache();
        AttachToScrollViewer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
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
        InvalidateVisual();
    }

    private void ParentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Re-render when viewport size changes (window resize/maximize).
        InvalidateVisual();
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
}
