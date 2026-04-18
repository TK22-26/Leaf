#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
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
}
