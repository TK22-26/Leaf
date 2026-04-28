using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Leaf.Behaviors;

/// <summary>
/// Attached property that fades the top and / or bottom edges of any
/// scrollable container into transparency. Set
/// <c>behaviors:ScrollEdgeFader.IsEnabled="True"</c> on a host
/// <see cref="FrameworkElement"/> (typically a <see cref="Grid"/> wrapping
/// a <see cref="ListBox"/>, <see cref="ListView"/>, <see cref="TreeView"/>,
/// or other scrollable element). The behaviour:
/// <list type="bullet">
/// <item>Locates the first descendant <see cref="ScrollViewer"/>.</item>
/// <item>Forces <see cref="UIElement.CacheMode"/> on the host to
/// <see cref="BitmapCache"/> so WPF renders the host + descendants to a
/// single offscreen surface that the <see cref="UIElement.OpacityMask"/>
/// can multiply into. Without the cache, the mask applies to the host's
/// own (empty) visual and the descendants render past it.</item>
/// <item>Subscribes to <see cref="ScrollViewer.ScrollChanged"/> and swaps
/// the mask between four states based on the live scroll position:
/// no fade when content fits, bottom-only when at top, top-only when at
/// bottom, and both edges when mid-scroll.</item>
/// </list>
/// Used by the right-side info panels (merge status, working changes,
/// commit detail) so the fade chrome reads consistently across them.
/// </summary>
public static class ScrollEdgeFader
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ScrollEdgeFader),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host) return;
        // No-op guard: WPF can fire DependencyProperty change callbacks
        // when the same value is re-set (e.g. style retrigger). Without
        // this, rapid true→true→false→false cycles would each subscribe
        // and unsubscribe Loaded handlers in pairs — net-zero leak but
        // wasted churn.
        if (Equals(e.OldValue, e.NewValue)) return;

        if ((bool)e.NewValue)
        {
            // Stash the host's existing CacheMode (almost always null in
            // practice) so DisableFading can put it back. Replacing it
            // unconditionally without a save would silently destroy any
            // CacheMode a future caller might have set independently.
            SetOriginalCacheMode(host, host.CacheMode);
            host.CacheMode = new BitmapCache { SnapsToDevicePixels = false };
            host.Loaded += OnHostLoaded;
            host.Unloaded += OnHostUnloaded;
            // Loaded may have already fired (e.g. behavior set after Loaded);
            // attempt an immediate hookup so the first paint isn't unfaded.
            if (host.IsLoaded) WireUpScrollViewer(host);
        }
        else
        {
            host.Loaded -= OnHostLoaded;
            host.Unloaded -= OnHostUnloaded;
            DetachScrollViewer(host);
            host.OpacityMask = null;
            host.CacheMode = GetOriginalCacheMode(host);
            SetOriginalCacheMode(host, null);
        }
    }

    private static void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host) WireUpScrollViewer(host);
    }

    private static void OnHostUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host) DetachScrollViewer(host);
    }

    private static void WireUpScrollViewer(FrameworkElement host)
    {
        var sv = FindDescendantScrollViewer(host);
        if (sv is null)
        {
            // Template not realized yet (e.g. host inside a collapsed
            // Expander or a virtualizing TabItem whose body hasn't rendered
            // its first frame). Hook a one-shot LayoutUpdated retry — by
            // the next layout pass the inner ScrollViewer should exist.
            // Without the retry the fade is permanently dead for that
            // host because Loaded only fires once.
            //
            // Closure captures `host` because LayoutUpdated's sender is the
            // dispatcher target, not the element the event was attached to.
            // The retryHandler-references-itself pattern is the standard C#
            // way to unsubscribe a lambda once it has fired successfully.
            EventHandler? retryHandler = null;
            retryHandler = (_, _) =>
            {
                var foundSv = FindDescendantScrollViewer(host);
                if (foundSv is null) return;
                host.LayoutUpdated -= retryHandler;
                AttachScrollViewer(host, foundSv);
            };
            host.LayoutUpdated += retryHandler;
            return;
        }
        AttachScrollViewer(host, sv);
    }

    private static void AttachScrollViewer(FrameworkElement host, ScrollViewer sv)
    {
        // Stash the ScrollViewer reference on the host so DetachScrollViewer
        // can find it later without re-walking the visual tree (the tree
        // may already have been torn down by the time Unloaded fires).
        SetTrackedScrollViewer(host, sv);
        sv.ScrollChanged -= OnScrollChanged;
        sv.ScrollChanged += OnScrollChanged;
        // Remember the host on the ScrollViewer too, so the scroll-changed
        // handler can find the host without traversing the tree per event.
        SetHostOf(sv, host);
        UpdateMask(host, sv);
    }

    private static void DetachScrollViewer(FrameworkElement host)
    {
        var sv = GetTrackedScrollViewer(host);
        if (sv is null) return;
        sv.ScrollChanged -= OnScrollChanged;
        SetTrackedScrollViewer(host, null);
        SetHostOf(sv, null);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        var host = GetHostOf(sv);
        if (host is null) return;
        UpdateMask(host, sv);
    }

    private static void UpdateMask(FrameworkElement host, ScrollViewer sv)
    {
        // Half-pixel slack on the boundary checks so a settling animation
        // (or sub-pixel residual offset) doesn't flicker the fade on/off.
        bool atTop = sv.VerticalOffset <= 0.5;
        bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 0.5
                        || sv.ScrollableHeight <= 0;

        host.OpacityMask = (atTop, atBottom) switch
        {
            (true, true) => null,                // Content fits — no fade
            (true, false) => BottomOnlyMask,
            (false, true) => TopOnlyMask,
            (false, false) => BothEdgesMask,
        };
    }

    // ── visual-tree helpers ──────────────────────────────────────────────

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        // Apply template explicitly so the inner ScrollViewer is reachable
        // even on the first Loaded callback before any layout pass has
        // forced template expansion.
        if (root is FrameworkElement fe) fe.ApplyTemplate();
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var nested = FindDescendantScrollViewer(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    // ── attached state (per-instance ScrollViewer + host references) ─────

    private static readonly DependencyProperty TrackedScrollViewerProperty = DependencyProperty.RegisterAttached(
        "TrackedScrollViewer", typeof(ScrollViewer), typeof(ScrollEdgeFader),
        new PropertyMetadata(null));

    private static ScrollViewer? GetTrackedScrollViewer(DependencyObject d) => (ScrollViewer?)d.GetValue(TrackedScrollViewerProperty);
    private static void SetTrackedScrollViewer(DependencyObject d, ScrollViewer? value) => d.SetValue(TrackedScrollViewerProperty, value);

    private static readonly DependencyProperty HostOfProperty = DependencyProperty.RegisterAttached(
        "HostOf", typeof(FrameworkElement), typeof(ScrollEdgeFader),
        new PropertyMetadata(null));

    private static FrameworkElement? GetHostOf(DependencyObject d) => (FrameworkElement?)d.GetValue(HostOfProperty);
    private static void SetHostOf(DependencyObject d, FrameworkElement? value) => d.SetValue(HostOfProperty, value);

    private static readonly DependencyProperty OriginalCacheModeProperty = DependencyProperty.RegisterAttached(
        "OriginalCacheMode", typeof(CacheMode), typeof(ScrollEdgeFader),
        new PropertyMetadata(null));

    private static CacheMode? GetOriginalCacheMode(DependencyObject d) => (CacheMode?)d.GetValue(OriginalCacheModeProperty);
    private static void SetOriginalCacheMode(DependencyObject d, CacheMode? value) => d.SetValue(OriginalCacheModeProperty, value);

    // ── frozen mask brushes ──────────────────────────────────────────────

    private static readonly Brush BottomOnlyMask = BuildBottomOnly();
    private static readonly Brush TopOnlyMask = BuildTopOnly();
    private static readonly Brush BothEdgesMask = BuildBothEdges();

    private static Brush BuildBottomOnly()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.80));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0, 0, 0), 0.90));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 0.96));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.00));
        b.Freeze();
        return b;
    }

    private static Brush BuildTopOnly()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.00));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 0.04));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0, 0, 0), 0.10));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.20));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 1.00));
        b.Freeze();
        return b;
    }

    private static Brush BuildBothEdges()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.00));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 0.04));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0, 0, 0), 0.10));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.20));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.80));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0, 0, 0), 0.90));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 0.96));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.00));
        b.Freeze();
        return b;
    }
}
