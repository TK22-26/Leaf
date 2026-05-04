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
    public void StartRangeResolveAnimation_RegistersRange_AndRestartsOnReEntry()
    {
        // Public entry point on the pane's V5 resolve-fade surface. Reach
        // into _rangeResolveStarts by reflection — it's the only observable
        // side effect (fade-in progress is read off GetResolvedFadeAlpha
        // which is also private). Asserting the dictionary state is a firmer
        // contract than "does not throw".
        var pane = new ReadOnlyMergePane();
        var startsField = typeof(ReadOnlyMergePane)
            .GetField("_rangeResolveStarts",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        pane.StartRangeResolveAnimation(0);
        pane.StartRangeResolveAnimation(1);
        var starts = (System.Collections.IDictionary)startsField.GetValue(pane)!;
        starts.Count.Should().Be(2, because: "each distinct range gets its own start timestamp");

        // Re-entry on the same index must overwrite the previous timestamp
        // (restart the fade) rather than throw.
        var firstStart = starts[0];
        System.Threading.Thread.Sleep(2); // ensure Stopwatch.GetTimestamp increments
        pane.StartRangeResolveAnimation(0);
        starts[0].Should().NotBe(firstStart,
            because: "re-entering an in-flight index restarts the animation from 'now'");
    }
}
