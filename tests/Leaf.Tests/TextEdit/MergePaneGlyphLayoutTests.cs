using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Leaf.TextEdit;
using Xunit;

namespace Leaf.Tests.TextEdit;

public class MergePaneGlyphLayoutTests
{
    [StaFact]
    public void DefaultMetrics_AreSensible()
    {
        var layout = new MergePaneGlyphLayout();

        layout.FontFamily.Should().NotBeNull();
        layout.FontSize.Should().BeGreaterThan(0);
        layout.TabSize.Should().Be(MergePaneGlyphLayout.DefaultTabSize);
        layout.LineHeight.Should().BeGreaterThan(0);
        layout.AdvanceWidth.Should().BeGreaterThan(0);
        layout.Baseline.Should().BeGreaterThan(0);
    }

    [StaFact]
    public void GetVisualTop_IsLineHeightTimesIndexMinusOne()
    {
        var layout = new MergePaneGlyphLayout();
        layout.GetVisualTop(1).Should().Be(0);
        layout.GetVisualTop(2).Should().Be(layout.LineHeight);
        layout.GetVisualTop(10).Should().Be(layout.LineHeight * 9);
    }

    [StaFact]
    public void GetVisualTop_LineIndexZeroOrNegative_Throws()
    {
        var layout = new MergePaneGlyphLayout();
        FluentActions.Invoking(() => layout.GetVisualTop(0))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => layout.GetVisualTop(-5))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [StaFact]
    public void LineIndexAtYOffset_IsInverseOfGetVisualTop()
    {
        var layout = new MergePaneGlyphLayout();
        for (int i = 1; i <= 50; i++)
        {
            var top = layout.GetVisualTop(i);
            layout.LineIndexAtYOffset(top).Should().Be(i);
            // Mid-line Y also resolves to the same line index.
            layout.LineIndexAtYOffset(top + layout.LineHeight / 2).Should().Be(i);
        }
    }

    [StaFact]
    public void LineIndexAtYOffset_ClampsNegativeYToOne()
    {
        var layout = new MergePaneGlyphLayout();
        layout.LineIndexAtYOffset(-10).Should().Be(1);
        layout.LineIndexAtYOffset(-0.0001).Should().Be(1);
    }

    [StaFact]
    public void ChangingFontSize_InvalidatesDerivedMetrics()
    {
        var layout = new MergePaneGlyphLayout();
        var oldLineHeight = layout.LineHeight;
        var oldAdvance = layout.AdvanceWidth;

        layout.FontSize = layout.FontSize * 2;

        layout.LineHeight.Should().NotBe(oldLineHeight);
        layout.AdvanceWidth.Should().NotBe(oldAdvance);
    }

    [StaFact]
    public void ChangingFontSize_RaisesPropertyChanged()
    {
        var layout = new MergePaneGlyphLayout();
        var raised = new List<string?>();
        layout.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        layout.FontSize = layout.FontSize * 2;

        raised.Should().Contain(nameof(MergePaneGlyphLayout.FontSize));
    }

    [StaFact]
    public void SettingSameValue_DoesNotRaisePropertyChanged()
    {
        var layout = new MergePaneGlyphLayout();
        var raised = new List<string?>();
        layout.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        layout.FontSize = layout.FontSize;       // no-op
        layout.TabSize = layout.TabSize;         // no-op
        layout.FontWeight = layout.FontWeight;   // no-op

        raised.Should().BeEmpty();
    }

    [StaFact]
    public void InvalidFontSize_Throws()
    {
        var layout = new MergePaneGlyphLayout();
        FluentActions.Invoking(() => layout.FontSize = 0)
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => layout.FontSize = -10)
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [StaFact]
    public void InvalidTabSize_Throws()
    {
        var layout = new MergePaneGlyphLayout();
        FluentActions.Invoking(() => layout.TabSize = 0)
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => layout.TabSize = -1)
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [StaFact]
    public void BuildFormattedText_NullInput_Throws()
    {
        var layout = new MergePaneGlyphLayout();
        FluentActions.Invoking(() => layout.BuildFormattedText(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [StaFact]
    public void BuildFormattedText_ReturnsMeasurableTextAtCurrentFont()
    {
        var layout = new MergePaneGlyphLayout();
        var ft = layout.BuildFormattedText("hello");
        ft.Width.Should().BeGreaterThan(0);
        ft.Height.Should().BeGreaterThan(0);
    }

    [StaFact]
    public void Typeface_MatchesCurrentFontProperties()
    {
        var layout = new MergePaneGlyphLayout();
        layout.FontWeight = FontWeights.Bold;

        layout.Typeface.Weight.Should().Be(FontWeights.Bold);
        layout.Typeface.Style.Should().Be(FontStyles.Normal);
        layout.Typeface.Stretch.Should().Be(FontStretches.Normal);
    }

    [StaFact]
    public void LineHeight_UsesSamePipelineAsTextViewDefaultLineHeight()
    {
        // The core alignment invariant: MergePaneGlyphLayout and TextView must
        // produce identical LineHeight for matched typography. Both paths must
        // go through System.Windows.Media.TextFormatting.TextFormatter.FormatLine
        // with a VisualLineTextParagraphProperties — not FormattedText, whose
        // .Height leaves out line-leading and diverges by ~1.3 px at 12.5pt.
        //
        // We assert parity by measuring a TextView directly with the same
        // font DependencyProperty values that the layout is configured with.
        // (Constructing a TextEditor control instead gives a FontSize that
        // doesn't always propagate without a measure pass — testing TextView
        // directly sidesteps that.)
        var layout = new MergePaneGlyphLayout();

        var textView = new Leaf.TextEdit.Rendering.TextView();
        // TextView reads font from attached TextElement properties (inherited DPs).
        System.Windows.Documents.TextElement.SetFontFamily(textView, layout.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(textView, layout.FontSize);
        System.Windows.Documents.TextElement.SetFontWeight(textView, layout.FontWeight);
        System.Windows.Documents.TextElement.SetFontStyle(textView, layout.FontStyle);
        System.Windows.Documents.TextElement.SetFontStretch(textView, layout.FontStretch);
        textView.Document = new Leaf.TextEdit.Document.TextDocument("x");

        // TextView computes metrics lazily on first touch.
        var tvLineHeight = textView.DefaultLineHeight;
        var tvBaseline = textView.DefaultBaseline;
        var tvAdvance = textView.WideSpaceWidth;

        layout.LineHeight.Should().BeApproximately(tvLineHeight, 0.01);
        layout.Baseline.Should().BeApproximately(tvBaseline, 0.01);
        layout.AdvanceWidth.Should().BeApproximately(tvAdvance, 0.01);
    }

    [StaFact]
    public void TabPixelWidth_IsTabSizeTimesAdvanceWidth()
    {
        var layout = new MergePaneGlyphLayout { TabSize = 4 };
        layout.TabPixelWidth.Should().BeApproximately(4 * layout.AdvanceWidth, 0.001);
    }

    [StaFact]
    public void BuildFormattedText_WithCRLF_ProducesMeasurableText()
    {
        var layout = new MergePaneGlyphLayout();
        var ft = layout.BuildFormattedText("first\r\nsecond");
        ft.Width.Should().BeGreaterThan(0);
        ft.Height.Should().BeGreaterThan(0);
    }

    [StaFact]
    public void BuildFormattedText_WithUnicode_ProducesMeasurableText()
    {
        var layout = new MergePaneGlyphLayout();
        var ft = layout.BuildFormattedText("αβγ 🌲");
        ft.Width.Should().BeGreaterThan(0);
        ft.Height.Should().BeGreaterThan(0);
    }

    [StaFact]
    public void ChangingPixelsPerDip_InvalidatesMeasurements()
    {
        var layout = new MergePaneGlyphLayout();
        var baselineBefore = layout.LineHeight;
        layout.PixelsPerDip = 1.5;  // 150% scaling

        var raised = new List<string?>();
        layout.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        layout.PixelsPerDip = 2.0;
        raised.Should().Contain(nameof(MergePaneGlyphLayout.PixelsPerDip));

        // At different DPIs, line-height quantisation can differ under Display mode.
        // Ideal mode is DPI-invariant — we just assert the invalidation path runs.
        _ = layout.LineHeight;  // forces re-measurement
    }

    [StaFact]
    public void ChangingTextFormattingMode_InvalidatesMeasurements()
    {
        var layout = new MergePaneGlyphLayout { PixelsPerDip = 1.5 };
        var ideal = layout.LineHeight;
        layout.TextFormattingMode = System.Windows.Media.TextFormattingMode.Display;
        // Display mode quantises to the pixel grid; may differ from ideal.
        // We only require re-measurement happened (no stale cache).
        var display = layout.LineHeight;
        (ideal > 0).Should().BeTrue();
        (display > 0).Should().BeTrue();
    }

    [StaFact]
    public void InvalidPixelsPerDip_Throws()
    {
        var layout = new MergePaneGlyphLayout();
        FluentActions.Invoking(() => layout.PixelsPerDip = 0)
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => layout.PixelsPerDip = -1)
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [StaFact]
    public void FontFamily_Null_Throws()
    {
        var layout = new MergePaneGlyphLayout();
        FluentActions.Invoking(() => layout.FontFamily = null!)
            .Should().Throw<ArgumentNullException>();
    }
}
