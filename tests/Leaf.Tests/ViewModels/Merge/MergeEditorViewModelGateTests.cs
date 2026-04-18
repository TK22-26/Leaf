using FluentAssertions;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Tests for the <see cref="MergeEditorViewModel.ContainsConflictMarkers"/>
/// commit gate. Ported from the pre-Phase-2c
/// <c>ConflictResolutionViewModelGateTests</c> which covered the same logic
/// under the old <c>ConflictResolutionViewModel</c>. The gate is the final
/// defence against committing a file that still contains unresolved zdiff3
/// markers — fire "fail loudly" per Engineering-Software policy.
/// </summary>
public class MergeEditorViewModelGateTests
{
    [Fact]
    public void Gate_EmptyContent_AllowsCommit()
    {
        MergeEditorViewModel.ContainsConflictMarkers(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void Gate_PlainContent_AllowsCommit()
    {
        MergeEditorViewModel.ContainsConflictMarkers("line1\nline2\nline3\n").Should().BeFalse();
    }

    [Fact]
    public void Gate_FullTriadLF_BlocksCommit()
    {
        var content = "ctx\n<<<<<<< ours\nours-content\n=======\ntheirs-content\n>>>>>>> theirs\nend\n";
        MergeEditorViewModel.ContainsConflictMarkers(content).Should().BeTrue();
    }

    [Fact]
    public void Gate_FullTriadCRLF_BlocksCommit()
    {
        // AvalonEdit on Windows preserves CRLF; without CRLF tolerance the gate is bypassed.
        var content = "ctx\r\n<<<<<<< ours\r\nours\r\n=======\r\ntheirs\r\n>>>>>>> theirs\r\nend\r\n";
        MergeEditorViewModel.ContainsConflictMarkers(content).Should().BeTrue();
    }

    [Fact]
    public void Gate_LoneOpenMarker_AllowsCommit()
    {
        var content = "# Example marker\n<<<<<<< this is docs, not a conflict\nmore docs\n";
        MergeEditorViewModel.ContainsConflictMarkers(content).Should().BeFalse();
    }

    [Fact]
    public void Gate_OpenerThenCloserButNoSeparator_AllowsCommit()
    {
        var content = "<<<<<<< ours\nsome text\n>>>>>>> theirs\n";
        MergeEditorViewModel.ContainsConflictMarkers(content).Should().BeFalse();
    }

    [Fact]
    public void Gate_SeparatorBeforeOpener_AllowsCommit()
    {
        var content = "=======\n<<<<<<< later\n>>>>>>> theirs\n";
        MergeEditorViewModel.ContainsConflictMarkers(content).Should().BeFalse();
    }

    [Fact]
    public void Gate_SeparatorWithTrailingContent_AllowsCommit()
    {
        // Real zdiff3 separator is exactly "=======".
        var content = "<<<<<<< ours\no\n======= extra\nt\n>>>>>>> theirs\n";
        MergeEditorViewModel.ContainsConflictMarkers(content).Should().BeFalse();
    }
}
