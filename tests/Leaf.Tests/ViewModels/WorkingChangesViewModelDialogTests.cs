using Leaf.Models;
using Leaf.Services;
using Leaf.Tests.Composition;
using Leaf.Tests.Fakes;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Tests for WorkingChangesViewModel dialog service integration.
/// </summary>
public class WorkingChangesViewModelDialogTests
{
    private readonly FakeGitService _gitService;
    private readonly FakeDialogService _dialogService;
    private readonly WorkingChangesViewModel _viewModel;

    public WorkingChangesViewModelDialogTests()
    {
        // Build the test collection with our production wiring, then
        // swap in the two fakes we inspect in assertions so the test
        // can see the calls VM made.
        _gitService = new FakeGitService();
        _dialogService = new FakeDialogService();

        var services = TestServices.CreateCollection();
        services.Replace(ServiceDescriptor.Singleton<IGitService>(_gitService));
        services.Replace(ServiceDescriptor.Singleton<IDialogService>(_dialogService));

        var provider = TestServices.BuildProvider(services);
        _viewModel = provider.GetRequiredService<WorkingChangesViewModel>();
    }

    [Fact]
    public async Task DiscardFileAsync_ShowsConfirmationDialog()
    {
        // Arrange
        var file = new FileStatusInfo { Path = "test.cs" };
        await _viewModel.SetRepositoryAsync("C:/test/repo");
        _dialogService.ConfirmationResult = true;

        // Act
        await _viewModel.DiscardFileAsync(file);

        // Assert
        Assert.Single(_dialogService.ConfirmationCalls);
        Assert.Contains("test.cs", _dialogService.ConfirmationCalls[0].Message);
        Assert.Equal("Discard Changes", _dialogService.ConfirmationCalls[0].Title);
    }

    [Fact]
    public async Task DiscardFileAsync_WhenUserCancels_DoesNotDiscardFile()
    {
        // Arrange
        var file = new FileStatusInfo { Path = "test.cs" };
        await _viewModel.SetRepositoryAsync("C:/test/repo");
        _dialogService.ConfirmationResult = false;

        // Act
        await _viewModel.DiscardFileAsync(file);

        // Assert
        Assert.Single(_dialogService.ConfirmationCalls);
        Assert.Empty(_gitService.DiscardFileChangesCalls);
    }

    [Fact]
    public async Task DiscardFileAsync_WhenUserConfirms_DiscardsFile()
    {
        // Arrange
        var file = new FileStatusInfo { Path = "test.cs" };
        await _viewModel.SetRepositoryAsync("C:/test/repo");
        _dialogService.ConfirmationResult = true;

        // Act
        await _viewModel.DiscardFileAsync(file);

        // Assert
        Assert.Single(_gitService.DiscardFileChangesCalls);
        Assert.Equal("test.cs", _gitService.DiscardFileChangesCalls[0].FilePath);
    }

    [Fact]
    public async Task DeleteFileAsync_ShowsConfirmationDialog()
    {
        // Arrange
        var file = new FileStatusInfo { Path = "test.cs" };
        await _viewModel.SetRepositoryAsync("C:/test/repo");
        _dialogService.ConfirmationResult = false; // Don't actually delete

        // Act
        await _viewModel.DeleteFileAsync(file);

        // Assert
        Assert.Single(_dialogService.ConfirmationCalls);
        Assert.Contains("test.cs", _dialogService.ConfirmationCalls[0].Message);
        Assert.Equal("Delete File", _dialogService.ConfirmationCalls[0].Title);
    }

    [Fact]
    public async Task DiscardAllAsync_ShowsConfirmationDialog()
    {
        // Arrange
        await _viewModel.SetRepositoryAsync("C:/test/repo");
        _dialogService.ConfirmationResult = false;

        // Act
        await _viewModel.DiscardAllAsync();

        // Assert
        Assert.Single(_dialogService.ConfirmationCalls);
        Assert.Contains("discard all changes", _dialogService.ConfirmationCalls[0].Message);
        Assert.Equal("Discard All Changes", _dialogService.ConfirmationCalls[0].Title);
    }

    [Fact]
    public async Task AdminDeleteReservedFileAsync_ShowsConfirmationDialog()
    {
        // Arrange
        var file = new FileStatusInfo { Path = "nul" };
        await _viewModel.SetRepositoryAsync("C:/test/repo");
        _dialogService.ConfirmationResult = false; // Don't proceed

        // Act
        await _viewModel.AdminDeleteReservedFileAsync(file);

        // Assert
        Assert.Single(_dialogService.ConfirmationCalls);
        Assert.Contains("nul", _dialogService.ConfirmationCalls[0].Message);
        Assert.Equal("Admin Delete", _dialogService.ConfirmationCalls[0].Title);
    }
}

