#nullable enable
using FluentAssertions;
using Leaf.Models;
using Leaf.ViewModels;
using Xunit;

namespace Leaf.Tests.Rebase;

/// <summary>
/// Pure-VM tests for <see cref="RebaseDialogViewModel"/>. The dialog is just
/// a strategy picker — no async, no IO — so the surface to verify is small:
/// derived strings change with the mode, the action button enables and
/// disables in step with <c>IsRebasing</c>, and the option flags round-trip.
/// </summary>
public class RebaseDialogViewModelTests
{
    [Fact]
    public void Defaults_AreStandardModeWithFlagsOff()
    {
        var vm = new RebaseDialogViewModel();

        vm.SelectedMode.Should().Be(RebaseMode.Standard);
        vm.Autosquash.Should().BeFalse();
        vm.UpdateRefs.Should().BeFalse();
        vm.IsRebasing.Should().BeFalse();
        vm.CanRebase.Should().BeTrue();
        vm.DialogTitle.Should().Be("Rebase Branch");
        vm.RebaseButtonText.Should().Be("Rebase");
    }

    [Fact]
    public void SwitchingToInteractive_UpdatesTitleAndButton()
    {
        var vm = new RebaseDialogViewModel();

        vm.SelectedMode = RebaseMode.Interactive;

        vm.DialogTitle.Should().Be("Interactive Rebase");
        vm.RebaseButtonText.Should().Be("Continue…");
    }

    [Fact]
    public void IsRebasing_DisablesCanRebase()
    {
        var vm = new RebaseDialogViewModel { IsRebasing = true };

        vm.CanRebase.Should().BeFalse();
    }

    [Fact]
    public void OptionFlags_RoundTrip()
    {
        var vm = new RebaseDialogViewModel
        {
            Autosquash = true,
            UpdateRefs = true,
        };

        vm.Autosquash.Should().BeTrue();
        vm.UpdateRefs.Should().BeTrue();
    }

    [Fact]
    public void SourceAndTarget_AreObservable()
    {
        var vm = new RebaseDialogViewModel
        {
            SourceBranch = "feature/x",
            TargetBranch = "main",
        };

        vm.SourceBranch.Should().Be("feature/x");
        vm.TargetBranch.Should().Be("main");
    }

    [Theory]
    [InlineData(RebaseMode.Standard, "Rebase Branch", "Rebase")]
    [InlineData(RebaseMode.Interactive, "Interactive Rebase", "Continue…")]
    public void Mode_DrivesDialogTitleAndButtonText(RebaseMode mode, string expectedTitle, string expectedButton)
    {
        var vm = new RebaseDialogViewModel { SelectedMode = mode };

        vm.DialogTitle.Should().Be(expectedTitle);
        vm.RebaseButtonText.Should().Be(expectedButton);
    }
}
