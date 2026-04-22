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
    public void ResolveHighlightingDefinition_IsExposedInternal()
    {
        // Internal-static helper consumed both by the pane at runtime and by
        // SyntaxHighlightIntegrationTests. Pinned here as a canary — renaming
        // it would break that integration without a compile error being
        // routed through the visible-via-tests surface.
        var definition = ReadOnlyMergePane.ResolveHighlightingDefinition("foo.cs");
        definition.Should().NotBeNull();
    }
}
