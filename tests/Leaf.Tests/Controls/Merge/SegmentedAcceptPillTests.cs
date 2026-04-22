#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="SegmentedAcceptPill"/>. Confirms the three-cell
/// exclusive-selection contract (Ours / Both / Theirs), the command wiring
/// with range index as parameter, and that clicking a cell invokes the
/// matching command — not the other two.
/// </summary>
public class SegmentedAcceptPillTests
{
    // Ensure the palette dict is merged before the first pill touches
    // DynamicResource lookups — otherwise UpdateCellHighlighting's
    // ResolveBrush calls return Transparent and the highlighting test fails.
    // Matches the pattern used by MergePaletteTests.
    private static readonly object _paletteLock = new();
    private static bool _paletteMerged;
    private static void EnsurePaletteLoaded()
    {
        lock (_paletteLock)
        {
            if (Application.Current is null)
            {
                try { _ = new Application(); }
                catch (InvalidOperationException) { /* another test class already created one */ }
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

    private sealed class RecordingCommand : ICommand
    {
        public int ExecuteCount { get; private set; }
        public object? LastParameter { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    [StaFact]
    public void ThreeCells_AreExposedAsButtons()
    {
        EnsurePaletteLoaded();
        var pill = new SegmentedAcceptPill();
        // Each cell is a Button inside the template — the three named cells
        // are the control's primary contract.
        var ours = (Button)pill.FindName("OursCell")!;
        var both = (Button)pill.FindName("BothCell")!;
        var theirs = (Button)pill.FindName("TheirsCell")!;
        ours.Should().NotBeNull();
        both.Should().NotBeNull();
        theirs.Should().NotBeNull();
    }

    [StaFact]
    public void ClickingOursCell_InvokesAcceptOursCommandWithRangeIndex()
    {
        EnsurePaletteLoaded();
        var cmd = new RecordingCommand();
        var pill = new SegmentedAcceptPill
        {
            RangeIndex = 7,
            AcceptOursCommand = cmd,
        };
        ((Button)pill.FindName("OursCell")!).RaiseEvent(
            new System.Windows.RoutedEventArgs(Button.ClickEvent));
        cmd.ExecuteCount.Should().Be(1);
        cmd.LastParameter.Should().Be(7);
    }

    [StaFact]
    public void ClickingTheirsCell_InvokesAcceptTheirsCommandWithRangeIndex()
    {
        EnsurePaletteLoaded();
        var cmd = new RecordingCommand();
        var pill = new SegmentedAcceptPill
        {
            RangeIndex = 3,
            AcceptTheirsCommand = cmd,
        };
        ((Button)pill.FindName("TheirsCell")!).RaiseEvent(
            new System.Windows.RoutedEventArgs(Button.ClickEvent));
        cmd.ExecuteCount.Should().Be(1);
        cmd.LastParameter.Should().Be(3);
    }

    [StaFact]
    public void ClickingBothCell_InvokesAcceptBothCommandWithRangeIndex()
    {
        EnsurePaletteLoaded();
        var cmd = new RecordingCommand();
        var pill = new SegmentedAcceptPill
        {
            RangeIndex = 5,
            AcceptBothCommand = cmd,
        };
        ((Button)pill.FindName("BothCell")!).RaiseEvent(
            new System.Windows.RoutedEventArgs(Button.ClickEvent));
        cmd.ExecuteCount.Should().Be(1);
        cmd.LastParameter.Should().Be(5);
    }

    [StaFact]
    public void OursClick_DoesNotInvokeOtherCommands()
    {
        EnsurePaletteLoaded();
        var oursCmd = new RecordingCommand();
        var theirsCmd = new RecordingCommand();
        var bothCmd = new RecordingCommand();
        var pill = new SegmentedAcceptPill
        {
            RangeIndex = 0,
            AcceptOursCommand = oursCmd,
            AcceptTheirsCommand = theirsCmd,
            AcceptBothCommand = bothCmd,
        };
        ((Button)pill.FindName("OursCell")!).RaiseEvent(
            new System.Windows.RoutedEventArgs(Button.ClickEvent));
        oursCmd.ExecuteCount.Should().Be(1);
        theirsCmd.ExecuteCount.Should().Be(0);
        bothCmd.ExecuteCount.Should().Be(0);
    }

    [StaFact]
    public void StateChange_UpdatesCellHighlighting()
    {
        EnsurePaletteLoaded();
        var pill = new SegmentedAcceptPill
        {
            RangeIndex = 0,
        };
        pill.State = ResolutionState.AcceptOurs.Instance;
        // Only the Ours cell should have a non-transparent background after
        // the state change — the pill's UpdateCellHighlighting is the single
        // source of exclusive-selection visuals.
        var ours = (Button)pill.FindName("OursCell")!;
        var both = (Button)pill.FindName("BothCell")!;
        var theirs = (Button)pill.FindName("TheirsCell")!;
        ours.Background.Should().NotBe(System.Windows.Media.Brushes.Transparent);
        both.Background.Should().Be(System.Windows.Media.Brushes.Transparent);
        theirs.Background.Should().Be(System.Windows.Media.Brushes.Transparent);
    }
}
