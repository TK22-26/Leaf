#nullable enable
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

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
/// connection lines hit exact endpoints. This class is the single source of
/// truth — panes subscribe to <see cref="PropertyChanged"/> and re-render when
/// any value changes.
/// </para>
/// <para>
/// The type is not a service-locator helper — callers inject instances via DI
/// (<see cref="Leaf.TextEdit.IMergePaneGlyphLayout"/> is planned for Phase 2c)
/// or hold a shared reference captured from the parent merge-editor view model.
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
        new("Consolas, Courier New, monospace");

    /// <summary>Default font size in device-independent pixels. Tuned to match Leaf's existing diff/merge panes.</summary>
    public const double DefaultFontSize = 12.5 * 96.0 / 72.0; // 12.5pt → px

    /// <summary>Default tab width in character columns (each column is one <see cref="AdvanceWidth"/> wide).</summary>
    public const int DefaultTabSize = 4;

    private FontFamily _fontFamily = DefaultFontFamily;
    private double _fontSize = DefaultFontSize;
    private FontWeight _fontWeight = FontWeights.Normal;
    private FontStyle _fontStyle = FontStyles.Normal;
    private FontStretch _fontStretch = FontStretches.Normal;
    private int _tabSize = DefaultTabSize;

    /// <summary>Monospaced font family. Non-monospaced fonts will break pane alignment.</summary>
    public FontFamily FontFamily
    {
        get => _fontFamily;
        set
        {
            if (_fontFamily.Equals(value)) return;
            _fontFamily = value ?? throw new ArgumentNullException(nameof(value));
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

    // Derived metrics — computed lazily and invalidated on font changes.
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
    /// Height of a single line in device-independent pixels. Equal to the font's
    /// line spacing at the current size, rounded to an integer to avoid
    /// sub-pixel-offset bluriness on pane alignment.
    /// </summary>
    public double LineHeight
    {
        get
        {
            if (_lineHeight is null)
            {
                var raw = Typeface.FontFamily.LineSpacing * _fontSize;
                _lineHeight = Math.Ceiling(raw);
            }
            return _lineHeight.Value;
        }
    }

    /// <summary>
    /// Advance width of a single space character (a proxy for "one character
    /// column" in a monospaced font). Derived by measuring the typeface's
    /// glyph metrics.
    /// </summary>
    public double AdvanceWidth
    {
        get
        {
            if (_advanceWidth is null)
            {
                var ft = BuildFormattedText(" ");
                _advanceWidth = ft.WidthIncludingTrailingWhitespace;
            }
            return _advanceWidth.Value;
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
            if (_baseline is null)
            {
                var ft = BuildFormattedText(" ");
                _baseline = ft.Baseline;
            }
            return _baseline.Value;
        }
    }

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
        return (int)Math.Floor(yOffset / LineHeight) + 1;
    }

    /// <summary>
    /// Build a <see cref="FormattedText"/> for <paramref name="text"/> under
    /// this layout's typography. Useful for custom-render panes that need to
    /// measure and draw runs.
    /// </summary>
    public FormattedText BuildFormattedText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // NumberSubstitution and pixels-per-dip = 1.0 are chosen to match the
        // vendored AvalonEdit TextView — ensures pane glyph rendering is
        // pixel-identical to the embedded editor.
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            Typeface,
            _fontSize,
            System.Windows.Media.Brushes.Black,
            pixelsPerDip: 1.0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
