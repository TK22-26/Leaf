using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Converters;

namespace Leaf.Controls;

/// <summary>
/// A round identicon avatar derived from a commit's <c>AvatarKey</c>
/// (email or name). Centralises the Border + clipped Image idiom that
/// otherwise repeats across the commit detail card, the bisect detail
/// header, the co-author chip stack, and several other surfaces.
/// Size is parameterised; clip + image-inset radii are computed from
/// the requested size so a single property drives the whole layout.
/// </summary>
public partial class CommitIdenticon : UserControl
{
    private static readonly IdenticonConverter _identiconConverter = new();

    public CommitIdenticon()
    {
        InitializeComponent();

        // Initial-state bug: OnSizeChanged is the only place that
        // populates ImageClip / Radius / ImageSize, but it only fires
        // when the property changes value. A consumer that leaves Size
        // at the default 20.0 never triggers it — the read-only DPs
        // hold their initial values, ImageClip stays null, and the
        // Image renders unclipped (a 18x18 square inside the round
        // Border, looking like a "square under the circle frame").
        // RecomputeAvatarSource has the same problem: AvatarKey may
        // be set in XAML before the binding pipeline notifies the
        // callback, so the source is null on first render until the
        // user moves to a different commit and back.
        RecomputeGeometry();
        RecomputeAvatarSource();
    }

    /// <summary>
    /// Cache key for the identicon palette — typically the commit
    /// author's email (preferred for stability across rename) or
    /// the author's name (fallback). The IdenticonConverter hashes
    /// this into a deterministic palette.
    /// </summary>
    public static readonly DependencyProperty AvatarKeyProperty =
        DependencyProperty.Register(
            nameof(AvatarKey),
            typeof(string),
            typeof(CommitIdenticon),
            new PropertyMetadata(string.Empty, OnAvatarKeyChanged));

    public string AvatarKey
    {
        get => (string)GetValue(AvatarKeyProperty);
        set => SetValue(AvatarKeyProperty, value);
    }

    /// <summary>
    /// Outer diameter of the avatar in DIPs. Drives Border width/height,
    /// inner Image dimensions, corner radius, and clip geometry — set
    /// once and the whole control rescales.
    /// </summary>
    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(CommitIdenticon),
            new PropertyMetadata(20.0, OnSizeChanged));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Inner image diameter — Size minus the 1px border ring on each
    /// side. Read-only DP for the XAML binding.
    /// </summary>
    public static readonly DependencyPropertyKey ImageSizePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ImageSize),
            typeof(double),
            typeof(CommitIdenticon),
            new PropertyMetadata(18.0));

    public static readonly DependencyProperty ImageSizeProperty = ImageSizePropertyKey.DependencyProperty;

    public double ImageSize => (double)GetValue(ImageSizeProperty);

    /// <summary>
    /// Corner radius for the outer Border (always Size/2 for a circle).
    /// Read-only DP for the XAML binding.
    /// </summary>
    public static readonly DependencyPropertyKey RadiusPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Radius),
            typeof(double),
            typeof(CommitIdenticon),
            new PropertyMetadata(10.0));

    public static readonly DependencyProperty RadiusProperty = RadiusPropertyKey.DependencyProperty;

    public double Radius => (double)GetValue(RadiusProperty);

    /// <summary>
    /// Ellipse clip geometry sized to the inner image. Read-only DP
    /// so the XAML can bind without rebuilding the geometry on every
    /// render pass.
    /// </summary>
    public static readonly DependencyPropertyKey ImageClipPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ImageClip),
            typeof(Geometry),
            typeof(CommitIdenticon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ImageClipProperty = ImageClipPropertyKey.DependencyProperty;

    public Geometry? ImageClip => (Geometry?)GetValue(ImageClipProperty);

    /// <summary>
    /// Materialised identicon image — produced by piping
    /// <see cref="AvatarKey"/> through <see cref="IdenticonConverter"/>
    /// with <see cref="ImageSize"/> as the converter parameter. Read-
    /// only DP so the XAML can bind it as the Image source.
    /// </summary>
    public static readonly DependencyPropertyKey AvatarSourcePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(AvatarSource),
            typeof(object),
            typeof(CommitIdenticon),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AvatarSourceProperty = AvatarSourcePropertyKey.DependencyProperty;

    public object? AvatarSource => GetValue(AvatarSourceProperty);

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CommitIdenticon ic) return;
        ic.RecomputeGeometry();
        ic.RecomputeAvatarSource();
    }

    private static void OnAvatarKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CommitIdenticon ic) ic.RecomputeAvatarSource();
    }

    private void RecomputeGeometry()
    {
        var size = Size;
        var imgSize = Math.Max(0, size - 2);
        SetValue(ImageSizePropertyKey, imgSize);
        SetValue(RadiusPropertyKey, size / 2.0);
        var c = imgSize / 2.0;
        SetValue(ImageClipPropertyKey, (Geometry)new EllipseGeometry(new Point(c, c), c, c));
    }

    private void RecomputeAvatarSource()
    {
        var src = _identiconConverter.Convert(AvatarKey, typeof(object), ImageSize, System.Globalization.CultureInfo.InvariantCulture);
        SetValue(AvatarSourcePropertyKey, src);
    }
}
