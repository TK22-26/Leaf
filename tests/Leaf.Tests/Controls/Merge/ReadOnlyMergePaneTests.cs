#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="ReadOnlyMergePane"/>. C2 removed the per-side
/// accept checkbox (replaced by the three-cell <see cref="SegmentedAcceptPill"/>
/// on the result pane), so the old <c>IsAcceptedForSide</c> truth-table
/// tests are gone. The pane's remaining public surface — side resolution
/// for region highlights, syntax-highlighting resolution — is covered here
/// and in <c>SyntaxHighlightIntegrationTests</c>.
/// </summary>
public class ReadOnlyMergePaneTests
{
    [Fact]
    public void HighlightingResolver_IsSharedAcrossPanes()
    {
        // The panes no longer carry their own resolver — both call
        // MergeHighlightingResolver.ByFilePath. Pinning a quick sanity check
        // here keeps the shared helper on the test radar without duplicating
        // coverage from FileTypeIconResolverTests / SyntaxHighlightIntegrationTests.
        var definition = MergeHighlightingResolver.ByFilePath("foo.cs");
        definition.Should().NotBeNull();
    }

    [StaFact]
    public void StartRangeResolveAnimation_DoesNotThrow_AndCanBeCalledRepeatedly()
    {
        // Public entry point on the pane's V5 resolve-fade surface. Driven
        // by MergeEditorView.OnRangeStatesChanged when a range flips to
        // resolved. Re-entry with the same index must restart the animation
        // rather than throw, so clicking an accept button twice in quick
        // succession doesn't corrupt the internal ticker state.
        var pane = new ReadOnlyMergePane();
        FluentActions.Invoking(() =>
        {
            pane.StartRangeResolveAnimation(0);
            pane.StartRangeResolveAnimation(0);
            pane.StartRangeResolveAnimation(1);
        }).Should().NotThrow();
    }
}
