using System.Windows;
using System.Windows.Controls;

namespace Leaf.Behaviors;

/// <summary>
/// Attached behavior for ratio-based scroll synchronization between two ScrollViewers.
/// Prevents recursive loops and only syncs on user-initiated scroll.
/// </summary>
public static class SynchronizedScrollBehavior
{
    private static bool _isSyncing;

    public static readonly DependencyProperty SyncGroupProperty =
        DependencyProperty.RegisterAttached(
            "SyncGroup",
            typeof(string),
            typeof(SynchronizedScrollBehavior),
            new PropertyMetadata(null, OnSyncGroupChanged));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SynchronizedScrollBehavior),
            new PropertyMetadata(true));

    private static readonly Dictionary<string, List<ScrollViewer>> SyncGroups = [];

    public static string? GetSyncGroup(DependencyObject obj) => (string?)obj.GetValue(SyncGroupProperty);
    public static void SetSyncGroup(DependencyObject obj, string? value) => obj.SetValue(SyncGroupProperty, value);
    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnSyncGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
            return;

        if (e.OldValue is string oldGroup && SyncGroups.TryGetValue(oldGroup, out var oldList))
        {
            oldList.Remove(scrollViewer);
            scrollViewer.ScrollChanged -= OnScrollChanged;
        }

        if (e.NewValue is string newGroup)
        {
            if (!SyncGroups.TryGetValue(newGroup, out var list))
            {
                list = [];
                SyncGroups[newGroup] = list;
            }

            list.Add(scrollViewer);
            scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncing)
            return;

        if (e.VerticalChange == 0 && e.HorizontalChange == 0)
            return;

        if (sender is not ScrollViewer source)
            return;

        if (!GetIsEnabled(source))
            return;

        var group = GetSyncGroup(source);
        if (group == null || !SyncGroups.TryGetValue(group, out var viewers))
            return;

        _isSyncing = true;
        try
        {
            var scrollableHeight = source.ScrollableHeight;
            var ratio = scrollableHeight > 0 ? source.VerticalOffset / scrollableHeight : 0;

            foreach (var viewer in viewers)
            {
                if (viewer == source || !GetIsEnabled(viewer))
                    continue;

                var targetOffset = viewer.ScrollableHeight * ratio;
                viewer.ScrollToVerticalOffset(targetOffset);
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
