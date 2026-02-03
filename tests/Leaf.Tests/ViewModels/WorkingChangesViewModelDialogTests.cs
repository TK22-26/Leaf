using Leaf.Models;
using Leaf.Services;
using Leaf.Tests.Fakes;
using Leaf.ViewModels;
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
        _gitService = new FakeGitService();
        _dialogService = new FakeDialogService();

        // Create minimal fakes for other required services
        var settingsService = new SettingsService();
        var clipboardService = new FakeClipboardService();
        var fileSystemService = new FakeFileSystemService();

        _viewModel = new WorkingChangesViewModel(
            _gitService,
            settingsService,
            clipboardService,
            fileSystemService,
            _dialogService);
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

/// <summary>
/// Minimal fake clipboard service for testing.
/// </summary>
internal class FakeClipboardService : IClipboardService
{
    public string? LastText { get; private set; }

    public void SetText(string text)
    {
        LastText = text;
    }

    public string? GetText()
    {
        return LastText;
    }
}

/// <summary>
/// Minimal fake file system service for testing.
/// </summary>
internal class FakeFileSystemService : IFileSystemService
{
    public void OpenInExplorer(string folderPath) { }
    public void OpenInExplorerAndSelect(string filePath) { }
    public void RevealInExplorer(string path) { }
    public void OpenWithDefaultApp(string filePath) { }
    public void OpenInTerminal(string folderPath) { }
}
