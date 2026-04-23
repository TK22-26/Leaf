#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Presentation adapter for the C6 git command log footer. Pins the exit-
/// indicator + CommandText string the XAML row template binds to so the
/// log column renders deterministically.
/// </summary>
public class GitCommandLogEntryTests
{
    [Fact]
    public void Entry_SuccessExit_RendersCheckmark()
    {
        var raw = new GitCommandEventArgs("/repo", "merge-file -p ...", 0, "", "");
        var entry = new GitCommandLogEntry(raw);

        entry.ExitCode.Should().Be(0);
        entry.ExitIndicator.Should().Be("✓");
        entry.CommandText.Should().StartWith("git ");
        entry.CommandText.Should().Contain("merge-file");
    }

    [Fact]
    public void Entry_NonZeroExit_RendersCross()
    {
        var raw = new GitCommandEventArgs("/repo", "merge-file -p ...", 1, "", "conflict");

        new GitCommandLogEntry(raw).ExitIndicator.Should().Be("✗");
    }

    [Theory]
    [InlineData("blame --line-porcelain -- foo.cs", true)]
    [InlineData("blame -L 1,10 -- foo.cs", true)]
    [InlineData("rev-parse HEAD", true)]
    [InlineData("merge-file -p base.txt ours.txt theirs.txt", false)]
    [InlineData("commit -m test", false)]
    [InlineData("merge --abort", false)]
    [InlineData("status", false)]
    public void IsBackgroundChatter_ClassifiesArgsPrefix(string args, bool expectedChatty)
    {
        var e = new GitCommandEventArgs("/r", args, 0, "", "");
        GitCommandLog.IsBackgroundChatter(e).Should().Be(expectedChatty,
            because: "blame + rev-parse HEAD are C5 background probes; other commands are user actions");
    }

    [Fact]
    public void IsBackgroundChatter_EmptyArgs_IsNotChatter()
    {
        // Defensive: empty args shouldn't be silently filtered out since we
        // can't classify them. Letting them through surfaces a real bug
        // (empty git command) instead of hiding it.
        var e = new GitCommandEventArgs("/r", "", 0, "", "");
        GitCommandLog.IsBackgroundChatter(e).Should().BeFalse();
    }

    [StaFact]
    public void Entry_ExitBrush_ResolvesToPaletteState()
    {
        // The ExitBrush property walks through MergePaletteResources which
        // needs the palette BAML loaded. [StaFact] keeps the WPF dispatcher
        // context for the resource-flatten fallback path.
        var success = new GitCommandLogEntry(
            new GitCommandEventArgs("/r", "status", 0, "", ""));
        var failure = new GitCommandLogEntry(
            new GitCommandEventArgs("/r", "status", 1, "", ""));

        success.ExitBrush.Should().NotBeNull();
        failure.ExitBrush.Should().NotBeNull();
        success.ExitBrush.Should().NotBe(failure.ExitBrush,
            because: "success and failure must render with distinct palette colours");
    }
}
