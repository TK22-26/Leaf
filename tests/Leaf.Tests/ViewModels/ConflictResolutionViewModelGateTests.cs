using FluentAssertions;
using Leaf.ViewModels;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Tests for the <c>ConflictResolutionViewModel.ContainsConflictMarkers</c> commit gate.
/// Directly probes the gate because it is the final defence against silent corruption —
/// once a file passes this check, Leaf writes it to disk and runs <c>git add</c>.
/// </summary>
public class ConflictResolutionViewModelGateTests
{
    [Fact]
    public void Gate_EmptyContent_AllowsCommit()
    {
        ConflictResolutionViewModel.ContainsConflictMarkers(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void Gate_PlainContent_AllowsCommit()
    {
        ConflictResolutionViewModel.ContainsConflictMarkers("line1\nline2\nline3\n").Should().BeFalse();
    }

    [Fact]
    public void Gate_FullTriadLF_BlocksCommit()
    {
        var content = "ctx\n<<<<<<< ours\nours-content\n=======\ntheirs-content\n>>>>>>> theirs\nend\n";
        ConflictResolutionViewModel.ContainsConflictMarkers(content).Should().BeTrue();
    }

    [Fact]
    public void Gate_FullTriadCRLF_BlocksCommit()
    {
        // AvalonEdit on Windows preserves CRLF line endings. Without CRLF tolerance in the
        // gate, a legitimate unresolved-conflict file would silently pass and be committed.
        var content = "ctx\r\n<<<<<<< ours\r\nours\r\n=======\r\ntheirs\r\n>>>>>>> theirs\r\nend\r\n";
        ConflictResolutionViewModel.ContainsConflictMarkers(content).Should().BeTrue();
    }

    [Fact]
    public void Gate_LoneOpenMarker_AllowsCommit()
    {
        // User documentation that mentions <<<<<<< but doesn't form a full triad is content.
        var content = "# Example marker\n<<<<<<< this is docs, not a conflict\nmore docs\n";
        ConflictResolutionViewModel.ContainsConflictMarkers(content).Should().BeFalse();
    }

    [Fact]
    public void Gate_OpenerThenCloserButNoSeparator_AllowsCommit()
    {
        // Opener and closer without a separator is not a zdiff3 triad.
        var content = "<<<<<<< ours\nsome text\n>>>>>>> theirs\n";
        ConflictResolutionViewModel.ContainsConflictMarkers(content).Should().BeFalse();
    }

    [Fact]
    public void Gate_SeparatorBeforeOpener_AllowsCommit()
    {
        // Markers out of order are not a real triad.
        var content = "=======\n<<<<<<< later\n>>>>>>> theirs\n";
        ConflictResolutionViewModel.ContainsConflictMarkers(content).Should().BeFalse();
    }

    [Fact]
    public void Gate_SeparatorWithTrailingContent_AllowsCommit()
    {
        // A real zdiff3 separator is exactly "=======" with nothing after.
        var content = "<<<<<<< ours\no\n======= extra\nt\n>>>>>>> theirs\n";
        ConflictResolutionViewModel.ContainsConflictMarkers(content).Should().BeFalse();
    }
}
