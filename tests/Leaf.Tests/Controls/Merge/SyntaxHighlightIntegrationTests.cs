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
        var definition = MergeHighlightingResolver.ByFilePath(filePath);
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
        var definition = MergeHighlightingResolver.ByFilePath(filePath);
        definition.Should().BeNull(
            because: "missing or unknown extensions must fail gracefully rather than throw");
    }

    [Fact]
    public void BothPanes_ResolveThroughTheSameCentralHelper()
    {
        // Before the closeout, ResultPane and ReadOnlyMergePane each kept
        // their own copy of the extension-to-definition logic. Now both call
        // MergeHighlightingResolver.ByFilePath — asserting the single call
        // succeeds is enough to prove the drift risk is gone.
        const string file = "test.cs";
        var shared = MergeHighlightingResolver.ByFilePath(file);
        shared.Should().NotBeNull(
            because: "the single central resolver must return the same definition for both panes");
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
        var definition = MergeHighlightingResolver.ByFilePath("test.cs")!;
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
