using FluentAssertions;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

public class ConflictMarkerParserTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyResult()
    {
        var result = ConflictMarkerParser.Parse(string.Empty);
        result.Conflicts.Should().BeEmpty();
        result.HasTrailingNewline.Should().BeFalse();
        result.OutputLines.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoConflicts_ReturnsLinesWithNoConflicts()
    {
        var text = "line1\nline2\nline3\n";
        var result = ConflictMarkerParser.Parse(text);

        result.Conflicts.Should().BeEmpty();
        result.OutputLines.Should().Equal("line1", "line2", "line3");
        result.HasTrailingNewline.Should().BeTrue();
    }

    [Fact]
    public void Parse_NoTrailingNewline_IsRecorded()
    {
        var result = ConflictMarkerParser.Parse("abc");
        result.HasTrailingNewline.Should().BeFalse();
        result.OutputLines.Should().Equal("abc");
    }

    [Fact]
    public void Parse_SingleZdiff3Conflict_CapturesAllThreeSides()
    {
        var text =
            "context before\n" +
            "<<<<<<< ours\n" +
            "ours line 1\n" +
            "ours line 2\n" +
            "||||||| base\n" +
            "base line\n" +
            "=======\n" +
            "theirs line 1\n" +
            ">>>>>>> theirs\n" +
            "context after\n";

        var result = ConflictMarkerParser.Parse(text);

        result.Conflicts.Should().HaveCount(1);
        var conflict = result.Conflicts[0];
        conflict.OursLines.Should().Equal("ours line 1", "ours line 2");
        conflict.BaseLines.Should().Equal("base line");
        conflict.TheirsLines.Should().Equal("theirs line 1");

        // Marked range covers lines 2..9 (1-based, half-open end = 10).
        conflict.MarkedRange.StartLine.Should().Be(2);
        conflict.MarkedRange.EndLineExclusive.Should().Be(10);
    }

    [Fact]
    public void Parse_MultipleConflicts_InDocumentOrder()
    {
        var text =
            "<<<<<<< ours\na\n||||||| base\nb\n=======\nc\n>>>>>>> theirs\n" +
            "shared\n" +
            "<<<<<<< ours\nx\n||||||| base\ny\n=======\nz\n>>>>>>> theirs\n";

        var result = ConflictMarkerParser.Parse(text);

        result.Conflicts.Should().HaveCount(2);
        result.Conflicts[0].OursLines.Should().Equal("a");
        result.Conflicts[1].OursLines.Should().Equal("x");
        result.Conflicts[0].MarkedRange.StartLine.Should().BeLessThan(result.Conflicts[1].MarkedRange.StartLine);
    }

    [Fact]
    public void Parse_ConflictWithEmptySections_SucceedsWithEmptyLists()
    {
        // pure-add from theirs: ours empty, base empty
        var text =
            "<<<<<<< ours\n" +
            "||||||| base\n" +
            "=======\n" +
            "inserted\n" +
            ">>>>>>> theirs\n";

        var result = ConflictMarkerParser.Parse(text);

        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].OursLines.Should().BeEmpty();
        result.Conflicts[0].BaseLines.Should().BeEmpty();
        result.Conflicts[0].TheirsLines.Should().Equal("inserted");
    }

    [Fact]
    public void Parse_FullyEmptyTriad_TreatedAsContent()
    {
        // A zdiff3 triad with nothing between any of the markers is user documentation
        // (git never emits such blocks). Parser must not report a spurious empty conflict.
        var text =
            "# Example conflict markers in a markdown doc:\n" +
            "<<<<<<< HEAD\n" +
            "||||||| base\n" +
            "=======\n" +
            ">>>>>>> feature\n" +
            "## Next section\n";

        var result = ConflictMarkerParser.Parse(text);
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NestedOpenMarker_InnerBecomesTheRealBlock()
    {
        // A second "<<<<<<<" before the outer block's base marker means the outer line
        // was content; the real conflict starts at the inner opener.
        var text =
            "<<<<<<< outer-looks-like-opener\n" +  // content
            "ours-ish\n" +
            "<<<<<<< real\n" +                      // real conflict starts here
            "real-ours\n" +
            "||||||| base\n" +
            "real-base\n" +
            "=======\n" +
            "real-theirs\n" +
            ">>>>>>> theirs\n";

        var result = ConflictMarkerParser.Parse(text);
        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].MarkedRange.StartLine.Should().Be(3);
        result.Conflicts[0].OursLines.Should().Equal("real-ours");
        result.Conflicts[0].BaseLines.Should().Equal("real-base");
    }

    [Fact]
    public void Parse_MissingBaseMarker_TreatsOpenAsContent()
    {
        // A line starting with <<<<<<< but not followed by ||||||| is just content.
        // Any file that documents conflict markers (tutorials, CHANGELOGs) must not
        // crash the merge engine.
        var text = "<<<<<<< ours\nours\n=======\ntheirs\n>>>>>>> theirs\n";
        var result = ConflictMarkerParser.Parse(text);
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MissingSeparator_TreatsOpenAsContent()
    {
        var text = "<<<<<<< ours\nours\n||||||| base\nbase\n>>>>>>> theirs\n";
        var result = ConflictMarkerParser.Parse(text);
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Parse_StrayCloseMarker_TreatedAsContent()
    {
        var text = "context\n>>>>>>> theirs\nmore\n";
        var result = ConflictMarkerParser.Parse(text);
        result.Conflicts.Should().BeEmpty();
        result.OutputLines.Should().Equal("context", ">>>>>>> theirs", "more");
    }

    [Fact]
    public void Parse_UserContentWithLookalikeOpenMarkerAndNoConflict_TreatedAsContent()
    {
        // Documentation about git conflict markers must not be misidentified.
        var text = "Here is how a conflict looks:\n<<<<<<< HEAD\nfoo\n=======\nbar\n>>>>>>> feature\n";
        var result = ConflictMarkerParser.Parse(text);
        result.Conflicts.Should().BeEmpty();
        result.OutputLines.Should().HaveCount(6);
    }

    [Fact]
    public void Parse_UserContentWithLookalikeOpenFollowedByRealConflict_OnlyRealConflictCaptured()
    {
        // The first "<<<<<<<" is documentation; the second starts a real conflict.
        var text =
            "<<<<<<< documentation line\n" + // line 1: content, no base/separator follows
            "other content\n" +               // line 2
            "<<<<<<< ours\n" +                // line 3: real conflict opens
            "ours-line\n" +
            "||||||| base\n" +
            "base-line\n" +
            "=======\n" +
            "theirs-line\n" +
            ">>>>>>> theirs\n";

        var result = ConflictMarkerParser.Parse(text);
        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].MarkedRange.StartLine.Should().Be(3);
        result.Conflicts[0].OursLines.Should().Equal("ours-line");
    }

    [Fact]
    public void Parse_CapturesLabelsFromOpenBaseAndCloseMarkers()
    {
        var text =
            "<<<<<<< HEAD\n" +
            "ours\n" +
            "||||||| merged common ancestors\n" +
            "base\n" +
            "=======\n" +
            "theirs\n" +
            ">>>>>>> feature/x\n";

        var result = ConflictMarkerParser.Parse(text);
        var c = result.Conflicts.Single();
        c.OursLabel.Should().Be("HEAD");
        c.BaseLabel.Should().Be("merged common ancestors");
        c.TheirsLabel.Should().Be("feature/x");
    }

    [Fact]
    public void Parse_NoLabels_ReturnsNullLabels()
    {
        var text = "<<<<<<<\nours\n|||||||\nbase\n=======\ntheirs\n>>>>>>>\n";
        var result = ConflictMarkerParser.Parse(text);
        var c = result.Conflicts.Single();
        c.OursLabel.Should().BeNull();
        c.BaseLabel.Should().BeNull();
        c.TheirsLabel.Should().BeNull();
    }

    [Fact]
    public void Parse_NormalisesCRLFDefensively()
    {
        // Even with -c core.autocrlf=false the parser must not leak \r into line content.
        var text =
            "context\r\n" +
            "<<<<<<< ours\r\n" +
            "a\r\n" +
            "||||||| base\r\n" +
            "b\r\n" +
            "=======\r\n" +
            "c\r\n" +
            ">>>>>>> theirs\r\n";

        var result = ConflictMarkerParser.Parse(text);

        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].OursLines.Single().Should().Be("a");
        result.Conflicts[0].BaseLines.Single().Should().Be("b");
        result.Conflicts[0].TheirsLines.Single().Should().Be("c");
    }

    [Fact]
    public void Parse_UnicodeContent_RoundTrips()
    {
        var text =
            "αβγ\n" +
            "<<<<<<< ours\n" +
            "λμν — ΞΟΠ\n" +
            "||||||| base\n" +
            "🌲\n" +
            "=======\n" +
            "📝\n" +
            ">>>>>>> theirs\n";

        var result = ConflictMarkerParser.Parse(text);

        result.OutputLines[0].Should().Be("αβγ");
        result.Conflicts[0].OursLines.Single().Should().Be("λμν — ΞΟΠ");
        result.Conflicts[0].BaseLines.Single().Should().Be("🌲");
        result.Conflicts[0].TheirsLines.Single().Should().Be("📝");
    }

    [Fact]
    public void Parse_LabelsAreIgnored_OnlyMarkerSequencesMatter()
    {
        // Labels can contain anything (including things that look like markers).
        var text = "<<<<<<< ours-label with >>> symbols\nours\n||||||| base-label\nbase\n=======\ntheirs\n>>>>>>> theirs-label\n";
        var result = ConflictMarkerParser.Parse(text);
        result.Conflicts.Should().HaveCount(1);
    }
}
