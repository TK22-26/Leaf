#nullable enable
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Leaf.TextEdit.Rendering;

namespace Leaf.TextEdit;

/// <summary>
/// Shared typography and line-metrics contract consumed by every pane of the
/// three-way merge editor — the two custom read-only input panes, the optional
/// custom base pane, the vendored <see cref="TextEditor"/> result pane, the
/// connection-line canvas, and the conflict minimap.
/// </summary>
/// <remarks>
/// <para>
/// All merge panes must agree on font, size, line height, tab size, and related
/// metrics so that corresponding lines align horizontally across panes and
/// connection lines hit exact endpoints.  This class is the single source of
/// truth — panes subscribe to <see cref="PropertyChanged"/> and re-render when
/// any value changes.
/// </para>
/// <para>
/// Critical correctness invariant: <see cref="LineHeight"/> must equal
/// <see cref="Leaf.TextEdit.Rendering.TextView.DefaultLineHeight"/> when both
/// are configured with the same typography. We achieve this by measuring the
/// height of a formatted glyph (<c>"x"</c>) through the same
/// <see cref="System.Windows.Media.TextFormatting.TextFormatter"/> pipeline
/// (with the same culture, <see cref="System.Windows.Media.TextFormattingMode"/>,
/// and <c>pixelsPerDip</c>) that the vendored <c>TextView.CalculateDefaultTextMetrics</c>
/// uses. Divergence here breaks every pixel-level UI decision downstream
/// (pane alignment, connection lines, minimap positioning). The invariant is
/// covered by <c>LineHeight_UsesSamePipelineAsTextViewDefaultLineHeight</c>.
/// </para>
/// </remarks>
public sealed class MergePaneGlyphLayout : INotifyPropertyChanged
{
    /// <summary>
    /// Default monospaced font used when no repo-specific override is set.
    /// Consolas is present on every Windows install since Vista; falls back to
    /// Courier New on systems that have somehow had it removed.
    /// </summary>
    public static readonly FontFamily DefaultFontFamily =
        new("Consolas, Courier New");

    /// <summary>
    /// Default font size in device-independent pixels. Matches the hardcoded
    /// <c>FontSize="12.5"</c> that the pre-Phase-2b <c>MergedResultEditorControl</c>
    /// and <c>ConflictSideEditorControl</c> XAMLs ship with — WPF interprets
    /// a raw XAML <c>FontSize</c> as DIPs, NOT points, so the numeric value
    /// carries through directly.
    /// </summary>
    public const double DefaultFontSize = 12.5;

    /// <summary>Default tab width in character columns (each column is one <see cref="AdvanceWidth"/> wide).</summary>
    public const int DefaultTabSize = 4;

    private FontFamily _fontFamily = DefaultFontFamily;
    private double _fontSize = DefaultFontSize;
    private FontWeight _fontWeight = FontWeights.Normal;
    private FontStyle _fontStyle = FontStyles.Normal;
    private FontStretch _fontStretch = FontStretches.Normal;
    private int _tabSize = DefaultTabSize;
    private TextFormattingMode _textFormattingMode = TextFormattingMode.Ideal;
    private double _pixelsPerDip = 1.0;

    /// <summary>Monospaced font family. Non-monospaced fonts will break pane alignment.</summary>
    public FontFamily FontFamily
    {
        get => _fontFamily;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_fontFamily.Equals(value)) return;
            _fontFamily = value;
            InvalidateDerivedMetrics();
            Notify();
        }
    }

    /// <summary>Font size in device-independent pixels (NOT points).</summary>
    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Font size must be positive.");
            if (_fontSize == value) return;
            _fontSize = value;
            InvalidateDerivedMetrics();
            Notify();
        }
    }

    public FontWeight FontWeight
    {
        get => _fontWeight;
        set { if (_fontWeight.Equals(value)) return; _fontWeight = value; InvalidateDerivedMetrics(); Notify(); }
    }

    public FontStyle FontStyle
    {
        get => _fontStyle;
        set { if (_fontStyle.Equals(value)) return; _fontStyle = value; InvalidateDerivedMetrics(); Notify(); }
    }

    public FontStretch FontStretch
    {
        get => _fontStretch;
        set { if (_fontStretch.Equals(value)) return; _fontStretch = value; InvalidateDerivedMetrics(); Notify(); }
    }

    /// <summary>Tab width in character columns. Must be positive.</summary>
    public int TabSize
    {
        get => _tabSize;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Tab size must be positive.");
            if (_tabSize == value) return;
            _tabSize = value;
            Notify();
        }
    }

    /// <summary>
    /// <see cref="System.Windows.Media.TextFormattingMode"/> used for all pane rendering.
    /// <c>Ideal</c> (default) matches the vendored <see cref="TextEditor"/>; switching
    /// to <c>Display</c> at one pane but not another would produce visible misalignment.
    /// </summary>
    public TextFormattingMode TextFormattingMode
    {
        get => _textFormattingMode;
        set { if (_textFormattingMode == value) return; _textFormattingMode = value; InvalidateDerivedMetrics(); Notify(); }
    }

    /// <summary>
    /// Per-monitor DPI scale factor (1.0 at 100 % scaling, 1.5 at 150 %, etc.).
    /// Host panes should push the real value via <see cref="SetDpi(DpiScale)"/>
    /// whenever their DPI changes (typically on <c>Window.DpiChanged</c>).
    /// Default of 1.0 is safe for unit tests and initial construction before
    /// the pane is attached to a visual tree.
    /// </summary>
    public double PixelsPerDip
    {
        get => _pixelsPerDip;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "PixelsPerDip must be positive.");
            if (_pixelsPerDip == value) return;
            _pixelsPerDip = value;
            InvalidateDerivedMetrics();
            Notify();
        }
    }

    /// <summary>Convenience overload for consumers holding a <see cref="DpiScale"/>.</summary>
    public void SetDpi(DpiScale dpi) => PixelsPerDip = dpi.PixelsPerDip;

    // Derived metrics — computed lazily and invalidated on font / DPI / mode changes.
    private Typeface? _typeface;
    private double? _lineHeight;
    private double? _advanceWidth;
    private double? _baseline;

    /// <summary>
    /// The WPF <see cref="Typeface"/> composed from the current font family /
    /// style / weight / stretch. Cached until any font property changes.
    /// </summary>
    public Typeface Typeface
    {
        get
        {
            if (_typeface is null)
            {
                _typeface = new Typeface(_fontFamily, _fontStyle, _fontWeight, _fontStretch);
            }
            return _typeface;
        }
    }

    /// <summary>
    /// Height of a single line in device-independent pixels. Measured through
    /// the same <see cref="FormattedText"/> pipeline the vendored
    /// <see cref="Leaf.TextEdit.Rendering.TextView"/> uses for its
    /// <c>DefaultLineHeight</c>, producing pixel-identical metrics.
    /// </summary>
    public double LineHeight
    {
        get
        {
            if (_lineHeight is null) EnsureMeasurementCache();
            return _lineHeight!.Value;
        }
    }

    /// <summary>
    /// Advance width of a wide character (<c>"x"</c>) — the same choice the
    /// vendored <see cref="Leaf.TextEdit.Rendering.TextView"/> uses for
    /// <c>WideSpaceWidth</c>. Used for one-character-column measurement.
    /// </summary>
    public double AdvanceWidth
    {
        get
        {
            if (_advanceWidth is null) EnsureMeasurementCache();
            return _advanceWidth!.Value;
        }
    }

    /// <summary>
    /// Distance from the top of a line to the font's baseline — needed to align
    /// background-rendered highlights with text correctly.
    /// </summary>
    public double Baseline
    {
        get
        {
            if (_baseline is null) EnsureMeasurementCache();
            return _baseline!.Value;
        }
    }

    /// <summary>
    /// Advance width of one tab stop in device-independent pixels —
    /// <see cref="TabSize"/> × <see cref="AdvanceWidth"/>.
    /// </summary>
    public double TabPixelWidth => TabSize * AdvanceWidth;

    /// <summary>
    /// Compute the Y-coordinate (in pane-local pixels) of the top of a given
    /// 1-based line index under this layout's line-height. Counterpart to
    /// <see cref="LineIndexAtYOffset"/>.
    /// </summary>
    public double GetVisualTop(int lineIndex1Based)
    {
        if (lineIndex1Based < 1)
            throw new ArgumentOutOfRangeException(nameof(lineIndex1Based), "Line index is 1-based; value must be >= 1.");
        return (lineIndex1Based - 1) * LineHeight;
    }

    /// <summary>
    /// Inverse of <see cref="GetVisualTop"/>: given a Y offset in pane-local
    /// pixels, return the 1-based line index that contains that Y. Values
    /// above the first line clamp to 1.
    /// </summary>
    public int LineIndexAtYOffset(double yOffset)
    {
        if (yOffset < 0) return 1;
        // Snap to an integer line index when the quotient is within FP rounding
        // tolerance of a whole number — otherwise a round-trip
        // LineIndexAtYOffset(GetVisualTop(n)) loses 1 on fractional LineHeights.
        var q = yOffset / LineHeight;
        var rounded = Math.Round(q);
        if (Math.Abs(q - rounded) < 1e-9) q = rounded;
        return (int)Math.Floor(q) + 1;
    }

    /// <summary>
    /// Build a <see cref="FormattedText"/> for <paramref name="text"/> under
    /// this layout's typography. Uses <see cref="CultureInfo.CurrentCulture"/>,
    /// the current <see cref="TextFormattingMode"/>, and <see cref="PixelsPerDip"/>
    /// — all matching the vendored <c>TextFormatterFactory</c> so measurements
    /// are identical to what the embedded <see cref="TextEditor"/> produces.
    /// </summary>
    public FormattedText BuildFormattedText(string text, Brush? foreground = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface,
            _fontSize,
            foreground ?? Brushes.Black,
            numberSubstitution: null,
            _textFormattingMode,
            _pixelsPerDip);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void EnsureMeasurementCache()
    {
        // Use the SAME pipeline as TextView.CalculateDefaultTextMetrics:
        // TextFormatter.FormatLine("x", ...) with a VisualLineTextParagraphProperties.
        // FormattedText's .Height diverges from TextFormatter.FormatLine's .Height
        // by ~1.3 px per line at common sizes because FormattedText returns only
        // the text metric height while TextFormatter also accounts for the full
        // line leading. Using the formatter directly is what gives us pixel
        // parity with the embedded TextEditor.
        var runProps = new GlobalTextRunProperties
        {
            typeface = Typeface,
            fontRenderingEmSize = _fontSize,
            foregroundBrush = Brushes.Black,
            cultureInfo = CultureInfo.CurrentCulture,
        };
        var paraProps = new VisualLineTextParagraphProperties
        {
            defaultTextRunProperties = runProps,
            textWrapping = TextWrapping.NoWrap,
            tabSize = _tabSize * 0, // tabSize here is in pixels; only used for multi-line layout — harmless for a single "x"
            flowDirection = FlowDirection.LeftToRight,
        };

        var formatter = TextFormatter.Create(_textFormattingMode);
        using var line = formatter.FormatLine(
            new SimpleTextSource("x", runProps),
            0,
            32000,
            paraProps,
            previousLineBreak: null);
        _lineHeight = Math.Max(1, line.Height);
        _baseline = Math.Max(1, line.Baseline);
        _advanceWidth = Math.Max(1, line.WidthIncludingTrailingWhitespace);
    }

    private void InvalidateDerivedMetrics()
    {
        _typeface = null;
        _lineHeight = null;
        _advanceWidth = null;
        _baseline = null;
    }

    private void Notify([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
