using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services.PullRequests;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the pull request detail view.
/// </summary>
public partial class PullRequestDetailViewModel : ObservableObject
{
    private readonly IPullRequestService _pullRequestService;

    [ObservableProperty]
    private PullRequestDetails? _details;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public PullRequestDetailViewModel(IPullRequestService pullRequestService)
    {
        _pullRequestService = pullRequestService;
    }

    /// <summary>
    /// Loads full details for a pull request.
    /// </summary>
    public async Task LoadAsync(string repoPath, int prNumber)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Details = await _pullRequestService.GetPullRequestAsync(repoPath, prNumber);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load PR #{prNumber}: {ex.Message}";
            Debug.WriteLine($"[PR] {ErrorMessage}");
        }
        finally
        {
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
}
