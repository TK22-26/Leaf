#nullable enable
using System.Threading.Tasks;
using FluentAssertions;
using Leaf.Models;
using Leaf.Tests.Composition;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Tests for Phase E surface area on <see cref="WorkspaceViewModel"/>:
/// the Ctrl+1..9 drilldown command and the paused-merge resume state.
/// </summary>
public class WorkspaceViewModelPhaseETests
{
    private static (WorkspaceViewModel ws, SubmoduleTileViewModel parent, SubmoduleTileViewModel sub) BuildWorkspace()
    {
        var sp = TestServices.BuildProvider(TestServices.CreateCollection());
        var ws = sp.GetRequiredService<WorkspaceViewModel>();
        var parent = new SubmoduleTileViewModel("C:/r/parent", "parent", isParent: true, scope: null, graph: null) { Workspace = ws };
        var sub = new SubmoduleTileViewModel("C:/r/sub", "sub", isParent: false, scope: null, graph: null) { Workspace = ws };
        ws.Tiles.Add(parent);
        ws.Tiles.Add(sub);
        return (ws, parent, sub);
    }

    // ─── E.1: Ctrl+1..9 drilldown ──────────────────────────────────

    [Fact]
    public async Task FocusTileByIndex_NoOp_WhenNotInGridMode()
    {
        var (ws, parent, _) = BuildWorkspace();
        ws.Mode = WorkspaceMode.Single;

        SubmoduleTileViewModel? requested = null;
        ws.TileOpenInSingleViewRequested += (_, t) => requested = t;

        await ws.FocusTileByIndexAsync(1);

        requested.Should().BeNull("the shortcut should only fire when the grid is the active view");
    }

    [Fact]
    public async Task FocusTileByIndex_OutOfRange_NoOps()
    {
        var (ws, _, _) = BuildWorkspace();
        ws.Mode = WorkspaceMode.Grid;

        var raised = 0;
        ws.TileOpenInSingleViewRequested += (_, _) => raised++;

        await ws.FocusTileByIndexAsync(0);    // zero is below 1-based range
        await ws.FocusTileByIndexAsync(99);   // beyond Tiles.Count

        raised.Should().Be(0);
    }

    [Fact]
    public async Task FocusTileByIndex_RaisesOpenInSingleViewForCorrectTile()
    {
        var (ws, parent, sub) = BuildWorkspace();
        ws.Mode = WorkspaceMode.Grid;

        SubmoduleTileViewModel? requested = null;
        ws.TileOpenInSingleViewRequested += (_, t) => requested = t;

        await ws.FocusTileByIndexAsync(1);
        requested.Should().BeSameAs(parent, "Ctrl+1 maps to the parent tile (always position 0)");

        requested = null;
        await ws.FocusTileByIndexAsync(2);
        requested.Should().BeSameAs(sub);
    }

    // ─── E.2: Paused-merge resume state ───────────────────────────

    [Fact]
    public void PausedMerge_StartsNull_HasPausedMergeFalse()
    {
        var (ws, _, _) = BuildWorkspace();
        ws.PausedMerge.Should().BeNull();
        ws.HasPausedMerge.Should().BeFalse();
    }

    [Fact]
    public void CancelPausedMerge_ClearsState()
    {
        var (ws, _, _) = BuildWorkspace();
        ws.PausedMerge = new WorkspaceViewModel.PausedMergeState("develop", MergeType.Normal, "C:/r/sub");
        ws.HasPausedMerge.Should().BeTrue();
        ws.CancelPausedMergeCommand.CanExecute(null).Should().BeTrue();

        ws.CancelPausedMerge();

        ws.PausedMerge.Should().BeNull();
        ws.HasPausedMerge.Should().BeFalse();
    }

    [Fact]
    public async Task ContinueMerge_NoOp_WhenNoPausedState()
    {
        var (ws, _, _) = BuildWorkspace();
        // CanContinueMerge gates execution — assert it directly so the
        // command-handler short-circuit doesn't depend on RelayCommand's
        // internal CanExecute behavior.
        ws.ContinueMergeCommand.CanExecute(null).Should().BeFalse();

        await ws.ContinueMergeAsync();
        ws.PausedMerge.Should().BeNull();
    }
}
