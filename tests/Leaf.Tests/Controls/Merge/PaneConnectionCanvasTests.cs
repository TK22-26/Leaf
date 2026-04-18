#nullable enable
using System.Windows.Media;
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="PaneConnectionCanvas"/>'s pure helpers.
/// Covers colour-coding, off-screen clip predicate, and endpoint Y math —
/// the parts that dictate what the bezier curves look like without
/// requiring a visual tree.
/// </summary>
public class PaneConnectionCanvasTests
{
    [Fact]
    public void BrushForState_Null_IsUnresolvedGrey()
    {
        var brush = PaneConnectionCanvas.BrushForState(state: null);
        brush.Should().NotBeNull();
        brush.Should().BeAssignableTo<SolidColorBrush>();
    }

    [Fact]
    public void BrushForState_Unresolved_IsUnresolvedBrush()
    {
        var brush = PaneConnectionCanvas.BrushForState(ResolutionState.Unresolved.Instance);
        brush.Should().NotBeNull();
        // Colour is the semi-transparent grey per the renderer constants.
        var solid = brush.Should().BeAssignableTo<SolidColorBrush>().Subject;
        solid.Color.A.Should().BeLessThan(0xFF, "unresolved is semi-transparent");
    }

    [Fact]
    public void BrushForState_AcceptOurs_IsOursBlueBrush()
    {
        var brush = PaneConnectionCanvas.BrushForState(ResolutionState.AcceptOurs.Instance);
        var solid = brush.Should().BeAssignableTo<SolidColorBrush>().Subject;
        // Blue tint: B > R.
        solid.Color.B.Should().BeGreaterThan(solid.Color.R);
    }

    [Fact]
    public void BrushForState_AcceptTheirs_IsTheirsGreenBrush()
    {
        var brush = PaneConnectionCanvas.BrushForState(ResolutionState.AcceptTheirs.Instance);
        var solid = brush.Should().BeAssignableTo<SolidColorBrush>().Subject;
        // Green tint: G > B && G > R.
        solid.Color.G.Should().BeGreaterThan(solid.Color.R);
        solid.Color.G.Should().BeGreaterThan(solid.Color.B);
    }

    [Fact]
    public void BrushForState_AcceptBoth_IsGradientBrush()
    {
        var brush = PaneConnectionCanvas.BrushForState(
            new ResolutionState.AcceptBoth(FirstOurs: true, SmartCombine: true));
        // Gradient from ours-blue to theirs-green signals "both".
        brush.Should().BeAssignableTo<LinearGradientBrush>();
    }

    [Fact]
    public void BrushForState_Manual_IsManualAmberBrush()
    {
        var brush = PaneConnectionCanvas.BrushForState(new ResolutionState.Manual("x"));
        var solid = brush.Should().BeAssignableTo<SolidColorBrush>().Subject;
        // Amber: R ≈ G > B, with R on the warmer side.
        solid.Color.R.Should().BeGreaterThan(solid.Color.B);
        solid.Color.G.Should().BeGreaterThan(solid.Color.B);
    }

    [Fact]
    public void BrushForState_FrozenBrushesAreReused()
    {
        // Hot-path: brushes allocate once in a static field. Two calls for the
        // same state must return the same instance — a regression that
        // allocated per-call would burn GC pressure on every render.
        var b1 = PaneConnectionCanvas.BrushForState(ResolutionState.AcceptOurs.Instance);
        var b2 = PaneConnectionCanvas.BrushForState(ResolutionState.AcceptOurs.Instance);
        b1.Should().BeSameAs(b2);
    }

    [Fact]
    public void ComputeEndpointY_StartsAtLineCenterWithNoOffset()
    {
        // Single-line range on line 1 with lineHeight 20: midline is (1+2)/2 - 0.5 = 1,
        // yielding (1-1)*20 - 0 + 10 = 10 (middle of the first row).
        var y = PaneConnectionCanvas.ComputeEndpointY(new LineRange(1, 2), 20, 0);
        y.Should().Be(10);
    }

    [Fact]
    public void ComputeEndpointY_AccountsForScrollOffset()
    {
        // Same range, but pane scrolled down 50 px. Endpoint moves up by 50.
        var y = PaneConnectionCanvas.ComputeEndpointY(new LineRange(1, 2), 20, 50);
        y.Should().Be(10 - 50);
    }

    [Fact]
    public void ComputeEndpointY_MultiLineRange_UsesMidpoint()
    {
        // Lines [1, 5) = lines 1,2,3,4. Midline is 2.5; endpoint at 2.5 * 20 + 10 - offset = 40.
        var y = PaneConnectionCanvas.ComputeEndpointY(new LineRange(1, 5), 20, 0);
        y.Should().Be(40);
    }

    [Fact]
    public void IsEntirelyOffScreen_BothAbove_IsTrue()
    {
        PaneConnectionCanvas.IsEntirelyOffScreen(-30, -40, canvasHeight: 400, lineHeight: 20)
            .Should().BeTrue();
    }

    [Fact]
    public void IsEntirelyOffScreen_BothBelow_IsTrue()
    {
        PaneConnectionCanvas.IsEntirelyOffScreen(500, 600, canvasHeight: 400, lineHeight: 20)
            .Should().BeTrue();
    }

    [Fact]
    public void IsEntirelyOffScreen_OneOnScreenOneOff_IsFalse()
    {
        // Curve with one endpoint visible still needs to be rendered — the
        // visible portion is what the user sees.
        PaneConnectionCanvas.IsEntirelyOffScreen(-50, 200, canvasHeight: 400, lineHeight: 20)
            .Should().BeFalse();
    }

    [Fact]
    public void IsEntirelyOffScreen_JustBarelyOnScreen_IsFalse()
    {
        // Endpoints inside the lineHeight padding are considered on-screen
        // (so a curve near the edge doesn't flicker as it scrolls in/out).
        PaneConnectionCanvas.IsEntirelyOffScreen(-10, -10, canvasHeight: 400, lineHeight: 20)
            .Should().BeFalse();
    }
}
