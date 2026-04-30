using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Leaf.Services;
using Leaf.Services.Shortcuts;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private DateTime _lastSpacePress = DateTime.MinValue;
    private static readonly TimeSpan DoubleTapThreshold = TimeSpan.FromMilliseconds(300);
    private GridLength _savedRightPanelWidth = new(350);
    private readonly TaskCompletionSource<object?> _firstRenderTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IGitService _gitService;
    private readonly IRepositoryManagementService _repositoryService;
    private readonly IShortcutService _shortcutService;
    private readonly MainViewModel _viewModel;
    private Task? _startupInitializationTask;

    public MainWindow(IServiceProvider services)
    {
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        InitializeComponent();

        _viewModel = services.GetRequiredService<MainViewModel>();
        _gitService = services.GetRequiredService<IGitService>();
        _repositoryService = services.GetRequiredService<IRepositoryManagementService>();
        _shortcutService = services.GetRequiredService<IShortcutService>();

        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.RequestGitFlowActionMenu += ViewModel_RequestGitFlowActionMenu;

        NotificationHostControl.NotificationService = services.GetRequiredService<INotificationService>();

        // §5.9 Phase 1: shortcuts are owned by IShortcutService and
        // applied to InputBindings here so the user's overrides take
        // effect immediately. Re-applies whenever a binding changes.
        ApplyShortcuts();
        _shortcutService.GestureChanged += OnShortcutGestureChanged;

        ContentRendered += OnContentRendered;
        // Without this, MainViewModel.Dispose is never called — every
        // unsubscribe added for plan §1.6 would be dead code.
        Closed += OnWindowClosed;
    }

    private void OnShortcutGestureChanged(object? sender, string? commandId)
    {
        // The service can fire with a specific id (single rebind) or
        // null (ResetAll). Either way the cheapest correct response is
        // to rebuild every App-scope binding — there are only a handful
        // and no per-row state to preserve.
        ApplyShortcuts();
    }

    private void ApplyShortcuts()
    {
        InputBindings.Clear();

        // View / window-chrome
        Bind(ShortcutCommandId.View.ToggleTerminal, _viewModel.ToggleTerminalCommand);
        Bind(ShortcutCommandId.View.ToggleCommandPalette, _viewModel.ToggleCommandPaletteCommand);
        Bind(ShortcutCommandId.View.ReportIssue, _viewModel.ReportIssueCommand);

        // Repository operations. Fetch defaults to the all-remotes
        // command — matches the toolbar's "Fetch" button. Refresh is a
        // distinct id with no default gesture; user assigns from
        // Settings if they want a separate keystroke from F5/Fetch.
        Bind(ShortcutCommandId.Repository.Fetch, _viewModel.FetchAllCommand);
        Bind(ShortcutCommandId.Repository.Pull, _viewModel.PullCommand);
        Bind(ShortcutCommandId.Repository.Push, _viewModel.PushCommand);
        Bind(ShortcutCommandId.Repository.Refresh, _viewModel.RefreshCommand);

        // Branch
        Bind(ShortcutCommandId.Branch.Create, _viewModel.CreateBranchCommand);

        // Commit / stash. The Commit shortcut is intentionally not
        // wired here — the commit input box's own Ctrl+Enter binding
        // already handles it scoped to that control, and adding a
        // Window-level binding would steal Ctrl+Enter from any text
        // box that wants to insert a newline.
        Bind(ShortcutCommandId.Commit.Stash, _viewModel.StashCommand);
        Bind(ShortcutCommandId.Commit.PopStash, _viewModel.PopStashCommand);
    }

    private void Bind(string commandId, ICommand command)
    {
        var gesture = _shortcutService.GetGesture(commandId);
        if (gesture == null) return; // user has unbound this shortcut
        InputBindings.Add(new KeyBinding(command, gesture));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // Mirror the subscriptions we added here so the window doesn't
        // root the VM after close.
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.RequestGitFlowActionMenu -= ViewModel_RequestGitFlowActionMenu;
        _shortcutService.GestureChanged -= OnShortcutGestureChanged;
        ContentRendered -= OnContentRendered;
        Closed -= OnWindowClosed;

        _viewModel.Dispose();
    }

    public Task InitializeStartupAsync()
    {
        _startupInitializationTask ??= InitializeStartupCoreAsync();
        return _startupInitializationTask;
    }

    public Task WaitForFirstRenderAsync() => _firstRenderTcs.Task;

    private async Task InitializeStartupCoreAsync()
    {
        try
        {
            await _viewModel.InitializeAfterFirstRenderAsync(restoreLastSelection: App.InitialRepoPath is null);

            if (App.InitialRepoPath is { } initialRepo)
            {
                Log.Info("App", $"Opening repository from --repo flag: {initialRepo}");
                var repoInfo = await _gitService.GetRepositoryInfoFastAsync(initialRepo);
                _repositoryService.AddRepository(repoInfo);
                await _viewModel.SelectRepositoryAsync(repoInfo, fetchInBackground: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("App", $"Startup initialization failed: {ex.Message}", ex);
            throw;
        }
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        _firstRenderTcs.TrySetResult(null);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;

        // Don't intercept space when typing in a text input
        if (Keyboard.FocusedElement is TextBox) return;

        var now = DateTime.UtcNow;
        if (now - _lastSpacePress <= DoubleTapThreshold)
        {
            _lastSpacePress = DateTime.MinValue; // Reset to avoid triple-tap
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ToggleCommandPaletteCommand.Execute(null);
                e.Handled = true;
            }
        }
        else
        {
            _lastSpacePress = now;
        }
    }

    private void RepoPane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ToggleRepoPaneCommand.Execute(null);
        }
    }



    private void TerminalSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateTerminalHeight(TerminalRow.ActualHeight);
        }
    }

    private void RepoPaneSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var newWidth = viewModel.RepoPaneWidth + e.HorizontalChange;
            newWidth = Math.Max(150, newWidth);
            viewModel.RepoPaneWidth = newWidth;
        }

        // GridSplitter's built-in behavior converts Column 0 from Auto to a fixed pixel width.
        // Reset it so the column sizes to content — critical for collapse to shrink the space.
        MainPanelGrid.ColumnDefinitions[0].Width = GridLength.Auto;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "IsGraphMode" && sender is MainViewModel vm)
        {
            if (vm.IsGraphMode)
            {
                RightPanelColumn.Width = _savedRightPanelWidth;
            }
            else
            {
                if (RightPanelColumn.Width.Value > 0)
                    _savedRightPanelWidth = RightPanelColumn.Width;
                RightPanelColumn.Width = new GridLength(0);
            }
        }
    }

    private void RepoPaneSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateRepoPaneWidth(RepoPaneGrid.ActualWidth);
        }
    }

    private void ViewModel_RequestGitFlowActionMenu(object? sender, EventArgs e)
    {
        var button = GitFlowActionBarButton;
        if (button?.ContextMenu == null) return;

        var menu = button.ContextMenu;
        menu.Items.Clear();

        var startFeature = new MenuItem { Header = "Start Feature" };
        startFeature.Click += (_, _) => _viewModel.StartFeatureCommand.Execute(null);
        menu.Items.Add(startFeature);

        var startRelease = new MenuItem { Header = "Start Release" };
        startRelease.Click += (_, _) => _viewModel.StartReleaseCommand.Execute(null);
        menu.Items.Add(startRelease);

        var startHotfix = new MenuItem { Header = "Start Hotfix" };
        startHotfix.Click += (_, _) => _viewModel.StartHotfixCommand.Execute(null);
        menu.Items.Add(startHotfix);

        menu.Items.Add(new Separator());

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => _viewModel.InitializeGitFlowCommand.Execute(null);
        menu.Items.Add(settings);

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
