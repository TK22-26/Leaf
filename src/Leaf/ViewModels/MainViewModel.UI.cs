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
    /// Toggle command palette visibility.
    /// </summary>
    [RelayCommand]
    public void ToggleCommandPalette()
    {
        if (CommandPaletteViewModel == null) return;

        if (CommandPaletteViewModel.IsOpen)
            CommandPaletteViewModel.Close();
        else
            CommandPaletteViewModel.Open();
    }

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
    /// Open settings. Pass <paramref name="initialSection"/> (e.g.
    /// <c>"ExternalTools"</c>) to deep-link the user to a specific section
    /// instead of the Clone Path default — used by call sites that
    /// surface a "configure me" prompt and want the user to land directly
    /// on the relevant config screen.
    /// </summary>
    [RelayCommand]
    public async Task OpenSettingsAsync(string? initialSection = null)
    {
        var dialog = new SettingsDialog(
            _credentialService,
            _settingsService,
            _externalToolConfig,
            _externalToolDetector,
            SelectedRepository?.Path,
            initialSection)
        {
            Width = 1000,
            Height = 750
        };
        await _dialogService.ShowDialogAsync(dialog);
        TerminalViewModel?.ReloadSettings();
        WorkingChangesViewModel?.RefreshAiAvailability();
        WorkingChangesViewModel?.RefreshCommitTemplatesEnabled();
        WorkingChangesViewModel?.RefreshSectionContexts();
        if (WorkingChangesViewModel != null)
            await WorkingChangesViewModel.RefreshExternalDiffToolAvailabilityAsync();
        if (CommitDetailViewModel != null)
            await CommitDetailViewModel.RefreshExternalDiffToolAvailabilityAsync();
        await RefreshExternalMergeToolAvailabilityAsync();
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
    public async Task ReportIssueAsync()
    {
        var dialog = new ReportIssueDialog();
        await _dialogService.ShowDialogAsync(dialog);
    }

    /// <summary>
    /// Open the reflog viewer for the currently selected repository.
    /// No-op if no repo is selected. The reflog view raises
    /// <see cref="ReflogViewModel.RepositoryMutated"/> after each
    /// destructive action; we route that through the standard
    /// async-error wrapper instead of a bare <c>async void</c>
    /// lambda so a mid-refresh exception surfaces instead of
    /// crashing the app via <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// </summary>
    [RelayCommand]
    public async Task ShowReflogAsync()
    {
        if (SelectedRepository == null) return;

        var vm = new ReflogViewModel(_gitService, _clipboardService, _dialogService, SelectedRepository.Path);
        void OnReflogMutated(object? sender, EventArgs e)
        {
            // The event fires on the UI thread; FireAndForget's error
            // handler produces a status toast if RefreshAsync throws.
            RefreshAsync().FireAndForget(nameof(ShowReflogAsync), isUserAction: true);
        }
        vm.RepositoryMutated += OnReflogMutated;
        try
        {
            var window = new Views.ReflogWindow(vm);
            await _dialogService.ShowDialogAsync(window);
        }
        finally
        {
            vm.RepositoryMutated -= OnReflogMutated;
        }
        // No extra post-close refresh: the event path above already
        // triggered one for every mutation, and an unconditional
        // second refresh would race with whatever the first refresh
        // is still doing on the UI thread.
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
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException
                                or TaskCanceledException
                                or System.Text.Json.JsonException
                                or InvalidOperationException)
        {
            // Update check is best-effort on startup — network down, GitHub
            // rate-limited, malformed manifest all fall through silently.
            Log.Info("Updates", $"Silent update check failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnTerminalCommandExecuted(object? sender, TerminalCommandExecutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            // Terminal-driven refresh — treat as user action since the user
            // explicitly ran a command.
            AsyncErrorHandler.Handle(ex, nameof(OnTerminalCommandExecuted), isUserAction: true);
        }
    }
}
