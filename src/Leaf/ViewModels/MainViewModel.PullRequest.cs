using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
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

    [ObservableProperty]
    private CreatePullRequestViewModel? _createPullRequestViewModel;

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

    private bool HasActivePullRequestScreen() =>
        ContentMode is ContentMode.PullRequestDetail or ContentMode.PullRequestCreate ||
        CreatePullRequestViewModel != null ||
        PullRequestDetailViewModel?.Details != null;

    private void DetachCreatePullRequestViewModel(CreatePullRequestViewModel? viewModel)
    {
        if (viewModel == null)
            return;

        viewModel.CreateCompleted -= OnCreatePullRequestCompleted;
        viewModel.CreateCancelled -= OnCreatePullRequestCancelled;
        viewModel.PullRequestCreated -= OnPullRequestCreated;
    }

    private void DetachPullRequestDetailViewModel(PullRequestDetailViewModel? viewModel)
    {
        if (viewModel == null)
            return;

        viewModel.FileSelected -= OnPullRequestFileSelected;
        viewModel.MutationCompleted -= OnPullRequestMutationCompleted;
    }

    private void OnPullRequestMutationCompleted(object? sender, EventArgs e)
    {
        // Refresh PR list after merge/close/update
        if (SelectedRepository != null)
        {
            SelectedRepository.PullRequestsLoaded = false;
            LoadBranchesForRepoAsync(SelectedRepository, forceReload: true)
                .FireAndForget(nameof(LoadBranchesForRepoAsync), isUserAction: true);
        }
    }

    private void ResetPullRequestViewState(RepositoryInfo? repositoryToClearSelection = null)
    {
        ContentMode = ContentMode.Graph;
        IsCommitDetailVisible = true;

        repositoryToClearSelection?.ClearPullRequestSelection();
        PullRequestDetailViewModel?.Clear();

        if (CreatePullRequestViewModel != null)
        {
            DetachCreatePullRequestViewModel(CreatePullRequestViewModel);
            CreatePullRequestViewModel = null;
        }
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
                NotificationType.Warning);
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

        Log.Info("PR", $"Selecting PR #{pr.Number}: {pr.Title}");

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
        PullRequestDetailViewModel ??= CreatePullRequestDetailViewModel();
        await PullRequestDetailViewModel.LoadAsync(SelectedRepository.Path, pr.Number);
    }

    /// <summary>
    /// Closes the pull request view and restores the git graph.
    /// </summary>
    [RelayCommand]
    private void ClosePullRequestView()
    {
        ResetPullRequestViewState(SelectedRepository);
    }

    /// <summary>
    /// Opens the pull request create form in the main content area.
    /// </summary>
    [RelayCommand]
    private async Task OpenCreatePullRequestAsync(object? parameter = null)
    {
        if (SelectedRepository == null || !CanEnterPullRequestMode())
            return;

        var request = parameter switch
        {
            CreatePullRequestRequest createRequest => createRequest,
            string sourceBranchName => new CreatePullRequestRequest(SourceBranch: sourceBranchName),
            _ => new CreatePullRequestRequest(SourceBranch: SelectedRepository.CurrentBranch)
        };

        var sourceBranch = request.SourceBranch ?? SelectedRepository.CurrentBranch;
        var targetBranch = request.TargetBranch;

        Log.Info("PR", $"Opening create PR form (source: {sourceBranch}, target: {targetBranch ?? "<auto>"})");

        // Create and initialize the form VM
        if (CreatePullRequestViewModel != null)
        {
            DetachCreatePullRequestViewModel(CreatePullRequestViewModel);
        }

        var vm = new CreatePullRequestViewModel(_pullRequestService, _gitService, _notificationService)
        {
            GetSessionToken = () => CurrentRepositoryToken
        };
        vm.CreateCompleted += OnCreatePullRequestCompleted;
        vm.CreateCancelled += OnCreatePullRequestCancelled;
        vm.PullRequestCreated += OnPullRequestCreated;

        CreatePullRequestViewModel = vm;

        // Switch content mode
        ContentMode = ContentMode.PullRequestCreate;
        IsCommitDetailVisible = false;
        IsDiffViewerVisible = false;

        await vm.InitializeAsync(SelectedRepository.Path, sourceBranch, targetBranch);
    }

    private void OnCreatePullRequestCompleted(object? sender, EventArgs e)
    {
        // Return to graph and refresh
        ClosePullRequestView();
        if (SelectedRepository != null)
        {
            SelectedRepository.PullRequestsLoaded = false;
            LoadBranchesForRepoAsync(SelectedRepository, forceReload: true)
                .FireAndForget(nameof(LoadBranchesForRepoAsync), isUserAction: true);
        }
    }

    private void OnCreatePullRequestCancelled(object? sender, EventArgs e)
    {
        ClosePullRequestView();
    }

    private void OnPullRequestCreated(object? sender, PullRequestInfo pr)
    {
        Log.Info("PR", $"PR created: #{pr.Number} {pr.Title}");
        _notificationService?.Show(
            "Pull Request Created",
            $"#{pr.Number} {pr.Title}",
            NotificationType.Success,
            OpenPullRequestInBrowserCommand,
            pr,
            new NotificationAction("Open in browser", () =>
            {
                if (!string.IsNullOrEmpty(pr.Url))
                    Process.Start(new ProcessStartInfo(pr.Url) { UseShellExecute = true });
            }),
            new NotificationAction("View in Leaf", () =>
            {
                SelectPullRequestAsync(pr).FireAndForget(nameof(SelectPullRequestAsync), isUserAction: true);
            }));
    }

    /// <summary>
    /// Opens a pull request's web URL in the default browser.
    /// </summary>
    [RelayCommand]
    private void OpenPullRequestInBrowser(PullRequestInfo? pr)
    {
        if (pr != null && !string.IsNullOrEmpty(pr.Url))
        {
            Process.Start(new ProcessStartInfo(pr.Url) { UseShellExecute = true });
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

    [RelayCommand]
    private async Task DeletePullRequestAsync(PullRequestInfo? pr)
    {
        if (SelectedRepository == null || pr == null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete pull request #{pr.Number} \"{pr.Title}\"?\n\nThis will close the pull request on the remote provider.",
            "Confirm Delete Pull Request");

        if (!confirmed)
            return;

        try
        {
            await _pullRequestService.ClosePullRequestAsync(SelectedRepository.Path, pr.Number);

            if (SelectedRepository.SelectedPullRequest?.Number == pr.Number)
            {
                SelectedRepository.ClearPullRequestSelection();
                PullRequestDetailViewModel?.Clear();
                if (ContentMode == ContentMode.PullRequestDetail)
                    ClosePullRequestView();
            }

            SelectedRepository.PullRequestsLoaded = false;
            await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);

            _notificationService?.Show(
                "Pull Request Closed",
                $"#{pr.Number} {pr.Title}",
                NotificationType.Information);
        }
        catch (Exception ex)
        {
            Log.Error("PR", $"Failed to delete PR #{pr.Number}: {ex.Message}", ex);
            _notificationService?.Show(
                "Error",
                $"Failed to delete pull request: {ex.Message}",
                NotificationType.Error);
        }
    }

    /// <summary>
    /// Finds a pull request associated with a commit SHA and navigates to it.
    /// </summary>
    [RelayCommand]
    private async Task FindPullRequestForCommitAsync(CommitInfo? commit)
    {
        if (commit == null || SelectedRepository == null)
            return;

        if (!_pullRequestService.IsSupported(SelectedRepository.Path))
        {
            _notificationService?.Show(
                "Not available",
                "No pull request provider configured for this repository.",
                NotificationType.Warning);
            return;
        }

        try
        {
            await BeginBusyAsync("Searching for pull request...");

            var pr = await _pullRequestService.FindPullRequestForCommitAsync(
                SelectedRepository.Path, commit.Sha);

            if (pr == null)
            {
                // Try squash-merge heuristic: check commit message for PR number pattern
                pr = await TrySquashMergeHeuristicAsync(commit);
            }

            if (pr == null)
            {
                NotifyInfo("No pull request found",
                    $"No pull request is associated with commit {commit.ShortSha}.");
                return;
            }

            await SelectPullRequestAsync(pr);
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Find pull request", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Heuristic: look for PR number in squash-merge commit messages
    /// (e.g., "... (#123)" or "Merge pull request #123").
    /// </summary>
    private async Task<PullRequestInfo?> TrySquashMergeHeuristicAsync(CommitInfo commit)
    {
        if (SelectedRepository == null || string.IsNullOrEmpty(commit.Message))
            return null;

        // Pattern: (#123) at end of first line, or "pull request #123"
        var message = commit.Message;
        var patterns = new[]
        {
            @"\(#(\d+)\)",
            @"[Pp]ull [Rr]equest #(\d+)",
            @"[Mm]erge.*#(\d+)"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(message, pattern);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var prNumber))
            {
                try
                {
                    var details = await _pullRequestService.GetPullRequestAsync(
                        SelectedRepository.Path, prNumber);
                    if (details != null)
                        return details.Summary;
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException
                                        or TaskCanceledException
                                        or InvalidOperationException)
                {
                    // Heuristic failed (network, auth, wrong ID) — continue
                    // trying other patterns. Log so auth/network issues that
                    // mask the feature are diagnosable.
                    Log.Info("PR", $"PR lookup for #{prNumber} failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Creates and configures a PullRequestDetailViewModel with event wiring.
    /// </summary>
    private PullRequestDetailViewModel CreatePullRequestDetailViewModel()
    {
        var vm = new PullRequestDetailViewModel(_pullRequestService, _notificationService);
        vm.FileSelected += OnPullRequestFileSelected;
        vm.MutationCompleted += OnPullRequestMutationCompleted;
        return vm;
    }

    [RelayCommand]
    private async Task TogglePullRequestFilterAsync()
    {
        if (SelectedRepository == null)
            return;

        SelectedRepository.ShowAllPullRequests = !SelectedRepository.ShowAllPullRequests;
        SelectedRepository.PullRequestsLoaded = false;

        Log.Info("PR", $"Toggled PR filter for {SelectedRepository.Name}: {(SelectedRepository.ShowAllPullRequests ? "all" : "open")}");
        await LoadBranchesForRepoAsync(SelectedRepository, forceReload: true);
    }

    /// <summary>
    /// Loads pull requests for a repository. Returns the list for category building.
    /// </summary>
    private async Task<List<PullRequestInfo>> LoadPullRequestsForRepoAsync(RepositoryInfo repo, bool forceReload = false)
    {
        if (repo.PullRequestsLoaded && !forceReload)
        {
            Log.Perf("LoadPRs", "Skipped (already loaded)");
            return [];
        }

        var sw = Log.StartTimer();
        try
        {
            await _pullRequestService.TryResolveAsync(repo.Path);
            Log.Perf("LoadPRs", "TryResolveAsync", sw.ElapsedMilliseconds);

            if (!_pullRequestService.IsSupported(repo.Path))
            {
                Log.Perf("LoadPRs", "Not supported, returning empty", sw.ElapsedMilliseconds);
                return [];
            }

            var listSw = Log.StartTimer();
            var filter = repo.ShowAllPullRequests ? PullRequestState.All : PullRequestState.Open;
            var prs = await _pullRequestService.ListPullRequestsAsync(repo.Path, filter);
            Log.Perf("LoadPRs", $"ListPullRequestsAsync returned {prs.Count} PRs", listSw.ElapsedMilliseconds);

            repo.PullRequestsLoaded = true;
            Log.Perf("LoadPRs", "TOTAL", sw.ElapsedMilliseconds);
            return prs;
        }
        catch (Exception ex)
        {
            Log.Error("LoadPRs", $"Failed after {sw.ElapsedMilliseconds}ms", ex);
            return [];
        }
    }
}
