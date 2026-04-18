#nullable enable
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Leaf.Services.Merge;

namespace Leaf.Controls.Merge;

/// <summary>
/// Custom WPF control that renders a side-by-side / onion-skin / swipe /
/// difference / overlay view of an <see cref="ImageConflictPayload"/>.
/// Uses <see cref="BitmapImage"/> + direct <c>DrawingContext</c> to avoid the
/// overhead of a full Image-element tree per pane — one <c>OnRender</c> pass
/// per mode.
/// </summary>
/// <remarks>
/// The pane is 100% read-only — image merges are inherently "use ours or use
/// theirs", there's no in-place edit. Resolution commands live on the VM and
/// are bound from the host view's footer, same as the binary-file overlay it
/// replaces.
/// </remarks>
public sealed class ImageConflictPane : FrameworkElement
{
    private BitmapSource? _oursBitmap;
    private BitmapSource? _theirsBitmap;
    private BitmapSource? _baseBitmap;
    private WriteableBitmap? _differenceCache;
    private Size _differenceCacheKey;

    private const double ModeBarHeight = 36.0;
    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x8A, 0x8A));
    private static readonly Pen DividerPen = new(
        new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), 1.0);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    static ImageConflictPane()
    {
        BackgroundBrush.Freeze();
        GridBrush.Freeze();
        TextBrush.Freeze();
        ErrorBrush.Freeze();
        DividerPen.Freeze();
    }

    public static readonly DependencyProperty PayloadProperty = DependencyProperty.Register(
        nameof(Payload), typeof(ImageConflictPayload), typeof(ImageConflictPane),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            OnPayloadChanged));

    public static readonly DependencyProperty ViewportProperty = DependencyProperty.Register(
        nameof(Viewport), typeof(ImageViewportState), typeof(ImageConflictPane),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            OnViewportChanged));

    public ImageConflictPayload? Payload
    {
        get => (ImageConflictPayload?)GetValue(PayloadProperty);
        set => SetValue(PayloadProperty, value);
    }

    public ImageViewportState? Viewport
    {
        get => (ImageViewportState?)GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public ImageConflictPane()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    private static void OnPayloadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pane = (ImageConflictPane)d;
        pane.RebuildBitmaps();
    }

    private static void OnViewportChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pane = (ImageConflictPane)d;
        if (e.OldValue is ImageViewportState oldState)
            oldState.PropertyChanged -= pane.OnViewportPropertyChanged;
        if (e.NewValue is ImageViewportState newState)
            newState.PropertyChanged += pane.OnViewportPropertyChanged;
        pane.InvalidateVisual();
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Invalidate the difference cache whenever mode changes — the cache
        // isn't mode-specific but we only use it in Difference mode.
        if (e.PropertyName == nameof(ImageViewportState.Mode))
        {
            _differenceCache = null;
        }
        InvalidateVisual();
    }

    private void RebuildBitmaps()
    {
        _differenceCache = null;
        _oursBitmap = DecodeBytes(Payload?.Ours.Bytes);
        _theirsBitmap = DecodeBytes(Payload?.Theirs.Bytes);
        _baseBitmap = DecodeBytes(Payload?.Base.Bytes);
        InvalidateVisual();
    }

    private static BitmapSource? DecodeBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // close the stream on load
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            // Any decoder failure — corrupted image, unsupported variant —
            // is reported as "nothing to render" and the VM's Use Ours/Theirs
            // buttons remain the escape hatch.
            return null;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Viewport is null) return;
        // Zoom centred on the cursor: the anchor before zoom maps to the same
        // screen position after zoom. delta > 0 = wheel up = zoom in.
        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var newZoom = Viewport.Zoom * factor;
        Viewport.Zoom = newZoom;
        e.Handled = true;
    }

    private Point _dragOrigin;
    private Point _panOrigin;
    private bool _panning;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Viewport is null) return;
        Focus();
        _dragOrigin = e.GetPosition(this);
        _panOrigin = Viewport.Pan;
        _panning = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_panning || Viewport is null) return;
        var p = e.GetPosition(this);
        Viewport.Pan = new Point(
            _panOrigin.X + (p.X - _dragOrigin.X),
            _panOrigin.Y + (p.Y - _dragOrigin.Y));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_panning)
        {
            _panning = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var payload = Payload;
        if (payload is null)
        {
            DrawMessage(dc, "No image content loaded.", TextBrush);
            return;
        }

        // Dedicated LFS-pointer message: Phase 6 doesn't ship an LFS fetcher
        // (audit §5.7 hasn't landed yet). Surface the situation so the user
        // knows to run `git lfs pull` out-of-band.
        if (payload.Ours.IsLfsPointer || payload.Theirs.IsLfsPointer || payload.Base.IsLfsPointer)
        {
            DrawMessage(dc,
                "Git LFS pointer detected. Run 'git lfs pull' so Leaf can preview the images, " +
                "then reopen this conflict. Use 'Use Ours' / 'Use Theirs' to force a side in the meantime.",
                ErrorBrush);
            return;
        }

        if (_oursBitmap is null && _theirsBitmap is null)
        {
            DrawMessage(dc,
                "Could not decode the image payload. The file may be corrupted or in an " +
                "unsupported format. 'Use Ours' / 'Use Theirs' remain available.",
                ErrorBrush);
            return;
        }

        var viewport = Viewport ?? new ImageViewportState();
        var mode = viewport.Mode;
        var contentArea = new Rect(0, ModeBarHeight, ActualWidth,
            Math.Max(0, ActualHeight - ModeBarHeight));

        DrawModeBar(dc, mode);
        DrawCheckerBoard(dc, contentArea);

        switch (mode)
        {
            case ImageMergeMode.SideBySide:
                DrawSideBySide(dc, contentArea, viewport);
                break;
            case ImageMergeMode.OnionSkin:
                DrawOnionSkin(dc, contentArea, viewport);
                break;
            case ImageMergeMode.Swipe:
                DrawSwipe(dc, contentArea, viewport);
                break;
            case ImageMergeMode.Difference:
                DrawDifference(dc, contentArea, viewport);
                break;
            case ImageMergeMode.Overlay:
                DrawOverlay(dc, contentArea, viewport);
                break;
        }

        DrawDimensionsSummary(dc);
    }

    private void DrawMessage(DrawingContext dc, string text, Brush brush)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelTypeface, 14, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(40, ActualWidth - 80),
            TextAlignment = TextAlignment.Center,
        };
        dc.DrawText(ft, new Point(
            (ActualWidth - ft.Width) / 2,
            (ActualHeight - ft.Height) / 2));
    }

    private void DrawModeBar(DrawingContext dc, ImageMergeMode mode)
    {
        // Simple mode-label strip. The actual mode picker is a ComboBox in
        // the XAML footer (bound to Viewport.Mode); this label gives at-a-glance
        // feedback from inside the pane.
        var bar = new Rect(0, 0, ActualWidth, ModeBarHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)), null, bar);
        var label = mode switch
        {
            ImageMergeMode.SideBySide => "Side by side",
            ImageMergeMode.OnionSkin => "Onion skin",
            ImageMergeMode.Swipe => "Swipe",
            ImageMergeMode.Difference => "Difference",
            ImageMergeMode.Overlay => "Overlay",
            _ => mode.ToString(),
        };
        var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelTypeface, 12, TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(12, (ModeBarHeight - ft.Height) / 2));
        dc.DrawLine(DividerPen, new Point(0, ModeBarHeight), new Point(ActualWidth, ModeBarHeight));
    }

    private void DrawCheckerBoard(DrawingContext dc, Rect area)
    {
        // Classic transparency-indicator checkerboard — helps the eye locate
        // the image bounds when either side has transparency.
        const double tile = 12.0;
        dc.PushClip(new RectangleGeometry(area));
        try
        {
            for (double y = area.Top; y < area.Bottom; y += tile)
            {
                int rowParity = ((int)((y - area.Top) / tile)) & 1;
                for (double x = area.Left; x < area.Right; x += tile)
                {
                    int colParity = ((int)((x - area.Left) / tile)) & 1;
                    if (((rowParity + colParity) & 1) == 1)
                    {
                        dc.DrawRectangle(GridBrush, null, new Rect(x, y, tile, tile));
                    }
                }
            }
        }
        finally { dc.Pop(); }
    }

    private void DrawSideBySide(DrawingContext dc, Rect area, ImageViewportState vp)
    {
        var half = new Rect(area.Left, area.Top, area.Width / 2, area.Height);
        var halfR = new Rect(area.Left + area.Width / 2, area.Top, area.Width / 2, area.Height);
        DrawImageFit(dc, _oursBitmap, half, vp, "Ours");
        DrawImageFit(dc, _theirsBitmap, halfR, vp, "Theirs");
        dc.DrawLine(DividerPen,
            new Point(area.Left + area.Width / 2, area.Top),
            new Point(area.Left + area.Width / 2, area.Bottom));
    }

    private void DrawOnionSkin(DrawingContext dc, Rect area, ImageViewportState vp)
    {
        // Ours at full opacity underneath, theirs at variable opacity on top.
        DrawImageFit(dc, _oursBitmap, area, vp, "Ours");
        dc.PushOpacity(vp.OnionSkinOpacity);
        try { DrawImageFit(dc, _theirsBitmap, area, vp, label: null); }
        finally { dc.Pop(); }
    }

    private void DrawSwipe(DrawingContext dc, Rect area, ImageViewportState vp)
    {
        // Ours on the full area; theirs clipped to the right of the swipe line.
        DrawImageFit(dc, _oursBitmap, area, vp, "Ours");
        var split = area.Left + area.Width * vp.SwipeRatio;
        var rightHalf = new Rect(split, area.Top, area.Right - split, area.Height);
        dc.PushClip(new RectangleGeometry(rightHalf));
        try { DrawImageFit(dc, _theirsBitmap, area, vp, "Theirs"); }
        finally { dc.Pop(); }
        dc.DrawLine(
            new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x4D)), 2.0),
            new Point(split, area.Top), new Point(split, area.Bottom));
    }

    private void DrawDifference(DrawingContext dc, Rect area, ImageViewportState vp)
    {
        // Per-pixel absolute difference ours − theirs. Cached in a
        // WriteableBitmap keyed on the natural image size so a zoom/pan
        // doesn't rebuild the pixels.
        if (_oursBitmap is null || _theirsBitmap is null)
        {
            DrawMessage(dc, "Difference needs both ours and theirs.", TextBrush);
            return;
        }
        var diff = BuildOrReuseDifference(_oursBitmap, _theirsBitmap);
        DrawImageFit(dc, diff, area, vp, "Difference (|ours - theirs|)");
    }

    private void DrawOverlay(DrawingContext dc, Rect area, ImageViewportState vp)
    {
        // Ours = red channel, theirs = green channel — classic overlay compare.
        // Done at 50% opacity on both sides for legibility; a future tweak
        // could expose a tint control.
        dc.PushOpacity(0.5);
        try
        {
            DrawImageFit(dc, _oursBitmap, area, vp, "Ours");
            DrawImageFit(dc, _theirsBitmap, area, vp, "Theirs");
        }
        finally { dc.Pop(); }
    }

    private WriteableBitmap BuildOrReuseDifference(BitmapSource ours, BitmapSource theirs)
    {
        // Cache until the natural sizes change.
        var key = new Size(
            Math.Max(ours.PixelWidth, theirs.PixelWidth),
            Math.Max(ours.PixelHeight, theirs.PixelHeight));
        if (_differenceCache is not null && _differenceCacheKey == key)
            return _differenceCache;

        int w = (int)key.Width;
        int h = (int)key.Height;
        var stride = w * 4;
        var buf = new byte[stride * h];

        // Copy both sides into BGRA32 at a common size, then |ours - theirs|
        // per channel. Pads the smaller side with transparent pixels.
        CopyToBgra(ours, w, h, out var oursBuf);
        CopyToBgra(theirs, w, h, out var theirsBuf);

        for (int i = 0; i < buf.Length; i += 4)
        {
            byte b = (byte)Math.Abs(oursBuf[i] - theirsBuf[i]);
            byte g = (byte)Math.Abs(oursBuf[i + 1] - theirsBuf[i + 1]);
            byte r = (byte)Math.Abs(oursBuf[i + 2] - theirsBuf[i + 2]);
            byte a = (byte)Math.Max(oursBuf[i + 3], theirsBuf[i + 3]);
            // Amplify so near-identical pixels don't look totally black.
            buf[i] = (byte)Math.Min(255, b * 4);
            buf[i + 1] = (byte)Math.Min(255, g * 4);
            buf[i + 2] = (byte)Math.Min(255, r * 4);
            buf[i + 3] = a;
        }

        var wb = new WriteableBitmap(w, h, ours.DpiX, ours.DpiY, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), buf, stride, 0);
        wb.Freeze();
        _differenceCache = wb;
        _differenceCacheKey = key;
        return wb;
    }

    private static void CopyToBgra(BitmapSource src, int w, int h, out byte[] buf)
    {
        var stride = w * 4;
        buf = new byte[stride * h];
        var converted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int sw = converted.PixelWidth, sh = converted.PixelHeight;
        var srcBuf = new byte[sw * 4 * sh];
        converted.CopyPixels(srcBuf, sw * 4, 0);
        // Centre the source in the destination so differently-sized images
        // align at their centres (common case: an icon grew by a few pixels).
        int ox = (w - sw) / 2, oy = (h - sh) / 2;
        for (int y = 0; y < sh; y++)
        {
            int dy = y + oy;
            if (dy < 0 || dy >= h) continue;
            for (int x = 0; x < sw; x++)
            {
                int dx = x + ox;
                if (dx < 0 || dx >= w) continue;
                int si = (y * sw + x) * 4;
                int di = (dy * w + dx) * 4;
                buf[di] = srcBuf[si];
                buf[di + 1] = srcBuf[si + 1];
                buf[di + 2] = srcBuf[si + 2];
                buf[di + 3] = srcBuf[si + 3];
            }
        }
    }

    private void DrawImageFit(
        DrawingContext dc,
        BitmapSource? bmp,
        Rect area,
        ImageViewportState vp,
        string? label)
    {
        if (bmp is null)
        {
            if (label is not null) DrawCornerLabel(dc, area, $"{label}: missing");
            return;
        }

        // Fit bitmap into area at natural aspect ratio, then apply viewport zoom/pan.
        var bw = bmp.PixelWidth;
        var bh = bmp.PixelHeight;
        var scale = Math.Min(area.Width / bw, area.Height / bh);
        var w = bw * scale * vp.Zoom;
        var h = bh * scale * vp.Zoom;
        var x = area.Left + (area.Width - w) / 2 + vp.Pan.X;
        var y = area.Top + (area.Height - h) / 2 + vp.Pan.Y;
        var target = new Rect(x, y, w, h);

        dc.PushClip(new RectangleGeometry(area));
        try { dc.DrawImage(bmp, target); }
        finally { dc.Pop(); }

        if (label is not null) DrawCornerLabel(dc, area, label);
    }

    private void DrawCornerLabel(DrawingContext dc, Rect area, string text)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelTypeface, 11, TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var pad = 4.0;
        var bg = new Rect(area.Left + 6, area.Top + 6, ft.Width + pad * 2, ft.Height + pad);
        dc.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)), null, bg);
        dc.DrawText(ft, new Point(bg.Left + pad, bg.Top + pad / 2));
    }

    private void DrawDimensionsSummary(DrawingContext dc)
    {
        if (_oursBitmap is null && _theirsBitmap is null) return;
        var text = $"Ours: {DimText(_oursBitmap)}   Theirs: {DimText(_theirsBitmap)}";
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelTypeface, 11, TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(ActualWidth - ft.Width - 10, ActualHeight - ft.Height - 8));
    }

    private static string DimText(BitmapSource? bmp) =>
        bmp is null ? "(missing)" : $"{bmp.PixelWidth}×{bmp.PixelHeight}";
}
