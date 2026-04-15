using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Leaf.Services;
using Leaf.Services.PullRequests;
using Leaf.ViewModels;

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
    private readonly GitService _gitService;
    private readonly RepositoryManagementService _repositoryService;
    private readonly MainViewModel _viewModel;
    private Task? _startupInitializationTask;

    public MainWindow()
    {
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        InitializeComponent();

        // Phase 0: Architecture Glue (MUST be created first)
        // NOTE: Dispatcher injected at composition root - NOT accessed inside services
        var dispatcherService = new DispatcherService(Dispatcher);
        var windowService = new WindowService();
        var repositorySessionFactory = new RepositorySessionFactory();
        var repositoryEventHub = new RepositoryEventHub(dispatcherService);

        // Phase 1: Foundation services
        var notificationService = new NotificationService(dispatcherService);
        var dialogService = new DialogService(dispatcherService, windowService, notificationService);
        var gitCommandRunner = new GitCommandRunner();
        var clipboardService = new ClipboardService();
        var fileSystemService = new FileSystemService();

        // Original services
        var gitService = new GitService();
        var credentialService = new CredentialService();
        var settingsService = new SettingsService();

        // Migrate legacy credentials to new multi-org format
        settingsService.MigrateCredentialsIfNeeded(credentialService);

        var gitFlowService = new GitFlowService(gitService);
        var repositoryService = new RepositoryManagementService(settingsService);
        var autoFetchService = new AutoFetchService(gitService, credentialService);
        var folderWatcherService = new FolderWatcherService();
        var pullRequestService = new PullRequestService(credentialService, gitService);

        // ViewModelFactory for transient ViewModel creation
        var viewModelFactory = new ViewModelFactory(gitService, dialogService, repositoryEventHub, clipboardService, fileSystemService);

        // Create view model with all services
        var viewModel = new MainViewModel(
            gitService,
            credentialService,
            settingsService,
            gitFlowService,
            repositoryService,
            autoFetchService,
            this,
            dispatcherService,
            repositoryEventHub,
            dialogService,
            repositorySessionFactory,
            gitCommandRunner,
            clipboardService,
            fileSystemService,
            folderWatcherService,
            pullRequestService,
            notificationService);

        viewModel.CommandPaletteViewModel = new ViewModels.CommandPaletteViewModel(
            repositoryService,
            () => viewModel.SelectedRepository,
            repo => viewModel.SelectRepositoryCommand.Execute(repo),
            branch => viewModel.CheckoutBranchCommand.Execute(branch));

        _gitService = gitService;
        _repositoryService = repositoryService;
        _viewModel = viewModel;

        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        viewModel.RequestGitFlowActionMenu += ViewModel_RequestGitFlowActionMenu;

        NotificationHostControl.NotificationService = notificationService;

        ContentRendered += OnContentRendered;
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
