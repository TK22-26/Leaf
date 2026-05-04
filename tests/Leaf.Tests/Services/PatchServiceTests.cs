using System.IO;
using System.Text;
using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Pure-logic tests for <see cref="PatchService"/>: mail-header parsing,
/// the <c>[PATCH]</c> prefix stripper, and RFC 2822 date parsing. The
/// CLI-driven paths (<c>format-patch</c>, <c>am</c>, continue/skip/abort)
/// require a real git fixture and live in the integration suite.
/// </summary>
public class PatchServiceTests
{
    [Fact]
    public void ParseHeaders_FormatPatchOutput_ExtractsAllFields()
    {
        var path = WritePatch(
            "From abcdef1234567890abcdef1234567890abcdef12 Mon Sep 17 00:00:00 2001\n" +
            "From: Alice Example <alice@example.com>\n" +
            "Date: Wed, 29 Apr 2026 10:00:00 +0000\n" +
            "Subject: [PATCH] add the foo widget\n" +
            "\n" +
            "---\n" +
            "diff --git a/foo b/foo\n");

        var item = PatchService.ParseHeaders(path);

        item.HasParseError.Should().BeFalse();
        item.Author.Should().Be("Alice Example <alice@example.com>");
        item.Subject.Should().Be("add the foo widget");
        item.AuthoredWhen.Year.Should().Be(2026);
        item.AuthoredWhen.Month.Should().Be(4);
        item.AuthoredWhen.Day.Should().Be(29);
    }

    [Fact]
    public void ParseHeaders_HandlesContinuationLines()
    {
        // Long subjects wrap with a leading whitespace continuation —
        // this is RFC 2822 unfolding territory and must be preserved.
        var path = WritePatch(
            "From abc Mon Sep 17 00:00:00 2001\n" +
            "From: Bob <b@x>\n" +
            "Date: Wed, 29 Apr 2026 10:00:00 +0000\n" +
            "Subject: [PATCH] wrap this subject\n" +
            "\tacross two lines\n" +
            "\n");

        var item = PatchService.ParseHeaders(path);

        item.HasParseError.Should().BeFalse();
        item.Subject.Should().Be("wrap this subject across two lines");
    }

    [Fact]
    public void ParseHeaders_PatchSeriesPrefix_IsStripped()
    {
        var path = WritePatch(
            "From abc Mon Sep 17 00:00:00 2001\n" +
            "From: A <a@x>\n" +
            "Date: Wed, 29 Apr 2026 10:00:00 +0000\n" +
            "Subject: [PATCH 2/3] middle of series\n" +
            "\n");

        var item = PatchService.ParseHeaders(path);
        item.Subject.Should().Be("middle of series");
    }

    [Fact]
    public void ParseHeaders_NotAnMboxFile_ReturnsParseError()
    {
        // The file exists but doesn't start with the mbox-from line —
        // we refuse it rather than show garbage as a "patch".
        var path = WritePatch("just a plain diff\ndiff --git a/x b/x\n");

        var item = PatchService.ParseHeaders(path);

        item.HasParseError.Should().BeTrue();
        item.Subject.Should().Be(Path.GetFileName(path));
    }

    [Fact]
    public void ParseHeaders_MissingHeaders_ReturnsParseError()
    {
        // mbox-from line present but no Subject — still a malformed
        // patch from our preview's perspective.
        var path = WritePatch(
            "From abc Mon Sep 17 00:00:00 2001\n" +
            "From: A <a@x>\n" +
            "\n");

        var item = PatchService.ParseHeaders(path);
        item.HasParseError.Should().BeTrue();
    }

    [Fact]
    public void ParseHeaders_MissingFile_ReturnsParseError()
    {
        var item = PatchService.ParseHeaders(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".patch"));
        item.HasParseError.Should().BeTrue();
    }

    [Fact]
    public void StripPatchPrefix_StripsBracketedPrefix()
    {
        PatchService.StripPatchPrefix("[PATCH] hello").Should().Be("hello");
        PatchService.StripPatchPrefix("[PATCH 1/3] hello").Should().Be("hello");
        PatchService.StripPatchPrefix("[RFC PATCH v2 3/4] big change").Should().Be("big change");
    }

    [Fact]
    public void StripPatchPrefix_NoPrefix_ReturnsTrimmed()
    {
        PatchService.StripPatchPrefix("plain subject").Should().Be("plain subject");
        PatchService.StripPatchPrefix("  spaced  ").Should().Be("spaced");
    }

    [Fact]
    public void StripPatchPrefix_UnclosedBracket_ReturnsTrimmedOriginal()
    {
        // Defensive: malformed prefix shouldn't eat the whole subject.
        PatchService.StripPatchPrefix("[unclosed never ends").Should().Be("[unclosed never ends");
    }

    [Fact]
    public void StripPatchPrefix_EmptyBracket_StripsAndKeepsTail()
    {
        // [] PATCH] is something a few mail-list filters produce. We strip
        // the empty bracket and trim. Empty-result is fine — the dialog
        // shows the file name in that case via the parse-error path.
        PatchService.StripPatchPrefix("[] real subject").Should().Be("real subject");
        PatchService.StripPatchPrefix("[PATCH]").Should().BeEmpty();
    }

    [Fact]
    public void TryParseRfc2822_HalfHourOffset_ParsesIndia()
    {
        // +0530 (India Standard Time) — half-hour offsets exist and the
        // normaliser must handle them, not just whole-hour zones.
        var when = PatchService.TryParseRfc2822("Wed, 29 Apr 2026 15:30:00 +0530");

        when.Should().NotBeNull();
        when!.Value.UtcDateTime.Should().Be(new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TryParseRfc2822_MinusZeroZero_ParsesAsUtc()
    {
        // RFC 2822 uses -0000 for "no zone known". DateTimeOffset reads
        // it as UTC, which is what we want for sorting/display.
        var when = PatchService.TryParseRfc2822("Wed, 29 Apr 2026 10:00:00 -0000");

        when.Should().NotBeNull();
        when!.Value.UtcDateTime.Should().Be(new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ParseHeaders_BomPrefixedPatch_StillParses()
    {
        // Some editors save .patch files with a UTF-8 BOM. The reader
        // must auto-detect/strip it; otherwise the leading From line
        // starts with a U+FEFF and the mbox-from probe fails, sending a
        // valid patch into the parse-error path.
        var path = Path.Combine(Path.GetTempPath(), "leaf-patch-bom-" + Guid.NewGuid().ToString("N") + ".patch");
        var content =
            "From abcdef Mon Sep 17 00:00:00 2001\n" +
            "From: BOM Person <bom@x>\n" +
            "Date: Wed, 29 Apr 2026 10:00:00 +0000\n" +
            "Subject: [PATCH] handle BOM\n" +
            "\n";
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var item = PatchService.ParseHeaders(path);

        item.HasParseError.Should().BeFalse();
        item.Subject.Should().Be("handle BOM");
        item.Author.Should().Be("BOM Person <bom@x>");
    }

    [Fact]
    public void TryParseRfc2822_ParsesGitFormat()
    {
        var when = PatchService.TryParseRfc2822("Wed, 29 Apr 2026 10:00:00 +0000");

        when.Should().NotBeNull();
        when!.Value.UtcDateTime.Should().Be(new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TryParseRfc2822_ParsesNonZeroOffset()
    {
        // -0500 is what most US-EST authored commits look like; we keep
        // the time accurate after normalising to UTC.
        var when = PatchService.TryParseRfc2822("Wed, 29 Apr 2026 05:00:00 -0500");

        when.Should().NotBeNull();
        when!.Value.UtcDateTime.Should().Be(new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TryParseRfc2822_WrongDayOfWeek_StillParses()
    {
        // 2026-04-29 is a Wednesday, but we don't strict-match the day
        // name — a wrong prefix shouldn't reject the otherwise-valid
        // date. Hand-edited patches and a few non-git tools get this
        // wrong in the wild.
        var when = PatchService.TryParseRfc2822("Mon, 29 Apr 2026 10:00:00 +0000");

        when.Should().NotBeNull();
        when!.Value.UtcDateTime.Should().Be(new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TryParseRfc2822_Garbage_ReturnsNull()
    {
        PatchService.TryParseRfc2822("not a date").Should().BeNull();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("Yes", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData(" true ", true)]      // git tolerates whitespace
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    [InlineData("", false)]            // git treats explicit empty as false
    public void ParseGitConfigBool_RecognisedValues_ParseCorrectly(string raw, bool expected)
    {
        PatchService.ParseGitConfigBool(raw).Should().Be(expected);
    }

    [Fact]
    public void ParseGitConfigBool_NullOrUnknown_ReturnsNull()
    {
        // null = key not set; the caller falls back to its own default
        // rather than treating absent config as a confirmed false.
        PatchService.ParseGitConfigBool(null).Should().BeNull();
        // git rejects garbage values rather than coercing them; we mirror
        // that so a typo in .gitconfig doesn't silently ship as `false`.
        PatchService.ParseGitConfigBool("maybe").Should().BeNull();
        PatchService.ParseGitConfigBool("TRUE!").Should().BeNull();
    }

    private static string WritePatch(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "leaf-patch-test-" + Guid.NewGuid().ToString("N") + ".patch");
        File.WriteAllText(path, content);
        return path;
    }
}
