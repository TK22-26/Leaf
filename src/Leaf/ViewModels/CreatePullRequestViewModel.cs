using System.Collections.ObjectModel;
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
    [ObservableProperty] private string _reviewerSearchText = string.Empty;

    // Branch lists for dropdowns
    [ObservableProperty] private ObservableCollection<string> _availableBranches = [];

    // Reviewer search
    [ObservableProperty] private ObservableCollection<ReviewerInfo> _reviewerSearchResults = [];
    [ObservableProperty] private ObservableCollection<ReviewerInfo> _selectedReviewers = [];

    // State
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private bool _isSearchingReviewers;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _reviewerSearchStatus;
    [ObservableProperty] private bool _supportsDraft;
    [ObservableProperty] private bool _supportsRequiredReviewers;

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
    }

    /// <summary>
    /// Initialize form for a given repository. Loads branch lists and capabilities.
    /// </summary>
    public async Task InitializeAsync(string repoPath, string? preselectedSourceBranch = null)
    {
        _repoPath = repoPath;
        ErrorMessage = null;
        Title = string.Empty;
        Body = string.Empty;
        IsDraft = false;
        SelectedReviewers.Clear();
        ReviewerSearchResults.Clear();
        ReviewerSearchText = string.Empty;

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

            // Set source branch
            SourceBranch = preselectedSourceBranch ?? branchNames.FirstOrDefault() ?? string.Empty;

            // Default target to main/master/develop
            TargetBranch = branchNames.FirstOrDefault(b => b == "main")
                ?? branchNames.FirstOrDefault(b => b == "master")
                ?? branchNames.FirstOrDefault(b => b == "develop")
                ?? branchNames.FirstOrDefault()
                ?? string.Empty;

            // Check capabilities
            var caps = _pullRequestService.GetCapabilities(repoPath);
            SupportsDraft = caps.HasFlag(PullRequestCapabilities.DraftPullRequests);
            SupportsRequiredReviewers = caps.HasFlag(PullRequestCapabilities.RequiredReviewers);

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

    [RelayCommand]
    private async Task SearchReviewersAsync()
    {
        if (string.IsNullOrWhiteSpace(ReviewerSearchText) || string.IsNullOrWhiteSpace(_repoPath))
        {
            ReviewerSearchResults.Clear();
            ReviewerSearchStatus = null;
            return;
        }

        IsSearchingReviewers = true;
        ReviewerSearchStatus = null;
        ReviewerSearchResults.Clear();

        try
        {
            var results = await _pullRequestService.SearchReviewersAsync(_repoPath, ReviewerSearchText);
            // Exclude already selected reviewers
            var filtered = results
                .Where(r => !SelectedReviewers.Any(s => s.Identifier == r.Identifier && s.Kind == r.Kind))
                .ToList();
            ReviewerSearchResults = new ObservableCollection<ReviewerInfo>(filtered);
            ReviewerSearchStatus = filtered.Count == 0 ? $"No collaborators matching \"{ReviewerSearchText}\"" : null;
        }
        catch (Exception ex) when (ex.Message.Contains("No GitHub PAT", StringComparison.OrdinalIgnoreCase)
                                || ex.Message.Contains("No PAT", StringComparison.OrdinalIgnoreCase))
        {
            ReviewerSearchStatus = "Reviewer search requires a GitHub PAT. Configure it in Settings.";
            Log.Warn("PR", $"Reviewer search failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            ReviewerSearchStatus = $"Search failed: {ex.Message}";
            Log.Error("PR", $"Reviewer search failed: {ex.Message}");
        }
        finally
        {
            IsSearchingReviewers = false;
        }
    }

    [RelayCommand]
    private void AddReviewer(ReviewerInfo? reviewer)
    {
        if (reviewer == null) return;
        if (!SelectedReviewers.Any(r => r.Identifier == reviewer.Identifier && r.Kind == reviewer.Kind))
        {
            SelectedReviewers.Add(reviewer);
        }
        ReviewerSearchResults.Clear();
        ReviewerSearchText = string.Empty;
    }

    [RelayCommand]
    private void RemoveReviewer(ReviewerInfo? reviewer)
    {
        if (reviewer != null)
            SelectedReviewers.Remove(reviewer);
    }

    [RelayCommand]
    private void ToggleReviewerRequired(ReviewerInfo? reviewer)
    {
        if (reviewer != null)
            reviewer.IsRequired = !reviewer.IsRequired;
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
}
