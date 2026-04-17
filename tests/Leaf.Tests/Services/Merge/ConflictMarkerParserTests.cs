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
    public void Parse_NestedOpenMarker_Throws()
    {
        var text =
            "<<<<<<< ours\n" +
            "ours\n" +
            "<<<<<<< nested\n" + // should fail fast
            "||||||| base\n" +
            "=======\n" +
            "theirs\n" +
            ">>>>>>> theirs\n";

        var act = () => ConflictMarkerParser.Parse(text);
        act.Should().Throw<MergeEngineException>().WithMessage("*nested*");
    }

    [Fact]
    public void Parse_MissingBaseMarker_Throws()
    {
        var text = "<<<<<<< ours\nours\n=======\ntheirs\n>>>>>>> theirs\n";
        var act = () => ConflictMarkerParser.Parse(text);
        act.Should().Throw<MergeEngineException>().WithMessage("*|||||||*");
    }

    [Fact]
    public void Parse_MissingSeparator_Throws()
    {
        var text = "<<<<<<< ours\nours\n||||||| base\nbase\n>>>>>>> theirs\n";
        var act = () => ConflictMarkerParser.Parse(text);
        act.Should().Throw<MergeEngineException>();
    }

    [Fact]
    public void Parse_StrayCloseMarker_Throws()
    {
        var text = "context\n>>>>>>> theirs\nmore\n";
        var act = () => ConflictMarkerParser.Parse(text);
        act.Should().Throw<MergeEngineException>().WithMessage("*stray marker*");
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
