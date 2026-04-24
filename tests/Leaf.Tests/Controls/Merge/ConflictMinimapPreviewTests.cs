#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Leaf.TextEdit;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="ConflictMinimapPreview"/>. The pixel→line
/// math and the two companion DPs (<see cref="ConflictMinimapPreview.VerticalOffset"/> +
/// <see cref="ConflictMinimapPreview.ViewportHeight"/>) are the load-
/// bearing part — they drive both click-to-jump and the translucent
/// viewport rectangle. Rendering fidelity (actual pixel output) is a
/// Stagehand smoke-test concern; these tests only prove the plumbing.
/// </summary>
public class ConflictMinimapPreviewTests
{
    [Theory]
    [InlineData(0, 100, 1)]       // top of preview → line 1
    [InlineData(2, 100, 2)]       // one LineRowHeight → line 2
    [InlineData(200, 100, 100)]   // past-bottom clamps to last line
    [InlineData(-10, 100, 1)]     // negative clamps to first line
    [InlineData(0, 0, 1)]         // empty document returns line 1
    public void PointerYToLine_MapsPixelToLineIndex(double y, int lineCount, int expected)
    {
        ConflictMinimapPreview.PointerYToLine(y, lineCount).Should().Be(expected);
    }

    [Fact]
    public void PointerYToLine_UsesTwoPixelRowHeight()
    {
        // LineRowHeight = 2 px — a click at Y=50 lands on line 26
        // (floor(50/2) + 1). Pins the constant so a change surfaces
        // here rather than as a silent drift in the view.
        ConflictMinimapPreview.PointerYToLine(50, 1000).Should().Be(26);
    }

    [StaFact]
    public void Refresh_IsCallable_OnFreshControl()
    {
        var preview = new ConflictMinimapPreview();
        FluentActions.Invoking(() => preview.Refresh()).Should().NotThrow();
    }

    [StaFact]
    public void FixedWidth_MatchesPreviewWidthConstant()
    {
        // The control locks its Width to PreviewWidth so the host grid
        // column can size accordingly. A theme / layout regression that
        // stretched the preview would change this value and surface
        // here.
        var preview = new ConflictMinimapPreview();
        preview.Width.Should().Be(ConflictMinimapPreview.PreviewWidth);
    }

    [StaFact]
    public void ViewportOffsetDp_UpdatesWithoutThrow_BeforeRender()
    {
        // Host's ScrollViewer.ScrollChanged can fire before the preview
        // gets its first render pass. Setting VerticalOffset + ViewportHeight
        // on a pre-render control must be safe — no NRE on Layout null.
        var preview = new ConflictMinimapPreview
        {
            VerticalOffset = 100,
            ViewportHeight = 200,
        };
        preview.VerticalOffset.Should().Be(100);
        preview.ViewportHeight.Should().Be(200);
    }

    [StaFact]
    public void SideDp_DefaultsToOurs()
    {
        var preview = new ConflictMinimapPreview();
        preview.Side.Should().Be(MergePaneSide.Ours,
            because: "most callers bind one preview per pane — Ours default matches the left-side placement");
    }
}
