using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.PullRequests;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the pull request detail view in the main content area.
/// </summary>
public partial class PullRequestDetailViewModel : ObservableObject
{
    private readonly IPullRequestService _pullRequestService;

    private string _repoPath = string.Empty;
    private int _prNumber;

    [ObservableProperty]
    private PullRequestDetails? _details;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editTitle = string.Empty;

    [ObservableProperty]
    private string _editBody = string.Empty;

    /// <summary>
    /// Whether the PR is open and can be merged/closed/updated.
    /// </summary>
    public bool IsOpen => Details?.Summary?.State is PullRequestState.Open or PullRequestState.Draft;

    /// <summary>
    /// Whether merge methods are available based on provider capabilities.
    /// </summary>
    public bool CanMerge => IsOpen && (Details?.IsMergeable ?? false);

    /// <summary>
    /// Raised when a merge/close completes and the caller should refresh.
    /// </summary>
    public event EventHandler? MutationCompleted;

    /// <summary>
    /// Raised when the user wants to view a file diff.
    /// </summary>
    public event EventHandler<PullRequestFileInfo>? FileSelected;

    public PullRequestDetailViewModel(IPullRequestService pullRequestService)
    {
        _pullRequestService = pullRequestService;
    }

    /// <summary>
    /// Loads full details for a pull request.
    /// </summary>
    public async Task LoadAsync(string repoPath, int prNumber)
    {
        _repoPath = repoPath;
        _prNumber = prNumber;
        IsLoading = true;
        ErrorMessage = null;
        IsEditing = false;

        Log.Info("PR", $"Loading PR #{prNumber} details for {repoPath}");
        var sw = Log.StartTimer();

        try
        {
            Details = await _pullRequestService.GetPullRequestAsync(repoPath, prNumber);
            OnPropertyChanged(nameof(IsOpen));
            OnPropertyChanged(nameof(CanMerge));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load PR #{prNumber}: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            Log.Perf("PR", $"LoadAsync PR #{prNumber}", sw.ElapsedMilliseconds);
            IsLoading = false;
        }
    }

    /// <summary>
    /// Clears the loaded detail state.
    /// </summary>
    public void Clear()
    {
        Details = null;
        ErrorMessage = null;
        IsLoading = false;
        IsEditing = false;
        _repoPath = string.Empty;
        _prNumber = 0;
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        var url = Details?.Summary?.Url;
        if (!string.IsNullOrEmpty(url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void SelectFile(PullRequestFileInfo? file)
    {
        if (file != null)
            FileSelected?.Invoke(this, file);
    }

    // --- Merge ---

    [RelayCommand]
    private async Task MergeAsync(MergeMethod method)
    {
        if (Details == null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var result = await _pullRequestService.MergePullRequestAsync(
                _repoPath, _prNumber, method);

            if (!result.Success)
            {
                ErrorMessage = $"Merge failed: {result.ErrorMessage}";
                return;
            }

            // Reload to get updated state
            await LoadAsync(_repoPath, _prNumber);
            MutationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Merge failed: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // --- Close ---

    [RelayCommand]
    private async Task CloseAsync()
    {
        if (Details == null || !IsOpen) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _pullRequestService.ClosePullRequestAsync(_repoPath, _prNumber);
            await LoadAsync(_repoPath, _prNumber);
            MutationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to close PR: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // --- Update (edit title/body) ---

    [RelayCommand]
    private void StartEdit()
    {
        if (Details == null) return;
        EditTitle = Details.Summary.Title;
        EditBody = Details.Body;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (Details == null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _pullRequestService.UpdatePullRequestAsync(
                _repoPath, _prNumber,
                title: EditTitle != Details.Summary.Title ? EditTitle : null,
                body: EditBody != Details.Body ? EditBody : null);

            IsEditing = false;
            await LoadAsync(_repoPath, _prNumber);
            MutationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to update PR: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refresh the current PR data from the provider.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!string.IsNullOrEmpty(_repoPath) && _prNumber > 0)
            await LoadAsync(_repoPath, _prNumber);
    }

    /// <summary>
    /// Opens a status check's target URL in the browser.
    /// </summary>
    [RelayCommand]
    private void OpenCheckUrl(PullRequestStatusCheckInfo? check)
    {
        if (check != null && !string.IsNullOrEmpty(check.TargetUrl))
        {
            Process.Start(new ProcessStartInfo(check.TargetUrl) { UseShellExecute = true });
        }
    }
}
