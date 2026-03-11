using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _newCommentText = string.Empty;

    [ObservableProperty]
    private string _requiredReviewerSearchText = string.Empty;

    [ObservableProperty]
    private string _optionalReviewerSearchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ReviewerInfo> _requiredReviewerSearchResults = [];

    [ObservableProperty]
    private ObservableCollection<ReviewerInfo> _optionalReviewerSearchResults = [];

    [ObservableProperty]
    private bool _isSearchingRequiredReviewers;

    [ObservableProperty]
    private bool _isSearchingOptionalReviewers;

    [ObservableProperty]
    private string? _requiredReviewerSearchStatus;

    [ObservableProperty]
    private string? _optionalReviewerSearchStatus;

    [ObservableProperty]
    private string _newLabelText = string.Empty;

    [ObservableProperty]
    private bool _isRequiredReviewerEditorOpen;

    [ObservableProperty]
    private bool _isOptionalReviewerEditorOpen;

    [ObservableProperty]
    private bool _isLabelEditorOpen;

    private CancellationTokenSource? _requiredReviewerSearchCts;
    private CancellationTokenSource? _optionalReviewerSearchCts;

    /// <summary>
    /// Whether the PR is open and can be merged/closed/updated.
    /// </summary>
    public bool IsOpen => Details?.Summary?.State is PullRequestState.Open or PullRequestState.Draft;

    /// <summary>
    /// Whether merge methods are available based on provider capabilities.
    /// </summary>
    public bool CanMerge => IsOpen && (Details?.IsMergeable ?? false);

    /// <summary>
    /// Whether high-level review actions are supported.
    /// </summary>
    public bool SupportsReviews =>
        !string.IsNullOrWhiteSpace(_repoPath) &&
        _pullRequestService.GetCapabilities(_repoPath).HasFlag(PullRequestCapabilities.Reviews) &&
        IsOpen;

    public bool SupportsRequiredReviewers =>
        !string.IsNullOrWhiteSpace(_repoPath) &&
        _pullRequestService.GetCapabilities(_repoPath).HasFlag(PullRequestCapabilities.RequiredReviewers);

    public bool SupportsLabels =>
        !string.IsNullOrWhiteSpace(_repoPath) &&
        _pullRequestService.GetCapabilities(_repoPath).HasFlag(PullRequestCapabilities.Labels);

    public bool CanManageReviewers => IsOpen && !string.IsNullOrWhiteSpace(_repoPath) && !IsLoading;

    public bool CanManageLabels => IsOpen && SupportsLabels && !IsLoading;

    public bool SupportsNeutralReviewFeedback =>
        !string.IsNullOrWhiteSpace(_repoPath) &&
        _pullRequestService.GetCapabilities(_repoPath).HasFlag(PullRequestCapabilities.RequiredReviewers);

    /// <summary>
    /// Required reviewers for providers that distinguish them.
    /// </summary>
    public IReadOnlyList<ReviewerDisplayEntry> RequiredReviewers =>
        BuildReviewerEntries(isRequired: true);

    /// <summary>
    /// Non-required reviewers and review requests.
    /// </summary>
    public IReadOnlyList<ReviewerDisplayEntry> OptionalReviewers =>
        BuildReviewerEntries(isRequired: false);

    public bool IsOverviewSelected => SelectedTabIndex == 0;

    public bool IsFilesSelected => SelectedTabIndex == 1;

    public bool IsUpdatesSelected => SelectedTabIndex == 2;

    public bool IsCommitsSelected => SelectedTabIndex == 3;

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
        NewCommentText = string.Empty;
        NewLabelText = string.Empty;
        CancelReviewerSearches();
        RequiredReviewerSearchText = string.Empty;
        OptionalReviewerSearchText = string.Empty;
        RequiredReviewerSearchResults.Clear();
        OptionalReviewerSearchResults.Clear();
        RequiredReviewerSearchStatus = null;
        OptionalReviewerSearchStatus = null;
        IsRequiredReviewerEditorOpen = false;
        IsOptionalReviewerEditorOpen = false;
        IsLabelEditorOpen = false;
        SelectedTabIndex = 0;

        Log.Info("PR", $"Loading PR #{prNumber} details for {repoPath}");
        var sw = Log.StartTimer();

        try
        {
            Details = await _pullRequestService.GetPullRequestAsync(repoPath, prNumber);
            if (Details != null)
            {
                Log.Info(
                    "PR",
                    $"Loaded PR #{prNumber}: files={Details.Files.Count}, comments={Details.Comments.Count}, checks={Details.StatusChecks.Count}, reviewers={Details.RequestedReviewers.Count}, updates={Details.Updates.Count}, commits={Details.Commits.Count}, workItems={Details.WorkItems.Count}");
            }

            OnPropertyChanged(nameof(IsOpen));
            OnPropertyChanged(nameof(CanMerge));
            OnPropertyChanged(nameof(SupportsReviews));
            OnPropertyChanged(nameof(SupportsRequiredReviewers));
            OnPropertyChanged(nameof(SupportsLabels));
            OnPropertyChanged(nameof(CanManageReviewers));
            OnPropertyChanged(nameof(CanManageLabels));
            OnPropertyChanged(nameof(RequiredReviewers));
            OnPropertyChanged(nameof(OptionalReviewers));
            SubmitReviewCommand.NotifyCanExecuteChanged();
            AddCommentCommand.NotifyCanExecuteChanged();
            AddLabelCommand.NotifyCanExecuteChanged();
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
        NewCommentText = string.Empty;
        NewLabelText = string.Empty;
        CancelReviewerSearches();
        RequiredReviewerSearchText = string.Empty;
        OptionalReviewerSearchText = string.Empty;
        RequiredReviewerSearchResults.Clear();
        OptionalReviewerSearchResults.Clear();
        RequiredReviewerSearchStatus = null;
        OptionalReviewerSearchStatus = null;
        IsRequiredReviewerEditorOpen = false;
        IsOptionalReviewerEditorOpen = false;
        IsLabelEditorOpen = false;
        SelectedTabIndex = 0;
        _repoPath = string.Empty;
        _prNumber = 0;
    }

    [RelayCommand]
    private void ShowOverview() => SelectedTabIndex = 0;

    [RelayCommand]
    private void ShowFiles() => SelectedTabIndex = 1;

    [RelayCommand]
    private void ShowUpdates() => SelectedTabIndex = 2;

    [RelayCommand]
    private void ShowCommits() => SelectedTabIndex = 3;

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

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void OpenRequiredReviewerEditor()
    {
        if (!SupportsRequiredReviewers)
            return;

        IsRequiredReviewerEditorOpen = true;
        IsOptionalReviewerEditorOpen = false;
        OptionalReviewerSearchText = string.Empty;
    }

    [RelayCommand]
    private void OpenOptionalReviewerEditor()
    {
        IsOptionalReviewerEditorOpen = true;
        IsRequiredReviewerEditorOpen = false;
        RequiredReviewerSearchText = string.Empty;
    }

    [RelayCommand]
    private void CloseReviewerEditors()
    {
        IsRequiredReviewerEditorOpen = false;
        IsOptionalReviewerEditorOpen = false;
        RequiredReviewerSearchText = string.Empty;
        OptionalReviewerSearchText = string.Empty;
        ClearReviewerSearch(ReviewerBucket.Required);
        ClearReviewerSearch(ReviewerBucket.Optional);
    }

    [RelayCommand]
    private void ToggleLabelEditor()
    {
        IsLabelEditorOpen = !IsLabelEditorOpen;
        if (!IsLabelEditorOpen)
            NewLabelText = string.Empty;
    }

    partial void OnRequiredReviewerSearchTextChanged(string value)
    {
        ScheduleReviewerSearch(ReviewerBucket.Required, value);
    }

    partial void OnOptionalReviewerSearchTextChanged(string value)
    {
        ScheduleReviewerSearch(ReviewerBucket.Optional, value);
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

    [RelayCommand(CanExecute = nameof(CanSubmitReview))]
    private async Task SubmitReviewAsync(PullRequestReviewState state)
    {
        if (Details == null)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _pullRequestService.SubmitReviewAsync(_repoPath, _prNumber, state, null);
            await LoadAsync(_repoPath, _prNumber);
            MutationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to submit review: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddComment))]
    private async Task AddCommentAsync()
    {
        if (Details == null || string.IsNullOrWhiteSpace(NewCommentText))
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _pullRequestService.AddCommentAsync(_repoPath, _prNumber, NewCommentText.Trim());
            NewCommentText = string.Empty;
            await LoadAsync(_repoPath, _prNumber);
            MutationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add comment: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddRequiredReviewerAsync(ReviewerInfo? reviewer)
    {
        await AddReviewerAsync(reviewer, ReviewerBucket.Required);
    }

    [RelayCommand]
    private async Task AddOptionalReviewerAsync(ReviewerInfo? reviewer)
    {
        await AddReviewerAsync(reviewer, ReviewerBucket.Optional);
    }

    [RelayCommand(CanExecute = nameof(CanAddLabel))]
    private async Task AddLabelAsync()
    {
        if (!SupportsLabels || string.IsNullOrWhiteSpace(NewLabelText))
            return;

        var label = NewLabelText.Trim();
        if (Details?.Labels.Any(existing => string.Equals(existing.Name, label, StringComparison.OrdinalIgnoreCase)) == true)
        {
            ErrorMessage = $"Tag '{label}' is already on this pull request.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _pullRequestService.AddLabelsAsync(_repoPath, _prNumber, [label]);
            NewLabelText = string.Empty;
            IsLabelEditorOpen = false;
            await LoadAsync(_repoPath, _prNumber);
            MutationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add tag: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RemoveLabelAsync(PullRequestLabelInfo? label)
    {
        if (!CanManageLabels || label == null || string.IsNullOrWhiteSpace(label.Name))
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _pullRequestService.RemoveLabelAsync(_repoPath, _prNumber, label.Name);
            await LoadAsync(_repoPath, _prNumber);
            MutationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to remove tag: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
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

    private bool CanSubmitReview() => Details != null && SupportsReviews && !IsLoading;

    private bool CanAddComment() => Details != null && IsOpen && !IsLoading && !string.IsNullOrWhiteSpace(NewCommentText);

    private bool CanAddLabel() => Details != null && IsOpen && SupportsLabels && !IsLoading && !string.IsNullOrWhiteSpace(NewLabelText);

    private async Task AddReviewerAsync(ReviewerInfo? reviewer, ReviewerBucket bucket)
    {
        if (reviewer == null || !CanManageReviewers)
            return;

        var reviewerToRequest = new ReviewerInfo
        {
            Identifier = reviewer.Identifier,
            DisplayName = reviewer.DisplayName,
            SecondaryText = reviewer.SecondaryText,
            AvatarUrl = reviewer.AvatarUrl,
            Kind = reviewer.Kind,
            IsRequired = bucket == ReviewerBucket.Required
        };

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _pullRequestService.RequestReviewersAsync(_repoPath, _prNumber, [reviewerToRequest]);
            ResetReviewerSearch(bucket);
            CloseReviewerEditors();
            await LoadAsync(_repoPath, _prNumber);
            MutationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add reviewer: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ScheduleReviewerSearch(ReviewerBucket bucket, string searchText)
    {
        var cts = new CancellationTokenSource();
        var previous = bucket == ReviewerBucket.Required
            ? Interlocked.Exchange(ref _requiredReviewerSearchCts, cts)
            : Interlocked.Exchange(ref _optionalReviewerSearchCts, cts);

        previous?.Cancel();
        previous?.Dispose();

        if (string.IsNullOrWhiteSpace(searchText) || !CanManageReviewers)
        {
            ClearReviewerSearch(bucket);
            return;
        }

        _ = RunDebouncedReviewerSearchAsync(bucket, searchText.Trim(), cts.Token);
    }

    private async Task RunDebouncedReviewerSearchAsync(ReviewerBucket bucket, string searchText, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            await SearchReviewersAsync(bucket, searchText, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SearchReviewersAsync(ReviewerBucket bucket, string searchText, CancellationToken cancellationToken)
    {
        SetSearching(bucket, true);
        SetSearchStatus(bucket, null);
        SetSearchResults(bucket, []);

        try
        {
            var results = await _pullRequestService.SearchReviewersAsync(_repoPath, searchText);
            cancellationToken.ThrowIfCancellationRequested();

            var targetIsRequired = bucket == ReviewerBucket.Required;
            var filtered = results
                .Where(r => !(Details?.RequestedReviewers.Any(existing =>
                    existing.Identifier == r.Identifier &&
                    existing.Kind == r.Kind &&
                    existing.IsRequired == targetIsRequired) ?? false))
                .ToList();

            SetSearchResults(bucket, filtered);
            SetSearchStatus(bucket, filtered.Count == 0 ? $"No reviewers matching \"{searchText}\"" : null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetSearchStatus(bucket, $"Search failed: {ex.Message}");
            Log.Error("PR", $"Reviewer search failed: {ex.Message}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetSearching(bucket, false);
            }
        }
    }

    private void SetSearchResults(ReviewerBucket bucket, IEnumerable<ReviewerInfo> results)
    {
        var collection = new ObservableCollection<ReviewerInfo>(results);
        if (bucket == ReviewerBucket.Required)
        {
            RequiredReviewerSearchResults = collection;
        }
        else
        {
            OptionalReviewerSearchResults = collection;
        }
    }

    private void SetSearchStatus(ReviewerBucket bucket, string? status)
    {
        if (bucket == ReviewerBucket.Required)
        {
            RequiredReviewerSearchStatus = status;
        }
        else
        {
            OptionalReviewerSearchStatus = status;
        }
    }

    private void SetSearching(ReviewerBucket bucket, bool value)
    {
        if (bucket == ReviewerBucket.Required)
        {
            IsSearchingRequiredReviewers = value;
        }
        else
        {
            IsSearchingOptionalReviewers = value;
        }
    }

    private void ClearReviewerSearch(ReviewerBucket bucket)
    {
        SetSearchResults(bucket, []);
        SetSearchStatus(bucket, null);
        SetSearching(bucket, false);
    }

    private void ResetReviewerSearch(ReviewerBucket bucket)
    {
        if (bucket == ReviewerBucket.Required)
        {
            RequiredReviewerSearchText = string.Empty;
        }
        else
        {
            OptionalReviewerSearchText = string.Empty;
        }

        ClearReviewerSearch(bucket);
    }

    private void CancelReviewerSearches()
    {
        _requiredReviewerSearchCts?.Cancel();
        _requiredReviewerSearchCts?.Dispose();
        _requiredReviewerSearchCts = null;

        _optionalReviewerSearchCts?.Cancel();
        _optionalReviewerSearchCts?.Dispose();
        _optionalReviewerSearchCts = null;
    }

    partial void OnDetailsChanged(PullRequestDetails? value)
    {
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(SupportsReviews));
        OnPropertyChanged(nameof(SupportsRequiredReviewers));
        OnPropertyChanged(nameof(SupportsLabels));
        OnPropertyChanged(nameof(SupportsNeutralReviewFeedback));
        OnPropertyChanged(nameof(CanManageReviewers));
        OnPropertyChanged(nameof(CanManageLabels));
        OnPropertyChanged(nameof(RequiredReviewers));
        OnPropertyChanged(nameof(OptionalReviewers));
        AddLabelCommand.NotifyCanExecuteChanged();
        RemoveLabelCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewSelected));
        OnPropertyChanged(nameof(IsFilesSelected));
        OnPropertyChanged(nameof(IsUpdatesSelected));
        OnPropertyChanged(nameof(IsCommitsSelected));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        SubmitReviewCommand.NotifyCanExecuteChanged();
        AddCommentCommand.NotifyCanExecuteChanged();
        AddLabelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanManageReviewers));
        OnPropertyChanged(nameof(CanManageLabels));
        RemoveLabelCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewCommentTextChanged(string value)
    {
        AddCommentCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewLabelTextChanged(string value)
    {
        AddLabelCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<ReviewerDisplayEntry> BuildReviewerEntries(bool isRequired)
    {
        if (Details == null)
            return [];

        var latestReviews = Details.Reviews
            .GroupBy(review => GetReviewerReviewKey(review))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(review => review.SubmittedAt)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        return Details.RequestedReviewers
            .Where(reviewer => reviewer.IsRequired == isRequired)
            .Select(reviewer =>
            {
                var state = ResolveReviewerState(reviewer, latestReviews);
                return new ReviewerDisplayEntry(reviewer, state);
            })
            .ToList();
    }

    private static string GetReviewerReviewKey(PullRequestReviewInfo review)
    {
        if (!string.IsNullOrWhiteSpace(review.ReviewerLogin))
            return review.ReviewerLogin;

        return review.ReviewerDisplayName ?? string.Empty;
    }

    private static PullRequestReviewState ResolveReviewerState(
        ReviewerInfo reviewer,
        IReadOnlyDictionary<string, PullRequestReviewInfo> latestReviews)
    {
        foreach (var key in GetReviewerMatchKeys(reviewer))
        {
            if (latestReviews.TryGetValue(key, out var review))
                return review.State;
        }

        return PullRequestReviewState.Pending;
    }

    private static IEnumerable<string> GetReviewerMatchKeys(ReviewerInfo reviewer)
    {
        if (!string.IsNullOrWhiteSpace(reviewer.Identifier))
            yield return reviewer.Identifier;

        if (!string.IsNullOrWhiteSpace(reviewer.SecondaryText))
            yield return reviewer.SecondaryText!;

        if (!string.IsNullOrWhiteSpace(reviewer.DisplayName))
            yield return reviewer.DisplayName;
    }

    private enum ReviewerBucket
    {
        Required,
        Optional
    }

    public sealed class ReviewerDisplayEntry
    {
        public ReviewerDisplayEntry(ReviewerInfo reviewer, PullRequestReviewState state)
        {
            Reviewer = reviewer;
            State = state;
        }

        public ReviewerInfo Reviewer { get; }

        public string DisplayName => Reviewer.DisplayName;

        public PullRequestReviewState State { get; }
    }
}
