#nullable enable
using System.Windows;
using FluentAssertions;
using Leaf.Models;
using Leaf.ViewModels;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Pure-VM tests for <see cref="FluentMessageBoxViewModel"/>. The view file
/// drives button visibility off the four <c>Show*</c> booleans the VM
/// exposes; <see cref="FluentMessageBoxViewModel.ApplyButtons"/> is the
/// table that maps WPF's <see cref="MessageBoxButton"/> enum to that set.
/// </summary>
public class FluentMessageBoxViewModelTests
{
    [Fact]
    public void Defaults_HideAllButtonsAndCheckbox()
    {
        var vm = new FluentMessageBoxViewModel();

        vm.ShowOk.Should().BeFalse();
        vm.ShowYes.Should().BeFalse();
        vm.ShowNo.Should().BeFalse();
        vm.ShowCancel.Should().BeFalse();
        vm.ShowDoNotShowAgain.Should().BeFalse();
        vm.HasIcon.Should().BeFalse();
    }

    [Theory]
    [InlineData(MessageBoxButton.OK, true, false, false, false)]
    [InlineData(MessageBoxButton.OKCancel, true, false, false, true)]
    [InlineData(MessageBoxButton.YesNo, false, true, true, false)]
    [InlineData(MessageBoxButton.YesNoCancel, false, true, true, true)]
    public void ApplyButtons_MapsEnumToVisibilityFlags(
        MessageBoxButton buttons, bool ok, bool yes, bool no, bool cancel)
    {
        var vm = new FluentMessageBoxViewModel();
        vm.ApplyButtons(buttons);

        vm.ShowOk.Should().Be(ok);
        vm.ShowYes.Should().Be(yes);
        vm.ShowNo.Should().Be(no);
        vm.ShowCancel.Should().Be(cancel);
    }

    [Fact]
    public void Icon_FlippedAway_FromNone_TogglesHasIcon()
    {
        var vm = new FluentMessageBoxViewModel { Icon = FluentMessageBoxIcon.None };
        vm.HasIcon.Should().BeFalse();

        vm.Icon = FluentMessageBoxIcon.Warning;
        vm.HasIcon.Should().BeTrue();
    }

    [Theory]
    [InlineData(FluentMessageBoxIcon.Information, FluentIcons.Common.Symbol.Info)]
    [InlineData(FluentMessageBoxIcon.Warning, FluentIcons.Common.Symbol.Warning)]
    [InlineData(FluentMessageBoxIcon.Error, FluentIcons.Common.Symbol.ErrorCircle)]
    [InlineData(FluentMessageBoxIcon.Question, FluentIcons.Common.Symbol.QuestionCircle)]
    public void IconSymbol_MapsToFluentIcon(FluentMessageBoxIcon kind, FluentIcons.Common.Symbol expected)
    {
        var vm = new FluentMessageBoxViewModel { Icon = kind };
        vm.IconSymbol.Should().Be(expected);
    }

    [Fact]
    public void DoNotShowAgain_DefaultsUncheckedEvenWhenSurfaced()
    {
        // The host opts the checkbox in via ShowDoNotShowAgain; its
        // initial state has to be unchecked so users have to actively
        // suppress the dialog rather than accidentally hide it.
        var vm = new FluentMessageBoxViewModel { ShowDoNotShowAgain = true };

        vm.DoNotShowAgainChecked.Should().BeFalse();
    }
}
