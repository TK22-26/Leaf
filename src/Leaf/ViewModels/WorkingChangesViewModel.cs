using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the working changes staging area view.
/// Handles staging, unstaging, discarding, and committing files.
/// </summary>
public partial class WorkingChangesViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private string? _repositoryPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(FileChangesSummary))]
    private WorkingChangesInfo? _workingChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingChars))]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private string _commitDescription = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Maximum characters for commit message.
    /// </summary>
    public const int MaxMessageLength = 72;

    /// <summary>
    /// Remaining characters for commit message.
    /// </summary>
    public int RemainingChars => MaxMessageLength - CommitMessage.Length;

    /// <summary>
    /// True if there are any working changes.
    /// </summary>
    public bool HasChanges => WorkingChanges?.HasChanges ?? false;

    /// <summary>
    /// True if can commit (has staged files and non-empty message).
    /// </summary>
    public bool CanCommit =>
        WorkingChanges?.HasStagedChanges == true &&
        !string.IsNullOrWhiteSpace(CommitMessage) &&
        CommitMessage.Length <= MaxMessageLength;

    /// <summary>
    /// Summary of file changes for display.
    /// </summary>
    public string FileChangesSummary
    {
        get
        {
            if (WorkingChanges == null)
                return "No changes";

            var total = WorkingChanges.TotalChanges;
            var branch = WorkingChanges.BranchName;

            return total switch
            {
                0 => "No changes",
                1 => $"1 file change on {branch}",
                _ => $"{total} file changes on {branch}"
            };
        }
    }

    public WorkingChangesViewModel(IGitService gitService)
    {
        _gitService = gitService;
    }

    /// <summary>
    /// Set the repository path and refresh working changes.
    /// </summary>
    public async Task SetRepositoryAsync(string? repoPath)
    {
        _repositoryPath = repoPath;
        await RefreshAsync();
    }

    /// <summary>
    /// Set the working changes directly (synced from GitGraphViewModel).
    /// </summary>
    public void SetWorkingChanges(string repoPath, WorkingChangesInfo? workingChanges)
    {
        _repositoryPath = repoPath;
        WorkingChanges = workingChanges;

        // Debug: show what data we received
        if (workingChanges == null)
        {
            ErrorMessage = "SetWorkingChanges: null data received";
        }
        else
        {
            ErrorMessage = $"Synced: {workingChanges.UnstagedFiles.Count} unstaged, {workingChanges.StagedFiles.Count} staged";
        }

        // Force notification for dependent properties
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(FileChangesSummary));
    }

    /// <summary>
    /// Refresh working changes from the repository.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            WorkingChanges = null;
            ErrorMessage = "No repository path set";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            ErrorMessage = $"Loaded: {WorkingChanges?.TotalChanges ?? 0} changes";
        }
        catch (Exception ex)
        {
            WorkingChanges = null;
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Stage a single file.
    /// </summary>
    [RelayCommand]
    public async Task StageFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitService.StageFileAsync(_repositoryPath, file.Path);
            // Refresh and notify - don't use event to avoid triggering full graph reload
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage a single file.
    /// </summary>
    [RelayCommand]
    public async Task UnstageFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitService.UnstageFileAsync(_repositoryPath, file.Path);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Stage all modified files.
    /// </summary>
    [RelayCommand]
    public async Task StageAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        try
        {
            await _gitService.StageAllAsync(_repositoryPath);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage all failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage all staged files.
    /// </summary>
    [RelayCommand]
    public async Task UnstageAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        try
        {
            await _gitService.UnstageAllAsync(_repositoryPath);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage all failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Discard all working directory changes.
    /// </summary>
    [RelayCommand]
    public async Task DiscardAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        // Show confirmation dialog
        var result = MessageBox.Show(
            "Are you sure you want to discard all changes? This cannot be undone.",
            "Discard All Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _gitService.DiscardAllChangesAsync(_repositoryPath);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Commit staged changes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCommit))]
    public async Task CommitAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath) || !CanCommit)
            return;

        try
        {
            IsLoading = true;

            var description = string.IsNullOrWhiteSpace(CommitDescription)
                ? null
                : CommitDescription.Trim();

            await _gitService.CommitAsync(_repositoryPath, CommitMessage.Trim(), description);

            // Clear form after successful commit
            CommitMessage = string.Empty;
            CommitDescription = string.Empty;

            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Commit failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnCommitMessageChanged(string value)
    {
        // Notify CanCommit changed when message changes
        CommitCommand.NotifyCanExecuteChanged();
    }
}
