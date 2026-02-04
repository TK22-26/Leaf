using System;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - GitFlow operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Initialize GitFlow in the current repository.
    /// </summary>
    [RelayCommand]
    public async Task InitializeGitFlowAsync()
    {
        if (SelectedRepository == null) return;

        var dialog = new Views.GitFlowInitDialog(_gitFlowService, _settingsService, SelectedRepository.Path)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            StatusMessage = "GitFlow initialized successfully";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Start a new GitFlow feature branch.
    /// </summary>
    [RelayCommand]
    public async Task StartFeatureAsync()
    {
        if (SelectedRepository == null) return;

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
        {
            await _dialogService.ShowInformationAsync(
                "GitFlow is not initialized in this repository.\n\nPlease initialize GitFlow first.",
                "GitFlow Not Initialized");
            return;
        }

        var dialog = new Views.StartBranchDialog(_gitFlowService, _gitService, SelectedRepository.Path, Models.GitFlowBranchType.Feature)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true)
        {
            StatusMessage = $"Started feature {dialog.BranchName}";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Start a new GitFlow release branch.
    /// </summary>
    [RelayCommand]
    public async Task StartReleaseAsync()
    {
        if (SelectedRepository == null) return;

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
        {
            await _dialogService.ShowInformationAsync(
                "GitFlow is not initialized in this repository.\n\nPlease initialize GitFlow first.",
                "GitFlow Not Initialized");
            return;
        }

        var dialog = new Views.StartBranchDialog(_gitFlowService, _gitService, SelectedRepository.Path, Models.GitFlowBranchType.Release)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true)
        {
            StatusMessage = $"Started release {dialog.BranchName}";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Start a new GitFlow hotfix branch.
    /// </summary>
    [RelayCommand]
    public async Task StartHotfixAsync()
    {
        if (SelectedRepository == null) return;

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
        {
            await _dialogService.ShowInformationAsync(
                "GitFlow is not initialized in this repository.\n\nPlease initialize GitFlow first.",
                "GitFlow Not Initialized");
            return;
        }

        var dialog = new Views.StartBranchDialog(_gitFlowService, _gitService, SelectedRepository.Path, Models.GitFlowBranchType.Hotfix)
        {
            Owner = _ownerWindow
        };

        if (dialog.ShowDialog() == true)
        {
            StatusMessage = $"Started hotfix {dialog.BranchName}";
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Finish a GitFlow branch (feature, release, or hotfix).
    /// </summary>
    [RelayCommand]
    public async Task FinishGitFlowBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null) return;

        var config = await _gitFlowService.GetConfigAsync(SelectedRepository.Path);
        if (config == null)
        {
            await _dialogService.ShowInformationAsync(
                "GitFlow is not initialized in this repository.",
                "GitFlow Not Initialized");
            return;
        }

        var branchType = _gitFlowService.GetBranchType(branch.Name, config);
        var flowName = _gitFlowService.GetFlowName(branch.Name, config);

        if (branchType == Models.GitFlowBranchType.None || string.IsNullOrEmpty(flowName))
        {
            await _dialogService.ShowInformationAsync(
                "This branch is not a GitFlow branch (feature, release, or hotfix).",
                "Not a GitFlow Branch");
            return;
        }

        var dialog = new Views.FinishBranchDialog(_gitFlowService, SelectedRepository.Path, branch.Name, branchType, flowName)
        {
            Owner = _ownerWindow
        };

        var result = dialog.ShowDialog();

        // Always refresh to detect conflicts or other state changes
        await RefreshAsync();

        if (result == true)
        {
            StatusMessage = $"Finished {branchType.ToString().ToLower()} {flowName}";
        }
    }

    /// <summary>
    /// Publish a GitFlow branch to remote.
    /// </summary>
    [RelayCommand]
    public async Task PublishGitFlowBranchAsync(BranchInfo branch)
    {
        if (SelectedRepository == null || branch == null) return;

        var config = await _gitFlowService.GetConfigAsync(SelectedRepository.Path);
        if (config == null)
        {
            await _dialogService.ShowInformationAsync(
                "GitFlow is not initialized in this repository.",
                "GitFlow Not Initialized");
            return;
        }

        var branchType = _gitFlowService.GetBranchType(branch.Name, config);
        var flowName = _gitFlowService.GetFlowName(branch.Name, config);

        if (branchType == Models.GitFlowBranchType.None || string.IsNullOrEmpty(flowName))
        {
            await _dialogService.ShowInformationAsync(
                "This branch is not a GitFlow branch.",
                "Not a GitFlow Branch");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Publishing {branchType.ToString().ToLower()} {flowName}...";

            var progress = new Progress<string>(msg => StatusMessage = msg);

            switch (branchType)
            {
                case Models.GitFlowBranchType.Feature:
                    await _gitFlowService.PublishFeatureAsync(SelectedRepository.Path, flowName, progress);
                    break;
                case Models.GitFlowBranchType.Release:
                    await _gitFlowService.PublishReleaseAsync(SelectedRepository.Path, flowName, progress);
                    break;
                case Models.GitFlowBranchType.Hotfix:
                    await _gitFlowService.PublishHotfixAsync(SelectedRepository.Path, flowName, progress);
                    break;
            }

            StatusMessage = $"Published {branchType.ToString().ToLower()} {flowName}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Publish failed: {ex.Message}";
            await _dialogService.ShowErrorAsync(
                $"Failed to publish branch:\n\n{ex.Message}",
                "Publish Failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Get GitFlow configuration for the selected repository.
    /// </summary>
    public async Task<GitFlowConfig?> GetGitFlowConfigAsync()
    {
        if (SelectedRepository == null) return null;
        return await _gitFlowService.GetConfigAsync(SelectedRepository.Path);
    }

    /// <summary>
    /// Get GitFlow status for the selected repository.
    /// </summary>
    public async Task<GitFlowStatus?> GetGitFlowStatusAsync()
    {
        if (SelectedRepository == null) return null;
        return await _gitFlowService.GetStatusAsync(SelectedRepository.Path);
    }

    /// <summary>
    /// Get suggested version for release or hotfix.
    /// </summary>
    public async Task<SemanticVersion?> GetSuggestedVersionAsync(GitFlowBranchType branchType)
    {
        if (SelectedRepository == null) return null;
        try
        {
            return await _gitFlowService.SuggestNextVersionAsync(SelectedRepository.Path, branchType);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get repository info for the selected repository.
    /// </summary>
    public async Task<RepositoryInfo?> GetRepositoryInfoAsync()
    {
        if (SelectedRepository == null) return null;
        return await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
    }

    /// <summary>
    /// Create a GitFlow branch (feature, release, or hotfix).
    /// </summary>
    public async Task CreateGitFlowBranchAsync(GitFlowBranchType branchType, string name)
    {
        if (SelectedRepository == null)
            throw new InvalidOperationException("No repository selected.");

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
            throw new InvalidOperationException("GitFlow is not initialized in this repository.");

        var progress = new Progress<string>(msg => StatusMessage = msg);

        switch (branchType)
        {
            case GitFlowBranchType.Feature:
                await _gitFlowService.StartFeatureAsync(SelectedRepository.Path, name, progress);
                StatusMessage = $"Started feature '{name}'";
                break;
            case GitFlowBranchType.Release:
                await _gitFlowService.StartReleaseAsync(SelectedRepository.Path, name, progress);
                StatusMessage = $"Started release '{name}'";
                break;
            case GitFlowBranchType.Hotfix:
                await _gitFlowService.StartHotfixAsync(SelectedRepository.Path, name, progress);
                StatusMessage = $"Started hotfix '{name}'";
                break;
            default:
                throw new ArgumentException($"Unsupported branch type: {branchType}");
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Classifies branches by their GitFlow type based on the GitFlow configuration.
    /// Sets the GitFlowType property on each branch for proper coloring.
    /// </summary>
    private static void ClassifyBranchesByGitFlowType(IEnumerable<BranchInfo> branches, GitFlowConfig config)
    {
        foreach (var branch in branches)
        {
            branch.GitFlowType = GetGitFlowBranchType(branch.Name, config);
        }
    }

    /// <summary>
    /// Determines the GitFlow branch type for a branch name.
    /// </summary>
    private static GitFlowBranchType GetGitFlowBranchType(string branchName, GitFlowConfig config)
    {
        // Check for exact matches first (main/develop)
        if (branchName.Equals(config.MainBranch, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Main;

        if (branchName.Equals(config.DevelopBranch, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Develop;

        // Check for prefixed branches
        if (branchName.StartsWith(config.FeaturePrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Feature;

        if (branchName.StartsWith(config.ReleasePrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Release;

        if (branchName.StartsWith(config.HotfixPrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Hotfix;

        if (branchName.StartsWith(config.SupportPrefix, StringComparison.OrdinalIgnoreCase))
            return GitFlowBranchType.Support;

        return GitFlowBranchType.None;
    }
}
