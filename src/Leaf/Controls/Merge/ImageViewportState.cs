#nullable enable
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Leaf.Controls.Merge;

/// <summary>
/// Shared zoom + pan state across all image-conflict panes so side-by-side,
/// swipe, and overlay modes stay coordinated. One instance per conflict; each
/// pane binds to it and updates the same backing values.
/// </summary>
/// <remarks>
/// Implements <see cref="INotifyPropertyChanged"/> directly rather than going
/// through CommunityToolkit.Mvvm so the control can live in the Controls
/// namespace without taking an MVVM dependency. The two or three properties
/// it exposes don't benefit from code generation.
/// </remarks>
public sealed class ImageViewportState : INotifyPropertyChanged
{
    private double _zoom = 1.0;
    private Point _pan;
    private ImageMergeMode _mode = ImageMergeMode.SideBySide;
    private double _swipeRatio = 0.5;
    private double _onionSkinOpacity = 0.5;

    /// <summary>Unit-relative zoom (1.0 = natural pixel size).</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetField(ref _zoom, Math.Max(0.05, Math.Min(32.0, value)));
    }

    /// <summary>Pan offset in viewport-pixel units relative to the natural origin.</summary>
    public Point Pan
    {
        get => _pan;
        set => SetField(ref _pan, value);
    }

    /// <summary>Which of the five modes is currently active.</summary>
    public ImageMergeMode Mode
    {
        get => _mode;
        set => SetField(ref _mode, value);
    }

    /// <summary>
    /// Swipe-mode split ratio (0 = all ours, 1 = all theirs). Drives the
    /// clipping geometry of the overlay in <see cref="ImageMergeMode.Swipe"/>.
    /// </summary>
    public double SwipeRatio
    {
        get => _swipeRatio;
        set => SetField(ref _swipeRatio, Math.Max(0.0, Math.Min(1.0, value)));
    }

    /// <summary>Onion-skin opacity of the "theirs" layer on top of "ours".</summary>
    public double OnionSkinOpacity
    {
        get => _onionSkinOpacity;
        set => SetField(ref _onionSkinOpacity, Math.Max(0.0, Math.Min(1.0, value)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// The five rendering modes from the plan. Plus a "lfs missing" fallback
/// surfaced via the <see cref="ImageConflictPane"/> when it can't decode.
/// </summary>
public enum ImageMergeMode
{
    SideBySide,
    OnionSkin,
    Swipe,
    Difference,
    Overlay,
}
