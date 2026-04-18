#nullable enable
using System.Windows.Media;
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Pins the <see cref="StatusPill"/> public contract: Count / Label / DotBrush
/// dependency properties round-trip and raise PropertyChanged. Rendering
/// correctness is covered by the palette tests + Stagehand smoke — this test
/// only proves the DP plumbing so host bindings keep working across refactors.
/// </summary>
public class StatusPillTests
{
    [StaFact]
    public void DependencyProperties_RoundTrip()
    {
        var pill = new StatusPill
        {
            Count = 7,
            Label = "Unresolved",
            DotBrush = Brushes.Red,
        };

        pill.Count.Should().Be(7);
        pill.Label.Should().Be("Unresolved");
        pill.DotBrush.Should().Be(Brushes.Red);
    }

    [StaFact]
    public void Defaults_AreSaneForHeaderUsage()
    {
        var pill = new StatusPill();

        pill.Count.Should().Be(0);
        pill.Label.Should().BeEmpty();
        pill.DotBrush.Should().Be(Brushes.Transparent);
    }
}
