#nullable enable
using System.Windows.Media;
using FluentAssertions;
using Leaf.Helpers;
using Leaf.TextEdit.Document;
using Leaf.TextEdit.Highlighting;
using Xunit;

namespace Leaf.Tests.Helpers;

/// <summary>
/// Pins the dark-theme syntax-colour remapping contract that both
/// <c>DiffViewerControl</c> and (post-fix) the merge panes rely on.
/// <c>SyntaxHighlightingHelper.ApplyDarkThemeColors</c> walks the named
/// highlighting colours defined by an AvalonEdit
/// <see cref="IHighlightingDefinition"/> and rewrites their foreground
/// brushes to the curated palette (Keywords -&gt; #569CD6, types -&gt;
/// #5598D0, etc.) plus a luminance-based <c>FixDarkColor</c> fallback
/// for tokens not in the explicit <c>ColorMap</c>. Without this remap,
/// AvalonEdit's stock .xshd includes dark blues / reds that render
/// invisibly on Leaf's dark pane surface.
/// </summary>
public class SyntaxHighlightingHelperTests
{
    [Fact]
    public void ApplyDarkThemeColors_MapsKeywordsTokenToExpectedBrightBlue()
    {
        // The C# .xshd names its keyword colour "Keywords" (plural) — see
        // src/Leaf/TextEdit/Highlighting/Resources/CSharp-Mode.xshd.
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
        definition.Should().NotBeNull(because: "the C# highlighting definition ships with Leaf.TextEdit");

        SyntaxHighlightingHelper.ApplyDarkThemeColors(definition!);

        var keywords = definition!.GetNamedColor("Keywords");
        keywords.Should().NotBeNull(because: "C#-mode.xshd defines a 'Keywords' named colour");
        var brush = keywords!.Foreground?.GetBrush(null);
        brush.Should().BeOfType<SolidColorBrush>();
        ((SolidColorBrush)brush!).Color.Should().Be(SyntaxHighlightingHelper.KeywordColor,
            because: "Keywords must land on the curated KeywordColor after remapping");
    }

    [Fact]
    public void ApplyDarkThemeColors_MapsCommentToExpectedGreen()
    {
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
        SyntaxHighlightingHelper.ApplyDarkThemeColors(definition!);

        var comment = definition!.GetNamedColor("Comment");
        comment.Should().NotBeNull();
        var brush = comment!.Foreground?.GetBrush(null);
        ((SolidColorBrush)brush!).Color.Should().Be(SyntaxHighlightingHelper.CommentColor);
    }

    [Fact]
    public void ApplyDarkThemeColors_RedTypeKeywords_LandOnReadableBlue()
    {
        // C# .xshd uses foreground="Red" for ReferenceTypeKeywords. "Red" isn't
        // in ColorMap but FixDarkColor's dark-red branch catches it and remaps
        // to TypeColor on pass 1; passes 2/3 may re-process via rule traversal
        // and further remap (TypeColor itself is blueish-dark so FixDarkColor
        // reads it as "dark blue" and routes to KeywordColor). Either way the
        // end result is a readable light-blue WCAG-AA against the dark pane
        // — which is the user-visible contract we care about. Assert just
        // that it's not the raw Red and matches one of the curated blues.
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
        SyntaxHighlightingHelper.ApplyDarkThemeColors(definition!);

        var refType = definition!.GetNamedColor("ReferenceTypeKeywords");
        refType.Should().NotBeNull();
        var brush = refType!.Foreground?.GetBrush(null);
        var colour = ((SolidColorBrush)brush!).Color;
        colour.Should().NotBe(Colors.Red, because: "stock AvalonEdit 'Red' is load-bearing invisible on dark");
        var acceptable = colour == SyntaxHighlightingHelper.TypeColor
                      || colour == SyntaxHighlightingHelper.KeywordColor;
        acceptable.Should().BeTrue(
            because: $"remap must land on one of the curated type/keyword blues for readability (got {colour})");
    }

    [Fact]
    public void ApplyDarkThemeColors_IsIdempotent()
    {
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
        SyntaxHighlightingHelper.ApplyDarkThemeColors(definition!);
        var firstKeywords = ((SolidColorBrush)definition!.GetNamedColor("Keywords")!.Foreground!.GetBrush(null)!).Color;
        var firstComment = ((SolidColorBrush)definition.GetNamedColor("Comment")!.Foreground!.GetBrush(null)!).Color;

        // Second call must not drift the colours — load-bearing because the
        // highlighting definition is a shared singleton reused across the
        // diff viewer and merge panes in the same AppDomain.
        SyntaxHighlightingHelper.ApplyDarkThemeColors(definition);
        var secondKeywords = ((SolidColorBrush)definition.GetNamedColor("Keywords")!.Foreground!.GetBrush(null)!).Color;
        var secondComment = ((SolidColorBrush)definition.GetNamedColor("Comment")!.Foreground!.GetBrush(null)!).Color;

        secondKeywords.Should().Be(firstKeywords);
        secondComment.Should().Be(firstComment);
    }

    [Fact]
    public void ApplyDarkThemeColors_DoesNotThrow_OnSparseDefinition()
    {
        // Non-C# definitions may have few named colours. The helper must
        // still run cleanly and not require any specific token to exist —
        // it remaps whatever it finds.
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".md");
        if (definition is null) return; // if markdown isn't packaged, skip silently
        var act = () => SyntaxHighlightingHelper.ApplyDarkThemeColors(definition);
        act.Should().NotThrow();
    }

    [Fact]
    public void MarkdownExtension_ResolvesPlainDefinitionWithoutTypographyOverrides()
    {
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".md");
        definition.Should().NotBeNull();
        definition!.Name.Should().Be("MarkDown");

        foreach (var colorName in new[] { "Heading", "Emphasis", "StrongEmphasis", "Code" })
        {
            var color = definition.GetNamedColor(colorName);
            color.Should().NotBeNull();
            AssertDoesNotChangeTypography(color!);
        }

        var document = new TextDocument(
            "# Heading\n" +
            "**strong** and *emphasis*\n" +
            "`inline code`\n" +
            "    public class Foo\n");
        var highlighter = new DocumentHighlighter(document, definition);

        for (var lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
        {
            var highlightedLine = highlighter.HighlightLine(lineNumber);
            foreach (var section in highlightedLine.Sections)
            {
                AssertDoesNotChangeTypography(section.Color);
            }
        }
    }

    private static void AssertDoesNotChangeTypography(HighlightingColor color)
    {
        color.FontFamily.Should().BeNull();
        color.FontSize.Should().BeNull();
        color.FontWeight.Should().BeNull();
        color.FontStyle.Should().BeNull();
    }
}
