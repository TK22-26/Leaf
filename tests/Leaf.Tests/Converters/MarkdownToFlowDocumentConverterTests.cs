using System.Globalization;
using System.Linq;
using System.Windows.Documents;
using FluentAssertions;
using Leaf.Converters;
using Xunit;

namespace Leaf.Tests.Converters;

public class MarkdownToFlowDocumentConverterTests
{
    private readonly MarkdownToFlowDocumentConverter _sut = new();

    [Fact]
    public void Convert_ShouldRenderLevelFourHeadingWithoutMarkdownMarkers()
    {
        var document = (FlowDocument)_sut.Convert("#### Heading Four", typeof(FlowDocument), null!, CultureInfo.InvariantCulture);

        document.Blocks.Should().ContainSingle();
        var paragraph = document.Blocks.FirstBlock.Should().BeOfType<Paragraph>().Subject;
        var bold = paragraph.Inlines.FirstInline.Should().BeOfType<Bold>().Subject;
        var run = bold.Inlines.FirstInline.Should().BeOfType<Run>().Subject;

        run.Text.Should().Be("Heading Four");
    }

    [Fact]
    public void Convert_ShouldRenderMarkdownLinksAsHyperlinks()
    {
        var document = (FlowDocument)_sut.Convert("See [Leaf](https://example.com/docs)", typeof(FlowDocument), null!, CultureInfo.InvariantCulture);

        var paragraph = document.Blocks.FirstBlock.Should().BeOfType<Paragraph>().Subject;
        var hyperlink = paragraph.Inlines.OfType<Hyperlink>().Should().ContainSingle().Subject;
        var run = hyperlink.Inlines.FirstInline.Should().BeOfType<Run>().Subject;

        run.Text.Should().Be("Leaf");
        hyperlink.NavigateUri.Should().NotBeNull();
        hyperlink.NavigateUri!.AbsoluteUri.Should().Be("https://example.com/docs");
    }
}
