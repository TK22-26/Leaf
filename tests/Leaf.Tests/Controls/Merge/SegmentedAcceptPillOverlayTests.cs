#nullable enable
using System.Windows;
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Leaf.TextEdit;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="SegmentedAcceptPillOverlay"/>. The overlay hosts
/// one <see cref="SegmentedAcceptPill"/> per conflicting range and exposes
/// <c>RefreshPillStates</c> as the explicit refresh entry point called by
/// <c>MergeEditorView.OnRangeStatesChanged</c> — a dictionary-mutation path
/// that cannot fire DP notifications.
/// </summary>
public class SegmentedAcceptPillOverlayTests
{
    // Match the palette-loading pattern used by SegmentedAcceptPillTests so
    // the overlay's inner pills can resolve palette brushes during click
    // highlighting. Not strictly required for RefreshPillStates (which only
    // reads State) but keeps test isolation consistent.
    private static readonly object _paletteLock = new();
    private static bool _paletteMerged;
    private static void EnsurePaletteLoaded()
    {
        lock (_paletteLock)
        {
            if (Application.Current is null)
            {
                try { _ = new Application(); }
                catch (InvalidOperationException) { /* already created by a sibling fixture */ }
            }
            if (_paletteMerged) return;
            var dict = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Leaf;component/Resources/Merge/Merge.xaml",
                    UriKind.Absolute),
            };
            Application.Current!.Resources.MergedDictionaries.Add(dict);
            _paletteMerged = true;
        }
    }

    private static ModifiedBaseRange Conflict(int index) =>
        new(
            Index: index,
            Base: new LineRange(1, 2),
            Ours: new LineRange(1, 2),
            Theirs: new LineRange(1, 2),
            ResultMarkedRange: new LineRange(index + 1, index + 6),
            BaseLines: new[] { "" },
            OursLines: new[] { "" },
            TheirsLines: new[] { "" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);

    [StaFact]
    public void Rebuild_CreatesOnePillPerConflictingRange()
    {
        EnsurePaletteLoaded();
        var overlay = new SegmentedAcceptPillOverlay
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { Conflict(0), Conflict(1), Conflict(2) },
        };
        overlay.Children.Count.Should().Be(3);
    }

    [StaFact]
    public void RefreshPillStates_WithDictionaryMutation_PropagatesNewState()
    {
        // RangeStates is a mutable Dictionary; DP notifications don't fire on
        // mutation. RefreshPillStates is the explicit resync path — without
        // it the pill UI would stay "Unresolved" after a click-accept.
        EnsurePaletteLoaded();
        var states = new Dictionary<int, ResolutionState>
        {
            [0] = ResolutionState.Unresolved.Instance,
        };
        var overlay = new SegmentedAcceptPillOverlay
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { Conflict(0) },
            RangeStates = states,
        };

        var pill = (SegmentedAcceptPill)overlay.Children[0]!;
        pill.State.Should().BeOfType<ResolutionState.Unresolved>();

        // Mutate the same dictionary in place (matching what the VM does when
        // a user accepts), then call the overlay's explicit refresh.
        states[0] = ResolutionState.AcceptOurs.Instance;
        overlay.RefreshPillStates();

        pill.State.Should().BeOfType<ResolutionState.AcceptOurs>(
            because: "RefreshPillStates must re-read the current RangeStates dictionary " +
                     "so the UI tracks mutations that bypass the DP notification path");
    }

    [StaFact]
    public void RefreshPillStates_WithNullRanges_IsNoOp()
    {
        EnsurePaletteLoaded();
        var overlay = new SegmentedAcceptPillOverlay();
        FluentActions.Invoking(() => overlay.RefreshPillStates()).Should().NotThrow(
            because: "pre-DataContext / early-lifecycle calls must not throw");
    }
}
