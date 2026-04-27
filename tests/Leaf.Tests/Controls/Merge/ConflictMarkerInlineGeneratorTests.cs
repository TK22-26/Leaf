#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Pins the zdiff3 marker classification contract for the inline-element
/// generator that replaces conflict markers with VS-Code-style toolbars.
/// Misclassifying any of the four marker prefixes would render the wrong
/// inline element (e.g. an [Accept Ours · Theirs · Both · Compare] toolbar
/// at a closer instead of an opener), so this lives in its own test surface.
/// </summary>
public class ConflictMarkerInlineGeneratorTests
{
    [Theory]
    [InlineData("<<<<<<<", "Open")]
    [InlineData("<<<<<<< ours", "Open")]
    [InlineData(">>>>>>>", "Close")]
    [InlineData(">>>>>>> theirs", "Close")]
    [InlineData("|||||||", "Base")]
    [InlineData("||||||| base", "Base")]
    [InlineData("=======", "Equals")]
    public void ClassifyMarker_RecognisesAllFourZdiff3Markers(string line, string expectedKindName)
    {
        ConflictMarkerInlineGenerator.ClassifyMarker(line).ToString().Should().Be(expectedKindName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("public class Foo {")]
    [InlineData("    return user;")]
    [InlineData("// comment")]
    [InlineData("======")]      // 6 equals signs — not a marker
    [InlineData("======= bonus")] // 7 equals + content — exact-match-only marker
    [InlineData("<<<")]          // partial opener — not a marker
    public void ClassifyMarker_RejectsNonMarkerLines(string line)
    {
        ConflictMarkerInlineGenerator.ClassifyMarker(line).ToString().Should().Be("None");
    }

    [Fact]
    public void ClassifyMarker_DistinguishesOpenerFromCloser()
    {
        // Defensive: a regression that flipped Open vs Close prefixes would
        // render the [Accept Ours·Theirs·Both·Compare] toolbar at the WRONG
        // end of the conflict — visually plausible, semantically broken.
        ConflictMarkerInlineGenerator.ClassifyMarker("<<<<<<< ours").ToString().Should().Be("Open");
        ConflictMarkerInlineGenerator.ClassifyMarker(">>>>>>> theirs").ToString().Should().Be("Close");
    }
}
