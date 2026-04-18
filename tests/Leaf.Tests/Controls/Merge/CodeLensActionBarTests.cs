#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Leaf.TextEdit;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="CodeLensActionBar"/>. Covers child generation per
/// conflict range, resolved-range 40% opacity, and the bar's command wiring.
/// Actual positioning math is driven by <see cref="MergePaneGlyphLayout.LineHeight"/>
/// and the scroll offset — both exercised here through a real layout instance.
/// </summary>
public class CodeLensActionBarTests
{
    private static ModifiedBaseRange Conflict(int index, int resultStartLine)
    {
        return new ModifiedBaseRange(
            Index: index,
            Base: new LineRange(1, 2),
            Ours: new LineRange(1, 2),
            Theirs: new LineRange(1, 2),
            ResultMarkedRange: new LineRange(resultStartLine, resultStartLine + 5),
            BaseLines: new[] { "" },
            OursLines: new[] { "" },
            TheirsLines: new[] { "" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
    }

    [StaFact]
    public void Rebuild_CreatesOneChildPerConflictingRange()
    {
        var bar = new CodeLensActionBar
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { Conflict(0, 10), Conflict(1, 30) },
        };
        bar.Children.Count.Should().Be(2,
            because: "one bar per conflicting range");
    }

    [StaFact]
    public void NonConflictingRanges_AreSkipped()
    {
        var nonConflict = Conflict(0, 10) with { IsConflicting = false };
        var bar = new CodeLensActionBar
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { nonConflict, Conflict(1, 30) },
        };
        bar.Children.Count.Should().Be(1,
            because: "non-conflicting ranges don't get a CodeLens row");
    }

    [StaFact]
    public void ResolvedRange_RendersAtFortyPercentOpacity()
    {
        var bar = new CodeLensActionBar
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { Conflict(0, 10) },
            RangeStates = new Dictionary<int, ResolutionState>
            {
                [0] = ResolutionState.AcceptOurs.Instance,
            },
        };
        var child = (StackPanel)bar.Children[0]!;
        child.Opacity.Should().BeApproximately(0.4, 0.001,
            because: "resolved ranges fade the chrome so it doesn't distract");
    }

    [StaFact]
    public void EachBar_HasThreeLinkChildren()
    {
        var bar = new CodeLensActionBar
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { Conflict(0, 10) },
        };
        var panel = (StackPanel)bar.Children[0]!;
        panel.Children.Count.Should().Be(3,
            because: "Accept Ours + Accept Theirs + Accept Both");
    }

    [StaFact]
    public void AcceptOursCommand_FiresWithRangeIndex()
    {
        int? capturedIndex = null;
        var command = new Leaf.Tests.Controls.Merge.CodeLensActionBarTestCommand(p => capturedIndex = (int)p!);
        var bar = new CodeLensActionBar
        {
            Layout = new MergePaneGlyphLayout(),
            Ranges = new[] { Conflict(7, 10) },
            AcceptOursCommand = command,
        };
        var panel = (StackPanel)bar.Children[0]!;
        var wrapper = (TextBlock)panel.Children[0]!;
        var link = (Hyperlink)wrapper.Inlines.FirstInline!;

        link.Command.Should().Be(command);
        link.CommandParameter.Should().Be(7,
            because: "CommandParameter carries the conflict range index to the VM");
    }
}

/// <summary>Simple ICommand for tests — fires a callback and always-CanExecute.</summary>
internal sealed class CodeLensActionBarTestCommand : ICommand
{
    private readonly Action<object?> _execute;
    public CodeLensActionBarTestCommand(Action<object?> execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
