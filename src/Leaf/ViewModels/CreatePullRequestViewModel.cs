using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Git.Core;
using Leaf.Services.PullRequests;

namespace Leaf.ViewModels;

public partial class CreatePullRequestViewModel : ObservableObject
{
    private readonly IPullRequestService _pullRequestService;
    private readonly IGitService _gitService;
    private readonly INotificationService? _notificationService;

    private string _repoPath = string.Empty;

    // Form fields
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private bool _isDraft;
    [ObservableProperty] private string _sourceBranch = string.Empty;
    [ObservableProperty] private string _targetBranch = string.Empty;
    [ObservableProperty] private string _requiredReviewerSearchText = string.Empty;
    [ObservableProperty] private string _optionalReviewerSearchText = string.Empty;

    // Branch lists for dropdowns
    [ObservableProperty] private ObservableCollection<string> _availableBranches = [];

    // Reviewer search
    [ObservableProperty] private ObservableCollection<ReviewerInfo> _requiredReviewerSearchResults = [];
    [ObservableProperty] private ObservableCollection<ReviewerInfo> _optionalReviewerSearchResults = [];
    [ObservableProperty] private ObservableCollection<ReviewerInfo> _selectedReviewers = [];

    // State
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private bool _isSearchingRequiredReviewers;
    [ObservableProperty] private bool _isSearchingOptionalReviewers;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _requiredReviewerSearchStatus;
    [ObservableProperty] private string? _optionalReviewerSearchStatus;
    [ObservableProperty] private bool _supportsDraft;
    [ObservableProperty] private bool _supportsRequiredReviewers;

    private CancellationTokenSource? _requiredReviewerSearchCts;
    private CancellationTokenSource? _optionalReviewerSearchCts;

    // Events
    public event EventHandler? CreateCompleted; // Fired after successful create
    public event EventHandler? CreateCancelled; // Fired on cancel
    public event EventHandler<PullRequestInfo>? PullRequestCreated; // Passes the newly created PR

    public CreatePullRequestViewModel(
        IPullRequestService pullRequestService,
        IGitService gitService,
        INotificationService? notificationService)
    {
        _pullRequestService = pullRequestService;
        _gitService = gitService;
        _notificationService = notificationService;

        SelectedReviewers.CollectionChanged += OnSelectedReviewersCollectionChanged;
    }

    /// <summary>
    /// Initialize form for a given repository. Loads branch lists and capabilities.
    /// </summary>
    public async Task InitializeAsync(string repoPath, string? preselectedSourceBranch = null, string? preselectedTargetBranch = null)
    {
        _repoPath = repoPath;
        ErrorMessage = null;
        Title = string.Empty;
        Body = string.Empty;
        IsDraft = false;
        SelectedReviewers.Clear();
        CancelReviewerSearches();
        RequiredReviewerSearchResults.Clear();
        OptionalReviewerSearchResults.Clear();
        RequiredReviewerSearchText = string.Empty;
        OptionalReviewerSearchText = string.Empty;
        RequiredReviewerSearchStatus = null;
        OptionalReviewerSearchStatus = null;

        try
        {
            // Load branches
            var branches = await _gitService.GetBranchesAsync(repoPath);
            var branchNames = branches
                .Where(b => !b.IsRemote)
                .Select(b => b.Name)
                .OrderBy(n => n)
                .ToList();

            AvailableBranches = new ObservableCollection<string>(branchNames);

            SourceBranch = ResolveBranchSelection(branchNames, preselectedSourceBranch)
                ?? branchNames.FirstOrDefault()
                ?? string.Empty;

            TargetBranch = ResolveBranchSelection(branchNames, preselectedTargetBranch, SourceBranch)
                ?? branchNames.FirstOrDefault(b => b == "main" && b != SourceBranch)
                ?? branchNames.FirstOrDefault(b => b == "master" && b != SourceBranch)
                ?? branchNames.FirstOrDefault(b => b == "develop" && b != SourceBranch)
                ?? branchNames.FirstOrDefault(b => b != SourceBranch)
                ?? branchNames.FirstOrDefault()
                ?? string.Empty;

            // Check capabilities
            var caps = _pullRequestService.GetCapabilities(repoPath);
            SupportsDraft = caps.HasFlag(PullRequestCapabilities.DraftPullRequests);
            SupportsRequiredReviewers = caps.HasFlag(PullRequestCapabilities.RequiredReviewers)
                || await IsAzureDevOpsRepositoryAsync(repoPath);

            // Auto-populate title/body from single commit
            if (!string.IsNullOrEmpty(SourceBranch) && !string.IsNullOrEmpty(TargetBranch) && SourceBranch != TargetBranch)
                await TryPopulateFromSingleCommitAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to initialize: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
    }

    private async Task TryPopulateFromSingleCommitAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                var countResult = GitCliHelpers.RunGitArgs(_repoPath, "rev-list", "--count", $"{TargetBranch}..{SourceBranch}");
                if (countResult.ExitCode != 0 || !int.TryParse(countResult.Output.Trim(), out var count) || count != 1)
                    return;

                var logResult = GitCliHelpers.RunGitArgs(_repoPath, "log", $"{TargetBranch}..{SourceBranch}", "-1", "--format=%s%n%b");
                if (logResult.ExitCode != 0) return;

                var output = logResult.Output.Trim();
                var firstNewline = output.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    Title = output[..firstNewline].Trim();
                    Body = output[(firstNewline + 1)..].Trim();
                }
                else
                {
                    Title = output;
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warn("PR", $"Failed to auto-populate from single commit: {ex.Message}");
        }
    }

    private static string? ResolveBranchSelection(IEnumerable<string> branchNames, string? preferredBranch, string? excludeBranch = null)
    {
        if (string.IsNullOrWhiteSpace(preferredBranch))
            return null;

        return branchNames.FirstOrDefault(branch =>
            string.Equals(branch, preferredBranch, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(branch, excludeBranch, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnRequiredReviewerSearchTextChanged(string value)
    {
        ScheduleReviewerSearch(ReviewerBucket.Required, value);
    }

    partial void OnOptionalReviewerSearchTextChanged(string value)
    {
        ScheduleReviewerSearch(ReviewerBucket.Optional, value);
    }

    [RelayCommand]
    private void AddRequiredReviewer(ReviewerInfo? reviewer)
    {
        AddReviewerToBucket(reviewer, ReviewerBucket.Required);
    }

    [RelayCommand]
    private void AddOptionalReviewer(ReviewerInfo? reviewer)
    {
        AddReviewerToBucket(reviewer, ReviewerBucket.Optional);
    }

    [RelayCommand]
    private void RemoveReviewer(ReviewerInfo? reviewer)
    {
        if (reviewer != null)
            SelectedReviewers.Remove(reviewer);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        // Validation
        if (string.IsNullOrWhiteSpace(SourceBranch))
        {
            ErrorMessage = "Source branch is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(TargetBranch))
        {
            ErrorMessage = "Target branch is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Title is required.";
            return;
        }
        if (SourceBranch == TargetBranch)
        {
            ErrorMessage = "Source and target branches must be different.";
            return;
        }

        IsSubmitting = true;
        ErrorMessage = null;

        try
        {
            var pr = await _pullRequestService.CreatePullRequestAsync(
                _repoPath, Title, Body, SourceBranch, TargetBranch, IsDraft);

            // Assign reviewers if any selected
            if (SelectedReviewers.Count > 0)
            {
                try
                {
                    await _pullRequestService.RequestReviewersAsync(
                        _repoPath, pr.Number, SelectedReviewers);
                }
                catch (Exception ex)
                {
                    Log.Warn("PR", $"Failed to assign reviewers: {ex.Message}");
                    // Don't fail the create — PR was already created
                }
            }

            PullRequestCreated?.Invoke(this, pr);
            CreateCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create pull request: {ex.Message}";
            Log.Error("PR", ErrorMessage);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CreateCancelled?.Invoke(this, EventArgs.Empty);
    }

    public IEnumerable<ReviewerInfo> RequiredSelectedReviewers =>
        SelectedReviewers.Where(r => r.IsRequired);

    public IEnumerable<ReviewerInfo> OptionalSelectedReviewers =>
        SelectedReviewers.Where(r => !r.IsRequired);

    public bool HasRequiredSelectedReviewers => SelectedReviewers.Any(r => r.IsRequired);

    public bool HasOptionalSelectedReviewers => SelectedReviewers.Any(r => !r.IsRequired);

    private async Task<bool> IsAzureDevOpsRepositoryAsync(string repoPath)
    {
        var remotes = await _gitService.GetRemotesAsync(repoPath);
        var defaultRemote = remotes.FirstOrDefault(r => r.Name == "origin") ?? remotes.FirstOrDefault();
        return defaultRemote?.IsAzureDevOps == true;
    }

    private void OnSelectedReviewersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (ReviewerInfo reviewer in e.OldItems)
            {
                reviewer.PropertyChanged -= OnSelectedReviewerPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (ReviewerInfo reviewer in e.NewItems)
            {
                reviewer.PropertyChanged += OnSelectedReviewerPropertyChanged;
            }
        }

        NotifyReviewerBucketsChanged();
    }

    private void OnSelectedReviewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewerInfo.IsRequired))
        {
            NotifyReviewerBucketsChanged();
        }
    }

    private void NotifyReviewerBucketsChanged()
    {
        OnPropertyChanged(nameof(RequiredSelectedReviewers));
        OnPropertyChanged(nameof(OptionalSelectedReviewers));
        OnPropertyChanged(nameof(HasRequiredSelectedReviewers));
        OnPropertyChanged(nameof(HasOptionalSelectedReviewers));
    }

    private void AddReviewerToBucket(ReviewerInfo? reviewer, ReviewerBucket bucket)
    {
        if (reviewer == null)
            return;

        var isRequired = bucket == ReviewerBucket.Required;
        var existing = SelectedReviewers.FirstOrDefault(r => r.Identifier == reviewer.Identifier && r.Kind == reviewer.Kind);

        if (existing != null)
        {
            existing.IsRequired = isRequired;
        }
        else
        {
            SelectedReviewers.Add(new ReviewerInfo
            {
                Identifier = reviewer.Identifier,
                DisplayName = reviewer.DisplayName,
                SecondaryText = reviewer.SecondaryText,
                AvatarUrl = reviewer.AvatarUrl,
                Kind = reviewer.Kind,
                IsRequired = isRequired
            });
        }

        ResetReviewerSearch(bucket);
        NotifyReviewerBucketsChanged();
    }

    private void ScheduleReviewerSearch(ReviewerBucket bucket, string searchText)
    {
        var cts = new CancellationTokenSource();
        var previous = bucket == ReviewerBucket.Required
            ? Interlocked.Exchange(ref _requiredReviewerSearchCts, cts)
            : Interlocked.Exchange(ref _optionalReviewerSearchCts, cts);

        previous?.Cancel();
        previous?.Dispose();

        if (string.IsNullOrWhiteSpace(searchText) || string.IsNullOrWhiteSpace(_repoPath))
        {
            ClearReviewerSearch(bucket);
            return;
        }

        RunDebouncedReviewerSearchAsync(bucket, searchText.Trim(), cts.Token)
            .FireAndForget(nameof(RunDebouncedReviewerSearchAsync), isUserAction: false);
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
                .Where(r => !SelectedReviewers.Any(s =>
                    s.Identifier == r.Identifier &&
                    s.Kind == r.Kind &&
                    s.IsRequired == targetIsRequired))
                .ToList();

            SetSearchResults(bucket, filtered);
            SetSearchStatus(bucket, filtered.Count == 0 ? $"No reviewers matching \"{searchText}\"" : null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex.Message.Contains("No GitHub PAT", StringComparison.OrdinalIgnoreCase)
                                || ex.Message.Contains("No Azure DevOps PAT", StringComparison.OrdinalIgnoreCase)
                                || ex.Message.Contains("No PAT", StringComparison.OrdinalIgnoreCase))
        {
            SetSearchStatus(bucket, "Reviewer search requires provider credentials in Settings.");
            Log.Warn("PR", $"Reviewer search failed: {ex.Message}");
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

    private enum ReviewerBucket
    {
        Required,
        Optional
    }
}
