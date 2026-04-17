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
}
