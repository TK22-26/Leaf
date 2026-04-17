using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// The launcher's non-trivial logic is the placeholder expansion —
/// the process-launch path is a thin Process.Start wrapper and is
/// covered by manual smoke testing, not unit tests (flaky, depends on
/// a real tool being installed).
/// </summary>
public class ExternalToolLauncherServiceTests
{
    [Fact]
    public void ExpandTemplate_ReplacesLocalAndRemote()
    {
        var result = ExternalToolLauncherService.ExpandTemplate(
            "\"$LOCAL\" \"$REMOTE\"",
            local: "C:/a.txt", remote: "C:/b.txt",
            baseFile: null, merged: null);

        result.Should().Be("\"C:/a.txt\" \"C:/b.txt\"");
    }

    [Fact]
    public void ExpandTemplate_ReplacesAllFourForMerge()
    {
        var result = ExternalToolLauncherService.ExpandTemplate(
            "\"$BASE\" \"$LOCAL\" \"$REMOTE\" -o \"$MERGED\"",
            local: "L", remote: "R", baseFile: "B", merged: "M");

        result.Should().Be("\"B\" \"L\" \"R\" -o \"M\"");
    }

    [Fact]
    public void ExpandTemplate_MissingBase_ThrowsIfTemplateUsesIt()
    {
        var act = () => ExternalToolLauncherService.ExpandTemplate(
            "\"$BASE\" \"$LOCAL\"",
            local: "L", remote: "R", baseFile: null, merged: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*$BASE*");
    }

    [Fact]
    public void ExpandTemplate_MissingMerged_ThrowsIfTemplateUsesIt()
    {
        var act = () => ExternalToolLauncherService.ExpandTemplate(
            "\"$LOCAL\" \"$MERGED\"",
            local: "L", remote: "R", baseFile: null, merged: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*$MERGED*");
    }

    [Fact]
    public void ExpandTemplate_PathsWithSpaces_LeftIntact()
    {
        var result = ExternalToolLauncherService.ExpandTemplate(
            "\"$LOCAL\" \"$REMOTE\"",
            local: "C:/My Documents/a.txt", remote: "C:/b.txt",
            baseFile: null, merged: null);

        // Template-provided quotes wrap the expanded value, so
        // CommandLineToArgvW re-splits cleanly on the other side.
        result.Should().Be("\"C:/My Documents/a.txt\" \"C:/b.txt\"");
    }

    [Fact]
    public void ExpandTemplate_ExpandedPathContainingPlaceholderName_NotReSubstituted()
    {
        // If the user has a temp file literally called "foo$REMOTE.txt"
        // and we ran the old naive Replace chain, expanding $LOCAL first
        // would produce a string containing $REMOTE, which the second
        // Replace would then clobber. Single-pass regex must match only
        // the original template tokens.
        var result = ExternalToolLauncherService.ExpandTemplate(
            "\"$LOCAL\" \"$REMOTE\"",
            local: "C:/tmp/foo$REMOTE.txt",
            remote: "C:/tmp/right.txt",
            baseFile: null,
            merged: null);

        result.Should().Be("\"C:/tmp/foo$REMOTE.txt\" \"C:/tmp/right.txt\"");
    }

    [Fact]
    public void ExpandTemplate_WordBoundary_DoesNotMatchSuffixedIdentifier()
    {
        // `$LOCALE` is a user-authored token, not ours — the \b in the
        // regex keeps it intact.
        var result = ExternalToolLauncherService.ExpandTemplate(
            "--env $LOCALE --file $LOCAL",
            local: "/x",
            remote: "/y",
            baseFile: null,
            merged: null);

        result.Should().Be("--env $LOCALE --file /x");
    }
}
