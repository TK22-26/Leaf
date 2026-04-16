using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.PullRequests;
using Leaf.Views;

namespace Leaf.ViewModels;

/// <summary>
/// Main application ViewModel - manages navigation and overall app state.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IGitService _gitService;
    private readonly IGitFlowService _gitFlowService;
    private readonly CredentialService _credentialService;
    private readonly SettingsService _settingsService;
    private readonly IRepositoryManagementService _repositoryService;
    private readonly IAutoFetchService _autoFetchService;
    private readonly FileWatcherService _fileWatcherService;

    // Phase 0/1: Architecture Glue Services
    private readonly IDispatcherService _dispatcherService;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboardService;
    private readonly IFolderWatcherService _folderWatcherService;
    private readonly IPullRequestService _pullRequestService;
    private readonly IRepositorySessionFactory _sessionFactory;
    private readonly IDiffService _diffService = new DiffService();
    private readonly INotificationService? _notificationService;
    private IRepositorySession? _currentSession;
    private bool _disposed;

    /// <summary>
    /// Cancellation token scoped to the current repository.
    /// Cancels when the user switches repositories — lets background git
    /// operations abort promptly instead of fighting for resources with the
    /// newly selected repo's loading. Pass this to every IGitService call.
    /// </summary>
    public CancellationToken CurrentRepositoryToken =>
        _currentSession?.CancellationToken ?? CancellationToken.None;

    private string? _pendingBranchBaseSha;

    /// <summary>
    /// Auto-fetch timer interval (10 minutes).
    /// </summary>
    private static readonly TimeSpan AutoFetchInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Event raised when a repository should be visually selected in the TreeView.
    /// </summary>
    public event EventHandler<RepositoryInfo>? RequestRepositorySelection;

    /// <summary>
    /// Event raised to open the branch create/rename popup.
    /// Uses an event instead of PropertyChanged on IsBranchInputVisible to avoid stuck state
    /// when SetProperty suppresses duplicate true→true transitions.
    /// </summary>
    public event EventHandler? RequestBranchCreatePopup;

    /// <summary>
    /// Last fetch time - delegated to AutoFetchService.
    /// </summary>
    public DateTime? LastFetchTime => _autoFetchService.LastFetchTime;

    /// <summary>
    /// Repository groups - delegated to RepositoryManagementService.
    /// </summary>
    public ObservableCollection<RepositoryGroup> RepositoryGroups => _repositoryService.RepositoryGroups;

    [ObservableProperty]
    private RepositoryInfo? _selectedRepository;

    [ObservableProperty]
    private GitGraphViewModel? _gitGraphViewModel;

    [ObservableProperty]
    private CommitDetailViewModel? _commitDetailViewModel;

    [ObservableProperty]
    private WorkingChangesViewModel? _workingChangesViewModel;

    [ObservableProperty]
    private DiffViewerViewModel? _diffViewerViewModel;

    [ObservableProperty]
    private TerminalViewModel? _terminalViewModel;

    [ObservableProperty]
    private ConflictResolutionViewModel? _mergeConflictResolutionViewModel;

    [ObservableProperty]
    private bool _isCommitDetailVisible = true;

    [ObservableProperty]
    private bool _isWorkingChangesSelected;

    [ObservableProperty]
    private bool _isDiffViewerVisible;

    [ObservableProperty]
    private bool _isRepoPaneCollapsed;

    [ObservableProperty]
    private double _repoPaneWidth = 220;

    [ObservableProperty]
    private bool _isTerminalVisible;

    [ObservableProperty]
    private bool _isBranchFilterActive;

    [ObservableProperty]
    private bool _isGitFlowInitialized;

    [ObservableProperty]
    private string _branchInputActionText = "Create";

    [ObservableProperty]
    private string _branchInputPlaceholder = "Branch name...";

    [ObservableProperty]
    private double _terminalHeight = 220;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Sets IsBusy and yields to the UI thread so the progress bar can render.
    /// </summary>
    private async Task BeginBusyAsync(string statusMessage)
    {
        IsBusy = true;
        StatusMessage = statusMessage;
        // Force WPF to complete a render pass before continuing, so the progress bar appears
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    [ObservableProperty]
    private string _commitSearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredRepositoryRootItems))]
    private string _repositorySearchText = string.Empty;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private UpdateInfo? _availableUpdate;

    /// <summary>
    /// Pinned repositories - delegated to RepositoryManagementService.
    /// </summary>
    public ObservableCollection<RepositoryInfo> PinnedRepositories => _repositoryService.PinnedRepositories;

    /// <summary>
    /// Recent repositories - delegated to RepositoryManagementService.
    /// </summary>
    public ObservableCollection<RepositoryInfo> RecentRepositories => _repositoryService.RecentRepositories;

    /// <summary>
    /// Repository root items for tree view - delegated to RepositoryManagementService.
    /// </summary>
    public ObservableCollection<object> RepositoryRootItems => _repositoryService.RepositoryRootItems;

    /// <summary>
    /// Filtered repository root items based on search text.
    /// </summary>
    public IEnumerable<object> FilteredRepositoryRootItems
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RepositorySearchText))
                return RepositoryRootItems;

            var searchText = RepositorySearchText.Trim();
            var result = new List<object>();

            foreach (var item in RepositoryRootItems)
            {
                if (item is Models.RepositorySection section)
                {
                    var filteredItems = section.Items
                        .Where(qi => qi.Repository?.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                    if (filteredItems.Count > 0)
                    {
                        var filteredSection = new Models.RepositorySection
                        {
                            Name = section.Name,
                            IsExpanded = true
                        };
                        foreach (var fi in filteredItems)
                            filteredSection.Items.Add(fi);
                        result.Add(filteredSection);
                    }
                }
                else if (item is Models.RepositoryGroup group)
                {
                    var filteredRepos = group.Repositories
                        .Where(r => r.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                    if (filteredRepos.Count > 0 || group.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var filteredGroup = new Models.RepositoryGroup
                        {
                            Name = group.Name,
                            IsExpanded = true,
                            IsWatched = group.IsWatched
                        };
                        var reposToAdd = filteredRepos.Count > 0 ? filteredRepos : group.Repositories.ToList();
                        foreach (var r in reposToAdd)
                            filteredGroup.Repositories.Add(r);
                        result.Add(filteredGroup);
                    }
                }
            }

            return result;
        }
    }

    private string? _mergeConflictRepoPath;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private bool _isBranchInputVisible;

    [ObservableProperty]
    private string _newBranchName = string.Empty;

    [ObservableProperty]
    private CommandPaletteViewModel? _commandPaletteViewModel;

    private bool _isRenameBranchInput;
    private string? _pendingRenameBranchName;
    private int _isGitDirectoryChangeRunning;
    private bool _isSwitchingRepository;
    private int _startupInitializationStarted;

    public MainViewModel(
        IGitService gitService,
        CredentialService credentialService,
        SettingsService settingsService,
        IGitFlowService gitFlowService,
        IRepositoryManagementService repositoryService,
        IAutoFetchService autoFetchService,
        IDispatcherService dispatcherService,
        IRepositoryEventHub eventHub,
        IDialogService dialogService,
        IRepositorySessionFactory sessionFactory,
        IGitCommandRunner gitCommandRunner,
        IClipboardService clipboardService,
        IFileSystemService fileSystemService,
        IFolderWatcherService folderWatcherService,
        IPullRequestService pullRequestService,
        INotificationService? notificationService = null)
    {
        _gitService = gitService;
        _gitFlowService = gitFlowService;
        _credentialService = credentialService;
        _settingsService = settingsService;
        _repositoryService = repositoryService;
        _autoFetchService = autoFetchService;
        _dispatcherService = dispatcherService;
        _dialogService = dialogService;
        _sessionFactory = sessionFactory;
        _clipboardService = clipboardService;
        _folderWatcherService = folderWatcherService;
        _pullRequestService = pullRequestService;
        _notificationService = notificationService;
        _fileWatcherService = new FileWatcherService();

        // Subscribe to folder watcher for new repository discovery
        _folderWatcherService.RepositoryDiscovered += OnRepositoryDiscovered;

        // Start watching saved folders and scan for missed repos
        var watchedFolders = _settingsService.LoadSettings().WatchedFolders;
        if (watchedFolders.Count > 0)
        {
            _folderWatcherService.StartWatching(watchedFolders);
            // Background scan — faults log-only unless the user opts into
            // background notifications. Replaces the prior bespoke ContinueWith.
            ScanWatchedFoldersAsync(watchedFolders).FireAndForget(nameof(ScanWatchedFoldersAsync), isUserAction: false);
        }

        // Subscribe to auto-fetch completion
        _autoFetchService.FetchCompleted += OnAutoFetchCompleted;

        // Provide each child VM with a getter for the active session token so
        // its background git calls abort when the user switches repositories.
        Func<CancellationToken> tokenGetter = () => CurrentRepositoryToken;

        _gitGraphViewModel = new GitGraphViewModel(gitService) { GetSessionToken = tokenGetter };
        _commitDetailViewModel = new CommitDetailViewModel(gitService, clipboardService, fileSystemService, settingsService)
            { GetSessionToken = tokenGetter };

        // Create AI and gitignore services for WorkingChangesViewModel
        var commitMessageParser = new CommitMessageParser();
        var ollamaService = new OllamaService();
        var aiCommitService = new AiCommitMessageService(settingsService, ollamaService, commitMessageParser);
        var gitignoreService = new GitignoreService(gitService);

        _workingChangesViewModel = new WorkingChangesViewModel(gitService, clipboardService, fileSystemService, dialogService, aiCommitService, gitignoreService, settingsService)
            { GetSessionToken = tokenGetter };
        _workingChangesViewModel.FileSelected += OnWorkingChangesFileSelected;
        _workingChangesViewModel.FileDeletedOrDiscarded += OnFileDeletedOrDiscarded;
        _diffViewerViewModel = new DiffViewerViewModel(gitService) { GetSessionToken = tokenGetter };
        _diffViewerViewModel.CloseRequested += OnDiffViewerCloseRequested;
        _diffViewerViewModel.HunkReverted += OnDiffViewerHunkReverted;
        _terminalViewModel = new TerminalViewModel(gitService, settingsService) { GetSessionToken = tokenGetter };
        _terminalViewModel.CommandExecuted += OnTerminalCommandExecuted;

        // Wire up file watcher events
        _fileWatcherService.WorkingDirectoryChanged += OnFileWatcherWorkingDirectoryChanged;
        _fileWatcherService.GitDirectoryChanged += OnFileWatcherGitDirectoryChanged;

        // Wire up selection changes
        _gitGraphViewModel.PropertyChanged += OnGitGraphViewModelPropertyChanged;

        // Wire up commit detail events
        _commitDetailViewModel.NavigateToCommitRequested += OnNavigateToCommitRequested;
        _commitDetailViewModel.SelectWorkingChangesRequested += OnSelectWorkingChangesRequested;

        // Start auto-fetch timer
        StartAutoFetchTimer();

        // Check for updates silently on startup
        CheckForUpdatesSilentlyAsync().FireAndForget(nameof(CheckForUpdatesSilentlyAsync), isUserAction: false);

        Log.Info("App", "MainViewModel initialized");
    }

    #region Event handlers (named so Dispose can unsubscribe — see plan §1.6)

    private void OnDiffViewerCloseRequested(object? sender, EventArgs e)
    {
        CloseDiffViewer();
    }

    private void OnFileWatcherWorkingDirectoryChanged(object? sender, EventArgs e)
    {
        HandleWorkingDirectoryChangedAsync()
            .FireAndForget(nameof(HandleWorkingDirectoryChangedAsync), isUserAction: false);
    }

    private void OnFileWatcherGitDirectoryChanged(object? sender, EventArgs e)
    {
        HandleGitDirectoryChangedAsync()
            .FireAndForget(nameof(HandleGitDirectoryChangedAsync), isUserAction: false);
    }

    private void OnGitGraphViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var graph = GitGraphViewModel;
        if (graph == null) return;

        if (e.PropertyName == nameof(GitGraphViewModel.SelectedCommit))
        {
            // Skip LoadCommitDetails for stash pseudo-commits — the
            // SelectedStash branch below loads stash details.
            if (graph.SelectedCommit?.IsStash != true)
            {
                LoadCommitDetails(graph.SelectedCommit);
            }
        }
        else if (e.PropertyName == nameof(GitGraphViewModel.IsWorkingChangesSelected))
        {
            IsWorkingChangesSelected = graph.IsWorkingChangesSelected;
            if (IsWorkingChangesSelected && SelectedRepository != null && WorkingChangesViewModel != null)
            {
                // Defer to avoid reentrancy during PropertyChanged.
                var repoPath = SelectedRepository.Path;
                var workingChanges = graph.WorkingChanges;
                var wcVm = WorkingChangesViewModel;
                _dispatcherService.InvokeAsync(() =>
                {
                    wcVm.SetWorkingChanges(repoPath, workingChanges);
                }).FireAndForget("SelectWorkingChanges.DispatcherInvoke", isUserAction: true);
            }
        }
        else if (e.PropertyName == nameof(GitGraphViewModel.WorkingChanges))
        {
            // Update working changes count in commit detail view.
            if (CommitDetailViewModel != null && graph.WorkingChanges != null)
            {
                CommitDetailViewModel.UpdateWorkingChangesCount(graph.WorkingChanges.TotalChanges);
            }
        }
        else if (e.PropertyName == nameof(GitGraphViewModel.SelectedStash))
        {
            // Notify that Pop command availability changed.
            PopStashCommand.NotifyCanExecuteChanged();

            // Load stash details when a stash is selected.
            var selectedStash = graph.SelectedStash;
            if (selectedStash != null && SelectedRepository != null && CommitDetailViewModel != null)
            {
                CommitDetailViewModel.LoadStashAsync(SelectedRepository.Path, selectedStash)
                    .FireAndForget(nameof(CommitDetailViewModel.LoadStashAsync), isUserAction: true);
            }
        }
    }

    private void OnNavigateToCommitRequested(object? sender, string sha)
    {
        GitGraphViewModel?.SelectCommitBySha(sha);
    }

    private void OnSelectWorkingChangesRequested(object? sender, EventArgs e)
    {
        GitGraphViewModel?.SelectWorkingChanges();
    }

    #endregion

    partial void OnSelectedRepositoryChanged(RepositoryInfo? value)
    {
        TerminalViewModel?.SetWorkingDirectory(value?.Path);

        if (value == null)
        {
            // Clear graph, commit detail, and working changes when no repo is selected
            if (GitGraphViewModel != null)
            {
                GitGraphViewModel.RepositoryPath = null;
                GitGraphViewModel.Commits.Clear();
                GitGraphViewModel.Nodes.Clear();
                GitGraphViewModel.SelectedCommit = null;
                GitGraphViewModel.WorkingChanges = null;
                GitGraphViewModel.Stashes.Clear();
                GitGraphViewModel.SelectedStash = null;
                GitGraphViewModel.TotalHeight = 0;
                GitGraphViewModel.MaxLane = 0;
                GitGraphViewModel.ErrorMessage = null;
            }

            CommitDetailViewModel?.ClearSelection();
            WorkingChangesViewModel?.ClearWorkingChanges();
            IsWorkingChangesSelected = false;
            IsDiffViewerVisible = false;
            ResetPullRequestViewState();
            StatusMessage = "Select a repository";
        }
    }

    partial void OnIsTerminalVisibleChanged(bool value)
    {
        var settings = _settingsService.LoadSettings();
        settings.IsTerminalVisible = value;
        _settingsService.SaveSettings(settings);
    }

    partial void OnCommitSearchTextChanged(string value)
    {
        // Apply filter to GitGraphViewModel as user types
        if (GitGraphViewModel != null)
        {
            GitGraphViewModel.SearchText = value;
        }
    }

    /// <summary>
    /// Disposes resources held by MainViewModel — unsubscribes from every
    /// event this VM subscribed to in its constructor (see plan §1.6) and
    /// disposes any IDisposable services and child ViewModels we own.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from every event wired up in the constructor. Order
        // mirrors the constructor wiring so any audit diff is easy to read.
        _folderWatcherService.RepositoryDiscovered -= OnRepositoryDiscovered;
        _autoFetchService.FetchCompleted -= OnAutoFetchCompleted;

        var workingChanges = WorkingChangesViewModel;
        if (workingChanges != null)
        {
            workingChanges.FileSelected -= OnWorkingChangesFileSelected;
            workingChanges.FileDeletedOrDiscarded -= OnFileDeletedOrDiscarded;
        }

        var diffViewer = DiffViewerViewModel;
        if (diffViewer != null)
        {
            diffViewer.CloseRequested -= OnDiffViewerCloseRequested;
            diffViewer.HunkReverted -= OnDiffViewerHunkReverted;
        }

        var terminal = TerminalViewModel;
        if (terminal != null)
        {
            terminal.CommandExecuted -= OnTerminalCommandExecuted;
        }

        _fileWatcherService.WorkingDirectoryChanged -= OnFileWatcherWorkingDirectoryChanged;
        _fileWatcherService.GitDirectoryChanged -= OnFileWatcherGitDirectoryChanged;

        var graph = GitGraphViewModel;
        if (graph != null)
        {
            graph.PropertyChanged -= OnGitGraphViewModelPropertyChanged;
        }

        var commitDetail = CommitDetailViewModel;
        if (commitDetail != null)
        {
            commitDetail.NavigateToCommitRequested -= OnNavigateToCommitRequested;
            commitDetail.SelectWorkingChangesRequested -= OnSelectWorkingChangesRequested;
        }

        // Transient VMs wired up outside the constructor still need to be
        // detached — they hold refs to long-lived services.
        DetachCreatePullRequestViewModel(CreatePullRequestViewModel);
        DetachPullRequestDetailViewModel(PullRequestDetailViewModel);

        // The last MergeConflictResolutionViewModel (if any) must detach its
        // MergeCompleted subscription and run its own Cleanup() so the
        // DispatcherTimer + CTS fields inside don't root the VM.
        var mergeConflictVm = MergeConflictResolutionViewModel;
        if (mergeConflictVm != null)
        {
            mergeConflictVm.MergeCompleted -= OnMergeConflictResolutionCompleted;
            mergeConflictVm.Cleanup();
        }

        // Stop the auto-fetch timer so no pending callbacks race with disposal.
        _autoFetchService.Stop();

        // Dispose IDisposable child ViewModels — these own CancellationTokenSource
        // fields that would otherwise leak at process shutdown (picked up from
        // plan §1.5 now that Dispose plumbing exists).
        (diffViewer as IDisposable)?.Dispose();
        (terminal as IDisposable)?.Dispose();
        (workingChanges as IDisposable)?.Dispose();
        (commitDetail as IDisposable)?.Dispose();
        (graph as IDisposable)?.Dispose();

        // Dispose current repository session
        _currentSession?.Dispose();
        _currentSession = null;

        // Dispose file watcher
        _fileWatcherService.Dispose();

        // Dispose folder watcher
        _folderWatcherService.Dispose();
    }
}
