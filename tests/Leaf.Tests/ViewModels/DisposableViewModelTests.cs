using System.IO;
using FluentAssertions;
using Leaf.Services;
using Leaf.Tests.Fakes;
using Leaf.ViewModels;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Regression tests for plan §1.6 — ensure the new IDisposable child
/// ViewModels tolerate double-Dispose and that calling Dispose before any
/// async operation has started is safe.
/// </summary>
public class DisposableViewModelTests
{
    [Fact]
    public void DiffViewerViewModel_Dispose_IsIdempotent()
    {
        var vm = new DiffViewerViewModel(new FakeGitService());

        var firstDispose = () => vm.Dispose();
        var secondDispose = () => vm.Dispose();

        firstDispose.Should().NotThrow();
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public void DiffViewerViewModel_Dispose_ImplementsIDisposable()
    {
        var vm = new DiffViewerViewModel(new FakeGitService());
        vm.Should().BeAssignableTo<IDisposable>(
            "plan §1.6 contract — MainViewModel.Dispose must be able to cast and call Dispose");
    }

    [Fact]
    public void GitGraphViewModel_Dispose_IsIdempotent()
    {
        var settings = CreateSettings();
        var repositoryService = new RepositoryManagementService(settings);
        var vm = new GitGraphViewModel(new FakeGitService(), settings, repositoryService, new BranchColorPaletteRegistry(settings));

        var firstDispose = () => vm.Dispose();
        var secondDispose = () => vm.Dispose();

        firstDispose.Should().NotThrow();
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public void GitGraphViewModel_Dispose_ImplementsIDisposable()
    {
        var settings = CreateSettings();
        var repositoryService = new RepositoryManagementService(settings);
        var vm = new GitGraphViewModel(new FakeGitService(), settings, repositoryService, new BranchColorPaletteRegistry(settings));
        vm.Should().BeAssignableTo<IDisposable>();
    }

    private static SettingsService CreateSettings()
    {
        var temp = Path.Combine(Path.GetTempPath(), "leaf-tests", Guid.NewGuid().ToString("N"));
        return new SettingsService(temp);
    }

}
