using System;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - GitFlow operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Event raised when the GitFlow action menu should be shown on the action bar button.
    /// </summary>
    public event EventHandler? RequestGitFlowActionMenu;

    /// <summary>
    /// GitFlow action bar button — opens init dialog if not initialized, or shows action menu if initialized.
    /// </summary>
    [RelayCommand]
    public async Task GitFlowButtonAsync()
    {
        if (SelectedRepository == null) return;

        if (!IsGitFlowInitialized)
        {
            await InitializeGitFlowAsync();
            return;
        }

        RequestGitFlowActionMenu?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Initialize GitFlow in the current repository.
    /// </summary>
    [RelayCommand]
    public async Task InitializeGitFlowAsync()
    {
        if (SelectedRepository == null) return;

        var dialog = new Views.GitFlowInitDialog(_gitFlowService, _settingsService, SelectedRepository.Path);
        if (await _dialogService.ShowDialogAsync(dialog) && dialog.Result != null)
        {
            StatusMessage = "GitFlow initialized successfully";
            SelectedRepository.BranchesLoaded = false;
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Start a new GitFlow feature branch.
    /// </summary>
    [RelayCommand]
    public Task StartFeatureAsync() => StartGitFlowBranchDialogAsync(Models.GitFlowBranchType.Feature, "feature");

    /// <summary>
    /// Start a new GitFlow release branch.
    /// </summary>
    [RelayCommand]
    public Task StartReleaseAsync() => StartGitFlowBranchDialogAsync(Models.GitFlowBranchType.Release, "release");

    /// <summary>
    /// Start a new GitFlow hotfix branch.
    /// </summary>
    [RelayCommand]
    public Task StartHotfixAsync() => StartGitFlowBranchDialogAsync(Models.GitFlowBranchType.Hotfix, "hotfix");

    /// <summary>
    /// Shared flow for the three Start* commands: ensure GitFlow is
    /// initialized, show the StartBranchDialog for <paramref name="branchType"/>,
    /// and refresh on success. The three entry points differ only in the
    /// branch type and the status-message noun, so the body is parameterized.
    /// </summary>
    private async Task StartGitFlowBranchDialogAsync(Models.GitFlowBranchType branchType, string statusNoun)
    {
        if (!await EnsureGitFlowInitializedAsync()) return;

        var dialog = new Views.StartBranchDialog(_gitFlowService, _gitService, SelectedRepository!.Path, branchType);
        if (await _dialogService.ShowDialogAsync(dialog))
        {
            StatusMessage = $"Started {statusNoun} {dialog.BranchName}";
            SelectedRepository.BranchesLoaded = false;
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Ensure a repository is selected and GitFlow is initialized for it.
    /// Shows the "GitFlow Not Initialized" information dialog and returns
    /// false when the caller should abort; returns true when it's safe to
    /// proceed. Consolidates the guard that was copy-pasted across the
    /// Start* commands.
    /// </summary>
    private async Task<bool> EnsureGitFlowInitializedAsync()
    {
        if (SelectedRepository == null) return false;

        var isInitialized = await _gitFlowService.IsInitializedAsync(SelectedRepository.Path);
        if (!isInitialized)
        {
            await _dialogService.ShowInformationAsync(
                "GitFlow is not initialized in this repository.\n\nPlease initialize GitFlow first.",
                "GitFlow Not Initialized");
            return false;
        }

        return true;
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

        var dialog = new Views.FinishBranchDialog(_gitFlowService, SelectedRepository.Path, branch.Name, branchType, flowName);
        var finished = await _dialogService.ShowDialogAsync(dialog);

        // Always refresh to detect conflicts or other state changes
        if (SelectedRepository != null)
            SelectedRepository.BranchesLoaded = false;
        await RefreshAsync();

        if (finished)
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
            await BeginBusyAsync($"Publishing {branchType.ToString().ToLower()} {flowName}...");

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
            SelectedRepository.BranchesLoaded = false;
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
        catch (Exception ex) when (ex is InvalidOperationException
                                or IOException
                                or UnauthorizedAccessException)
        {
            // Version detection is best-effort — returning null lets the
            // dialog fall back to a blank name field.
            Log.Info("GitFlow", $"SuggestNextVersion failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get repository info for the selected repository.
    /// </summary>
    public async Task<RepositoryInfo?> GetRepositoryInfoAsync()
    {
        if (SelectedRepository == null) return null;
        return await _gitService.GetRepositoryInfoFastAsync(SelectedRepository.Path, cancellationToken: CurrentRepositoryToken);
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

        SelectedRepository.BranchesLoaded = false;
        await RefreshAsync();
    }

    /// <summary>
    /// Snapshot of the data a GitFlow quick-create flyout needs to render.
    /// Groups the config-driven prefix and an optional suggested version so
    /// the code-behind has a single await instead of two orchestrated calls.
    /// </summary>
    /// <param name="Prefix">Branch prefix from GitFlow config (e.g. "feature/"), or a fallback when GitFlow isn't initialized.</param>
    /// <param name="SuggestedVersion">Next semantic version for release/hotfix flows; null for features or when no version can be derived.</param>
    public record GitFlowQuickCreateContext(string Prefix, SemanticVersion? SuggestedVersion);

    /// <summary>
    /// Gather the prefix and suggested version for a GitFlow quick-create
    /// flyout. Falls back to <paramref name="fallbackPrefix"/> when GitFlow
    /// isn't initialized or when the repo isn't selected — the UI still
    /// needs a prefix to render the preview. Version suggestion is only
    /// meaningful for release/hotfix and is always null for features.
    /// </summary>
    public async Task<GitFlowQuickCreateContext> PrepareGitFlowQuickCreateAsync(
        GitFlowBranchType branchType,
        string fallbackPrefix)
    {
        var config = SelectedRepository == null
            ? null
            : await _gitFlowService.GetConfigAsync(SelectedRepository.Path);

        if (config == null)
            return new GitFlowQuickCreateContext(fallbackPrefix, SuggestedVersion: null);

        var prefix = branchType switch
        {
            GitFlowBranchType.Feature => config.FeaturePrefix,
            GitFlowBranchType.Release => config.ReleasePrefix,
            GitFlowBranchType.Hotfix => config.HotfixPrefix,
            _ => fallbackPrefix
        };

        SemanticVersion? suggested = null;
        if (branchType is GitFlowBranchType.Release or GitFlowBranchType.Hotfix)
            suggested = await GetSuggestedVersionAsync(branchType);

        return new GitFlowQuickCreateContext(prefix, suggested);
    }

    /// <summary>
    /// Orchestrates the full GitFlow quick-create flow: check for
    /// uncommitted changes, optionally stash them, then create the branch.
    /// When the working tree is dirty the user is prompted via
    /// IDialogService; answering Cancel (or closing the dialog) aborts the
    /// flow and returns false. <paramref name="progress"/> receives status
    /// strings so the calling popup can update its inline progress label
    /// without this method touching any WPF types.
    /// </summary>
    /// <returns>True if the branch was created, false if the user cancelled.</returns>
    public async Task<bool> StartGitFlowBranchWithStashCheckAsync(
        GitFlowBranchType branchType,
        string name,
        IProgress<string>? progress = null)
    {
        if (SelectedRepository == null)
            throw new InvalidOperationException("No repository selected.");

        progress?.Report("Checking for uncommitted changes...");

        var repoInfo = await GetRepositoryInfoAsync();
        if (repoInfo?.IsDirty == true)
        {
            var result = await _dialogService.ShowMessageAsync(
                "You have uncommitted changes.\n\nWould you like to stash them first?",
                "Uncommitted Changes",
                System.Windows.MessageBoxButton.YesNoCancel);

            if (result == System.Windows.MessageBoxResult.Cancel)
                return false;

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                progress?.Report("Stashing changes...");
                await StashChangesAsync($"Auto-stash before {branchType.ToString().ToLower()} '{name}'");
            }
        }

        progress?.Report($"Creating {branchType.ToString().ToLower()} branch...");
        await CreateGitFlowBranchAsync(branchType, name);
        return true;
    }

    /// <summary>
    /// Finish a GitFlow branch by type + flow name. Resolves the full
    /// branch name via the config prefix, locates the matching BranchInfo
    /// on the selected repository, and dispatches to the existing
    /// <see cref="FinishGitFlowBranchAsync(BranchInfo)"/> flow. Used by
    /// the sidebar's GitFlow action menu, which only knows the flow name.
    /// </summary>
    public async Task FinishGitFlowBranchByNameAsync(GitFlowBranchType branchType, string flowName)
    {
        if (SelectedRepository == null) return;

        var config = await GetGitFlowConfigAsync();
        if (config == null) return;

        var prefix = branchType switch
        {
            GitFlowBranchType.Feature => config.FeaturePrefix,
            GitFlowBranchType.Release => config.ReleasePrefix,
            GitFlowBranchType.Hotfix => config.HotfixPrefix,
            _ => string.Empty
        };
        var fullName = prefix + flowName;

        var branch = SelectedRepository.LocalBranches
            .FirstOrDefault(b => b.Name.Equals(fullName, StringComparison.OrdinalIgnoreCase));

        if (branch != null)
            await FinishGitFlowBranchAsync(branch);
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
