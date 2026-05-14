#nullable enable
using FluentAssertions;
using Leaf.ViewModels;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Pure-VM tests for the workspace switch-branch dialog. The interesting
/// surface is <see cref="WorkspaceSwitchBranchDialogViewModel.IsValidBranchName"/>
/// — git enforces a substantial rule set on ref names, and the dialog
/// pre-validates so we never ship a malformed name to the workspace
/// iteration.
/// </summary>
public class WorkspaceSwitchBranchDialogViewModelTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("feature/x")]
    [InlineData("release/1.2.3")]
    [InlineData("user/test_branch-2")]
    [InlineData("hotfix/2026.05")]
    public void IsValidBranchName_AcceptsCommonNames(string name)
    {
        WorkspaceSwitchBranchDialogViewModel.IsValidBranchName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("with space")]
    [InlineData("trailing.")]
    [InlineData(".leading")]
    [InlineData("trailing/")]
    [InlineData("/leading")]
    [InlineData("dot..dot")]
    [InlineData("slash//slash")]
    [InlineData("at@{ref")]
    [InlineData("@")]
    [InlineData("trailing.lock")]
    [InlineData("controlchar")]
    [InlineData("question?mark")]
    [InlineData("colon:in:name")]
    [InlineData("tilde~bad")]
    [InlineData("caret^bad")]
    [InlineData("asterisk*bad")]
    [InlineData("bracket[bad")]
    [InlineData("backslash\\bad")]
    [InlineData("feature/.hidden")]
    [InlineData("feature/segment.lock")]
    public void IsValidBranchName_RejectsInvalidNames(string name)
    {
        WorkspaceSwitchBranchDialogViewModel.IsValidBranchName(name).Should().BeFalse();
    }

    [Fact]
    public void CanSwitch_RequiresNonEmptyAndValidName()
    {
        var vm = new WorkspaceSwitchBranchDialogViewModel();
        vm.CanSwitch.Should().BeFalse(); // empty

        vm.BranchName = "feat ure"; // contains space
        vm.CanSwitch.Should().BeFalse();

        vm.BranchName = "feature/ok";
        vm.CanSwitch.Should().BeTrue();
    }

    [Fact]
    public void ValidationError_EmptyWhenNameIsEmpty()
    {
        var vm = new WorkspaceSwitchBranchDialogViewModel { BranchName = "" };

        // Don't yell at the user before they've typed anything. The
        // placeholder + greyed-out Switch button carry the "type
        // something" affordance; an error line on an empty input is
        // visual noise.
        vm.ValidationError.Should().BeEmpty();
    }

    [Fact]
    public void ValidationError_PopulatedForInvalidName()
    {
        var vm = new WorkspaceSwitchBranchDialogViewModel { BranchName = "bad name" };
        vm.ValidationError.Should().NotBeEmpty();
    }

    [Fact]
    public void OptionFlags_DefaultOff()
    {
        var vm = new WorkspaceSwitchBranchDialogViewModel();
        vm.CreateIfMissing.Should().BeFalse();
        vm.StashChanges.Should().BeFalse();
    }
}
