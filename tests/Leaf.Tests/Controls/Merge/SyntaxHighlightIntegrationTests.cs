#nullable enable
using System.Windows.Media;
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.TextEdit.Document;
using Leaf.TextEdit.Highlighting;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Confirms that both merge panes resolve an AvalonEdit syntax-highlighting
/// definition by file extension, and that unknown extensions fail gracefully
/// (null rather than an exception).
/// </summary>
public class SyntaxHighlightIntegrationTests
{
    [Theory]
    [InlineData("foo.cs", "C#")]
    [InlineData("main.py", "Python")]
    [InlineData("app.xml", "XML")]
    [InlineData("index.js", "JavaScript")]
    public void ResolvesCommonExtensions_ToRegisteredDefinitions(string filePath, string expectedNameFragment)
    {
        var definition = ReadOnlyMergePane.ResolveHighlightingDefinition(filePath);
        definition.Should().NotBeNull(
            because: $"'{filePath}' should resolve to a built-in highlighting definition");
        definition!.Name.Should().ContainEquivalentOf(expectedNameFragment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-extension")]
    [InlineData("binary.xyz123unknown")]
    public void UnknownOrEmptyExtensions_ReturnNull(string? filePath)
    {
        var definition = ReadOnlyMergePane.ResolveHighlightingDefinition(filePath);
        definition.Should().BeNull(
            because: "missing or unknown extensions must fail gracefully rather than throw");
    }

    [Fact]
    public void ResultPaneAndReadOnlyPane_AgreeOnExtensionResolution()
    {
        // Both panes go through HighlightingManager.Instance.GetDefinitionByExtension.
        // If they drifted (e.g. one stripped the leading dot, the other didn't),
        // the merge editor would colour Ours + Theirs differently than Result.
        const string file = "test.cs";
        var romp = ReadOnlyMergePane.ResolveHighlightingDefinition(file);
        var result = ResultPane.ResolveHighlighting(file);
        romp.Should().NotBeNull();
        result.Should().NotBeNull();
        romp!.Name.Should().Be(result!.Name);
    }

    [StaFact]
    public void CSharpHighlighter_AssignsNonForegroundBrush_ToAtLeastOneToken()
    {
        // Plan V1/C1 contract: syntax highlighting must actually colour
        // something. A C# line with a keyword like `public` must produce at
        // least one HighlightedSection whose Foreground brush differs from
        // plain black (the default TextBlock foreground). Without this check
        // a broken xshd resource or a regression in DocumentHighlighter
        // plumbing would pass the extension-resolution tests silently.
        var definition = ReadOnlyMergePane.ResolveHighlightingDefinition("test.cs")!;
        var doc = new TextDocument("public class Foo {}");
        var highlighter = new DocumentHighlighter(doc, definition);
        var highlighted = highlighter.HighlightLine(1);

        var distinctBrushes = new HashSet<Color>();
        foreach (var section in highlighted.Sections)
        {
            var brush = section.Color?.Foreground?.GetBrush(null) as SolidColorBrush;
            if (brush is not null) distinctBrushes.Add(brush.Color);
        }
        distinctBrushes.Should().NotBeEmpty(
            because: "C# highlighting must emit at least one coloured section on a keyword line");
        distinctBrushes.Should().NotContain(
            c => c == Colors.Black && distinctBrushes.Count == 1,
            because: "at least one token colour must differ from the default black foreground");
    }
}
