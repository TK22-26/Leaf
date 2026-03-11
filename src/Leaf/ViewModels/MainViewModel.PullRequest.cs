using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services.PullRequests;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Pull request operations and content mode switching.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    private ContentMode _contentMode = ContentMode.Graph;

    [ObservableProperty]
    private PullRequestDetailViewModel? _pullRequestDetailViewModel;

    /// <summary>
    /// True when the main content area is showing the git graph (default mode).
    /// </summary>
    public bool IsGraphMode => ContentMode == ContentMode.Graph;

    /// <summary>
    /// True when the main content area is showing pull request details.
    /// </summary>
    public bool IsPullRequestDetailMode => ContentMode == ContentMode.PullRequestDetail;

    /// <summary>
    /// True when the main content area is showing the pull request create form.
    /// </summary>
    public bool IsPullRequestCreateMode => ContentMode == ContentMode.PullRequestCreate;

    partial void OnContentModeChanged(ContentMode value)
    {
        OnPropertyChanged(nameof(IsGraphMode));
        OnPropertyChanged(nameof(IsPullRequestDetailMode));
        OnPropertyChanged(nameof(IsPullRequestCreateMode));
    }

    /// <summary>
    /// Checks whether PR mode can be entered (blocked by merge/conflict state).
    /// </summary>
    private bool CanEnterPullRequestMode()
    {
        if (SelectedRepository == null)
            return false;

        if (SelectedRepository.IsMergeInProgress || SelectedRepository.ConflictCount > 0)
        {
            _notificationService?.Show(
                "Cannot open pull requests",
                "Resolve the current merge or conflict state before working with pull requests.",
                Services.NotificationType.Warning);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Selects a pull request and switches to detail view.
    /// </summary>
    [RelayCommand]
    private async Task SelectPullRequestAsync(PullRequestInfo? pr)
    {
        if (pr == null || SelectedRepository == null || !CanEnterPullRequestMode())
            return;

        // Clear all other selections
        SelectedRepository.ClearBranchSelection();
        SelectedRepository.ClearPullRequestSelection();

        // Select this PR
        pr.IsSelected = true;
        SelectedRepository.SelectedPullRequest = pr;

        // Switch content mode
        ContentMode = ContentMode.PullRequestDetail;
        IsCommitDetailVisible = false;
        IsDiffViewerVisible = false;

        // Load details
        PullRequestDetailViewModel ??= new PullRequestDetailViewModel(_pullRequestService);
        await PullRequestDetailViewModel.LoadAsync(SelectedRepository.Path, pr.Number);
    }

    /// <summary>
    /// Closes the pull request view and restores the git graph.
    /// </summary>
    [RelayCommand]
    private void ClosePullRequestView()
    {
        ContentMode = ContentMode.Graph;
        IsCommitDetailVisible = true;

        SelectedRepository?.ClearPullRequestSelection();
        PullRequestDetailViewModel?.Clear();
    }

    /// <summary>
    /// Opens the provider's "create pull request" page in the default browser.
    /// </summary>
    [RelayCommand]
    private void OpenCreatePullRequest(string? sourceBranch = null)
    {
        if (SelectedRepository == null)
            return;

        var branch = sourceBranch ?? SelectedRepository.CurrentBranch;
        if (string.IsNullOrEmpty(branch))
            return;

        var url = _pullRequestService.GetCreatePullRequestUrl(SelectedRepository.Path, branch);
        if (string.IsNullOrEmpty(url))
        {
            _notificationService?.Show(
                "Create Pull Request",
                "No supported pull request provider configured for this repository.",
                Services.NotificationType.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>
    /// Opens a pull request's web URL in the default browser.
    /// </summary>
    [RelayCommand]
    private void OpenPullRequestInBrowser(PullRequestInfo? pr)
    {
        if (pr != null && !string.IsNullOrEmpty(pr.Url))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pr.Url) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// Copies a pull request's web URL to the clipboard.
    /// </summary>
    [RelayCommand]
    private void CopyPullRequestUrl(PullRequestInfo? pr)
    {
        if (pr != null && !string.IsNullOrEmpty(pr.Url))
        {
            _clipboardService.SetText(pr.Url);
        }
    }

    /// <summary>
    /// Loads pull requests for a repository. Returns the list for category building.
    /// </summary>
    private async Task<List<PullRequestInfo>> LoadPullRequestsForRepoAsync(RepositoryInfo repo, bool forceReload = false)
    {
        if (repo.PullRequestsLoaded && !forceReload)
            return [];

        try
        {
            // Resolve the provider first (warm-up)
            await _pullRequestService.TryResolveAsync(repo.Path);

            if (!_pullRequestService.IsSupported(repo.Path))
                return [];

            var prs = await _pullRequestService.ListPullRequestsAsync(repo.Path);
            repo.PullRequestsLoaded = true;
            return prs;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PR] Failed to load pull requests: {ex.Message}");
            return [];
        }
    }
}
