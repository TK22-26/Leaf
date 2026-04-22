#nullable enable
using System.Windows.Input;
using FluentAssertions;
using Leaf.ViewModels;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Unit tests for <see cref="MergeCommandPaletteViewModel"/> — the Ctrl+K
/// merge-scoped palette. Covers filter behavior (substring + case-insensitive),
/// keyboard navigation (MoveUp / MoveDown with wrap), confirm routing through
/// <see cref="CommandPaletteItem.Tag"/>, and auto-close on confirm.
/// </summary>
public class MergeCommandPaletteViewModelTests
{
    private static CommandPaletteItem Item(string name, ICommand command, string? detail = null) =>
        new() { DisplayName = name, Detail = detail ?? string.Empty, Tag = command };

    private static CommandPaletteItem Item(string name) =>
        new() { DisplayName = name, Detail = string.Empty, Tag = new NoopCommand() };

    [Fact]
    public void Open_PopulatesAllItems_AndSelectsFirst()
    {
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[] { Item("Accept Ours"), Item("Accept Theirs"), Item("Next conflict") });

        vm.IsOpen.Should().BeTrue();
        vm.FilteredResults.Should().HaveCount(3);
        vm.SelectedIndex.Should().Be(0);
        vm.SelectedResult!.DisplayName.Should().Be("Accept Ours");
    }

    [Fact]
    public void Open_ClearsSearchText_SoEachInvocationStartsFromFullList()
    {
        var vm = new MergeCommandPaletteViewModel();
        vm.SearchText = "leftover";
        vm.Open(new[] { Item("Accept Ours"), Item("Accept Theirs") });

        vm.SearchText.Should().BeEmpty(
            because: "Open must reset any prior filter so the user starts fresh each time");
        vm.FilteredResults.Should().HaveCount(2);
    }

    [Fact]
    public void SearchText_FiltersBySubstring_CaseInsensitive()
    {
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[]
        {
            Item("Accept Ours"),
            Item("Accept Theirs"),
            Item("Next conflict"),
            Item("Undo"),
        });

        vm.SearchText = "accept";

        vm.FilteredResults.Should().HaveCount(2);
        vm.FilteredResults.Select(r => r.DisplayName).Should().BeEquivalentTo(
            new[] { "Accept Ours", "Accept Theirs" });
    }

    [Fact]
    public void SearchText_NoMatch_ProducesEmptyResultList()
    {
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[] { Item("Accept Ours"), Item("Next conflict") });

        vm.SearchText = "zzz_nothing_matches";

        vm.FilteredResults.Should().BeEmpty();
        vm.SelectedIndex.Should().Be(-1);
        vm.SelectedResult.Should().BeNull();
    }

    [Fact]
    public void MoveDown_AdvancesSelection_AndWrapsAtEnd()
    {
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[] { Item("A"), Item("B"), Item("C") });

        vm.MoveDown();
        vm.SelectedIndex.Should().Be(1);

        vm.MoveDown();
        vm.SelectedIndex.Should().Be(2);

        vm.MoveDown();
        vm.SelectedIndex.Should().Be(0,
            because: "arrow-down from the last item should wrap back to the first");
    }

    [Fact]
    public void MoveUp_FromFirst_WrapsToLast()
    {
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[] { Item("A"), Item("B"), Item("C") });

        vm.MoveUp();

        vm.SelectedIndex.Should().Be(2,
            because: "arrow-up from the first item should wrap to the last item");
    }

    [Fact]
    public void Confirm_ExecutesTagCommand_AndClosesPalette()
    {
        var invoked = false;
        var command = new NoopCommand(() => invoked = true);
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[] { Item("Accept Ours", command), Item("Next conflict") });

        vm.Confirm();

        invoked.Should().BeTrue(because: "Confirm should execute the Tag ICommand");
        vm.IsOpen.Should().BeFalse(because: "palette must auto-close after confirm");
    }

    [Fact]
    public void ConfirmItem_WhenCommandCanNotExecute_DoesNotInvoke_ButStillCloses()
    {
        var invoked = false;
        var command = new NoopCommand(() => invoked = true, canExecute: false);
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[] { Item("Accept Ours", command) });

        vm.ConfirmItem(vm.FilteredResults[0]);

        invoked.Should().BeFalse(
            because: "disabled commands must not fire, matching CanExecute guard");
        vm.IsOpen.Should().BeFalse(
            because: "palette closes either way so the user isn't left in a blocked state");
    }

    [Fact]
    public void HandleEscape_ClosesWithoutRunningCommand()
    {
        var invoked = false;
        var command = new NoopCommand(() => invoked = true);
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[] { Item("Accept Ours", command) });

        vm.HandleEscape();

        vm.IsOpen.Should().BeFalse();
        invoked.Should().BeFalse(because: "escape cancels, it does not confirm");
    }

    [Fact]
    public void EmptyItemList_SetsEmptyMessage_AndClearsSelection()
    {
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(Array.Empty<CommandPaletteItem>());

        vm.EmptyMessage.Should().NotBeNullOrEmpty();
        vm.SelectedIndex.Should().Be(-1);
        vm.SelectedResult.Should().BeNull();
    }

    [Fact]
    public void Filter_ProducesHighlightSegments_CoveringQueryPosition()
    {
        var vm = new MergeCommandPaletteViewModel();
        vm.Open(new[] { Item("Next conflict") });

        vm.SearchText = "conflict";

        var item = vm.FilteredResults[0];
        item.NameSegments.Should().NotBeNull();
        item.NameSegments.Any(s => s.IsMatch).Should().BeTrue(
            because: "segments must flag the matched span so the view can emphasize it");
        string.Concat(item.NameSegments.Select(s => s.Text)).Should().Be("Next conflict",
            because: "segments reassemble to the full display name, losing no text");
    }

    private sealed class NoopCommand : ICommand
    {
        private readonly Action? _execute;
        private readonly bool _canExecute;
        public NoopCommand(Action? execute = null, bool canExecute = true)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        public bool CanExecute(object? parameter) => _canExecute;
        public void Execute(object? parameter) => _execute?.Invoke();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
