using System;
using CommunityToolkit.Mvvm.Input;
using Leaf.Services;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - UI operations (terminal, settings, updates).
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Toggle terminal pane visibility.
    /// </summary>
    [RelayCommand]
    public void ToggleTerminal()
    {
        IsTerminalVisible = !IsTerminalVisible;
    }

    /// <summary>
    /// Toggle repo pane collapsed state.
    /// </summary>
    [RelayCommand]
    public void ToggleRepoPane()
    {
        IsRepoPaneCollapsed = !IsRepoPaneCollapsed;

        // Persist the state
        var settings = _settingsService.LoadSettings();
        settings.IsRepoPaneCollapsed = IsRepoPaneCollapsed;
        _settingsService.SaveSettings(settings);
    }

    public void UpdateRepoPaneWidth(double width)
    {
        if (width <= 0)
        {
            return;
        }

        RepoPaneWidth = width;
        var settings = _settingsService.LoadSettings();
        settings.RepoPaneWidth = width;
        _settingsService.SaveSettings(settings);
    }

    /// <summary>
    /// Open settings.
    /// </summary>
    [RelayCommand]
    public void OpenSettings()
    {
        var dialog = new SettingsDialog(_credentialService, _settingsService)
        {
            Owner = _ownerWindow,
            Width = 1000,
            Height = 750
        };
        dialog.ShowDialog();
        TerminalViewModel?.ReloadSettings();
        WorkingChangesViewModel?.RefreshAiAvailability();
        WorkingChangesViewModel?.RefreshSectionContexts();
        var updatedSettings = _settingsService.LoadSettings();
        if (CommitDetailViewModel != null)
            CommitDetailViewModel.IsCompactFileList = updatedSettings.CompactFileList;
        if (MergeConflictResolutionViewModel != null)
            MergeConflictResolutionViewModel.IsCompactFileList = updatedSettings.CompactFileList;
    }

    public void UpdateTerminalHeight(double height)
    {
        if (height <= 0)
        {
            return;
        }

        TerminalHeight = height;
        var settings = _settingsService.LoadSettings();
        settings.TerminalHeight = height;
        _settingsService.SaveSettings(settings);
    }

    /// <summary>
    /// Check for updates from GitHub releases.
    /// </summary>
    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        var updateService = new UpdateService();
        var updateInfo = await updateService.CheckForUpdatesAsync();

        // Update indicator state
        AvailableUpdate = updateInfo;
        IsUpdateAvailable = updateInfo != null;

        if (updateInfo != null)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                $"A new version of Leaf is available!\n\n" +
                $"Current version: {UpdateService.CurrentVersionString}\n" +
                $"Latest version: v{updateInfo.LatestVersion.Major}.{updateInfo.LatestVersion.Minor}.{updateInfo.LatestVersion.Build}\n\n" +
                $"Would you like to open the download page?",
                "Update Available");

            if (confirmed)
            {
                UpdateService.OpenDownloadPage(updateInfo.ReleaseUrl);
            }
        }
        else
        {
            await _dialogService.ShowInformationAsync(
                $"You're running the latest version of Leaf ({UpdateService.CurrentVersionString}).",
                "No Updates Available");
        }
    }

    /// <summary>
    /// Open dialog to report a new issue via GitHub CLI.
    /// </summary>
    [RelayCommand]
    public void ReportIssue()
    {
        var dialog = new ReportIssueDialog
        {
            Owner = _ownerWindow
        };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Open the releases page on GitHub.
    /// </summary>
    [RelayCommand]
    public void OpenReleasesPage()
    {
        UpdateService.OpenReleasesPage();
    }

    /// <summary>
    /// Check for updates silently on startup (no dialog if up to date).
    /// </summary>
    private async Task CheckForUpdatesSilentlyAsync()
    {
        try
        {
            var updateService = new UpdateService();
            var updateInfo = await updateService.CheckForUpdatesAsync();

            if (updateInfo != null)
            {
                AvailableUpdate = updateInfo;
                IsUpdateAvailable = true;
            }
        }
        catch
        {
            // Silently ignore errors during startup check
        }
    }

    private async void OnTerminalCommandExecuted(object? sender, TerminalCommandExecutedEventArgs e)
    {
        if (SelectedRepository == null)
        {
            return;
        }

        // Refresh after successful git commands to sync the graph.
        if (e.ExitCode == 0)
        {
            await RefreshAsync();
        }
    }
}
