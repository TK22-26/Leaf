using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Leaf.Behaviors;

/// <summary>
/// Enables horizontal scrolling via trackpad gestures and Shift+MouseWheel.
/// Apply to a Window to enable horizontal scrolling for all ScrollViewers within it.
/// </summary>
/// <remarks>
/// WPF doesn't natively expose horizontal scroll events from trackpads.
/// This behavior hooks into the WM_MOUSEHWHEEL window message to capture them.
/// See: https://github.com/dotnet/wpf/issues/5937
/// See: https://blog.walterlv.com/post/handle-horizontal-scrolling-of-touchpad-en.html
/// </remarks>
public static class HorizontalScrollBehavior
{
    private const int WM_MOUSEHWHEEL = 0x020E;

    /// <summary>
    /// Attached property to enable horizontal scroll behavior on a Window.
    /// </summary>
    public static readonly DependencyProperty EnableHorizontalScrollProperty =
        DependencyProperty.RegisterAttached(
            "EnableHorizontalScroll",
            typeof(bool),
            typeof(HorizontalScrollBehavior),
            new PropertyMetadata(false, OnEnableHorizontalScrollChanged));

    public static bool GetEnableHorizontalScroll(DependencyObject obj)
        => (bool)obj.GetValue(EnableHorizontalScrollProperty);

    public static void SetEnableHorizontalScroll(DependencyObject obj, bool value)
        => obj.SetValue(EnableHorizontalScrollProperty, value);

    private static void OnEnableHorizontalScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window)
        {
            if ((bool)e.NewValue)
            {
                window.SourceInitialized += Window_SourceInitialized;
                window.PreviewMouseWheel += Window_PreviewMouseWheel;
            }
            else
            {
                window.SourceInitialized -= Window_SourceInitialized;
                window.PreviewMouseWheel -= Window_PreviewMouseWheel;
            }
        }
    }

    private static void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            var source = PresentationSource.FromVisual(window) as HwndSource;
            source?.AddHook(WndProc);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEHWHEEL)
        {
            // Extract horizontal scroll delta (tilt)
            int tilt = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

            // Get the window from the hwnd
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.RootVisual is Window window)
            {
                // For WM_MOUSEHWHEEL, lParam contains SCREEN coordinates (not client!)
                int screenX = (short)(lParam.ToInt64() & 0xFFFF);
                int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                // Use Win32 ScreenToClient to get proper client coordinates
                var clientPt = new POINT { X = screenX, Y = screenY };
                ScreenToClient(hwnd, ref clientPt);

                // Get DPI scaling factor from the presentation source
                var transform = source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

                // Convert client pixel coordinates to WPF DIPs
                var mousePos = transform.Transform(new Point(clientPt.X, clientPt.Y));

                var scrollViewer = FindScrollViewerUnderPoint(window, mousePos);

                if (scrollViewer != null)
                {
                    // Scroll horizontally - tilt is typically in increments of 120 (like vertical scroll)
                    double scrollAmount = tilt / 4.0; // Adjust sensitivity as needed
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + scrollAmount);
                    handled = true;
                }
            }

            return (IntPtr)1;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Handles Shift+MouseWheel for horizontal scrolling (common convention).
    /// </summary>
    private static void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift && sender is Window window)
        {
            var mousePos = e.GetPosition(window);
            var scrollViewer = FindScrollViewerUnderPoint(window, mousePos);

            if (scrollViewer != null)
            {
                // Use the vertical delta for horizontal scrolling when Shift is held
                double scrollAmount = -e.Delta / 4.0; // Negate to match expected direction
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + scrollAmount);
                e.Handled = true;
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Finds the ScrollViewer under the given point in the visual tree.
    /// </summary>
    private static ScrollViewer? FindScrollViewerUnderPoint(Window window, Point point)
    {
        // Use InputHitTest which properly respects Visibility (unlike VisualTreeHelper.HitTest
        // which can return collapsed elements when they have high ZIndex)
        var current = window.InputHitTest(point) as DependencyObject;

        // Walk up the visual tree to find a ScrollViewer that can scroll horizontally
        while (current != null)
        {
            if (current is ScrollViewer scrollViewer)
            {
                // Check if there's horizontal content to scroll (ExtentWidth > ViewportWidth)
                // or if horizontal scrolling is explicitly enabled
                bool canScrollHorizontally = scrollViewer.ExtentWidth > scrollViewer.ViewportWidth ||
                                             scrollViewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto ||
                                             scrollViewer.HorizontalScrollBarVisibility == ScrollBarVisibility.Visible;

                if (canScrollHorizontally)
                {
                    return scrollViewer;
                }
            }
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
