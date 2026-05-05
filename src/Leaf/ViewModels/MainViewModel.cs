using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Composition;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.PullRequests;
using Leaf.Views;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDiffService _diffService;
    private readonly IExternalToolConfigService _externalToolConfig;
    private readonly IExternalToolDetectorService _externalToolDetector;
    private readonly IExternalToolLauncherService _externalToolLauncher;
    private readonly INotificationService? _notificationService;
    private readonly Services.Merge.IMergeEngine _mergeEngine;
    private readonly Services.Merge.IAiMergeAssistant? _aiMergeAssistant;
    private readonly Services.Merge.IWordDiffService _wordDiffService;
    private readonly Services.Merge.IImageMergeService? _imageMergeService;
    private readonly Services.Merge.IMergeBlameService _mergeBlameService;
    private readonly IInteractiveRebaseService _interactiveRebaseService;
    private readonly IPatchService _patchService;
    private readonly IBisectService _bisectService;
    private readonly IBranchColorPaletteRegistry _branchColorPaletteRegistry;
    private readonly ICommitTemplateService _commitTemplateService;

    // The per-repo DI scope. Owns the current IRepositorySession (scoped)
    // and — in future phases — the per-repo ViewModels. Disposed on repo
    // switch and on shutdown; disposal cascades to the session which
    // cancels its token and tears down the LibGit2Sharp Repository handle.
    // _currentSession caches the scope's session so CurrentRepositoryToken
    // is a field read, not a container lookup on every call.
    private IServiceScope? _currentScope;
    private IRepositorySession? _currentSession;
    private string? _currentScopeRepoPath;

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

    /// <summary>
    /// True when the current repository has an external merge tool
    /// configured (via Leaf's Settings or `git config`). Tracked for
    /// status / UI feedback; the OpenConflictInMergeTool command is
    /// always executable now — when no tool is configured it deep-links
    /// the user into Settings → External Tools instead of being disabled.
    /// </summary>
    [ObservableProperty]
    private bool _hasExternalMergeTool;

    [ObservableProperty]
    private GitGraphViewModel? _gitGraphViewModel;

    [ObservableProperty]
    private CommitDetailViewModel? _commitDetailViewModel;

    /// <summary>
    /// §5.17 — view-model behind the right-pane TagDetailView. Created
    /// lazily on first SelectTag, lives for the app's lifetime alongside
    /// the other detail VMs. Tag is set/cleared in the selection path.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTagDetailMode))]
    private TagDetailViewModel? _tagDetailViewModel;

    /// <summary>True when the right pane should show TagDetailView (a tag is currently selected).</summary>
    public bool IsTagDetailMode => TagDetailViewModel?.Tag is not null;

    [ObservableProperty]
    private WorkingChangesViewModel? _workingChangesViewModel;

    [ObservableProperty]
    private DiffViewerViewModel? _diffViewerViewModel;

    [ObservableProperty]
    private TerminalViewModel? _terminalViewModel;

    [ObservableProperty]
    private ViewModels.Merge.MergeEditorViewModel? _mergeConflictResolutionViewModel;

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
    private bool _isBusy;

    /// <summary>
    /// Sets <see cref="IsBusy"/> and yields to the WPF dispatcher so the
    /// progress indicator gets a render pass before the long-running work
    /// kicks off. The string parameter is purely advisory — it is logged
    /// for diagnostics but no longer surfaced to a status bar (that UI
    /// element was removed because it was never bound). Operations that
    /// need user-visible feedback should fire a toast via
    /// <see cref="NotifySuccess"/> / <see cref="NotifyInfo"/> on
    /// completion, not before.
    /// </summary>
    private async Task BeginBusyAsync(string operationDescription)
    {
        IsBusy = true;
        Log.Info("Op", operationDescription);
        // Force WPF to complete a render pass before continuing, so the progress bar appears
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Canonical failure feedback for a user-initiated operation: fire an
    /// error toast. Use this in every <c>catch</c> block that handles a
    /// recoverable git/IO failure inside a RelayCommand.
    /// </summary>
    /// <param name="operation">Operation label, e.g. "Push", "Delete branch".</param>
    /// <param name="detail">Failure message — usually <c>ex.Message</c>.</param>
    private Task ReportOperationFailureAsync(string operation, string detail)
    {
        return _dialogService.ShowErrorToastAsync(
            $"{operation} failed:\n\n{detail}",
            $"{operation} failed");
    }

    /// <summary>
    /// Fire a success toast. Helper around <see cref="INotificationService.Show"/>
    /// so call sites stay readable. Safe when <see cref="_notificationService"/>
    /// is null (test context, headless runs).
    /// </summary>
    private void NotifySuccess(string title, string description) =>
        _notificationService?.Show(title, description, NotificationType.Success);

    /// <summary>Fire an informational toast.</summary>
    private void NotifyInfo(string title, string description) =>
        _notificationService?.Show(title, description, NotificationType.Information);

    /// <summary>Fire a warning toast.</summary>
    private void NotifyWarning(string title, string description) =>
        _notificationService?.Show(title, description, NotificationType.Warning);

    /// <summary>
    /// Convenience overload that pulls the detail string from an exception.
    /// See <see cref="ReportOperationFailureAsync(string, string)"/>.
    /// </summary>
    private Task ReportOperationFailureAsync(string operation, Exception ex)
        => ReportOperationFailureAsync(operation, ex.Message);

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

    // Cache for FilteredRepositoryRootItems — WPF bindings often read the
    // getter multiple times per layout pass, so without a cache each read
    // rebuilt the filtered tree. Keyed by the trimmed search text; the
    // empty-search fast path returns the underlying ObservableCollection
    // directly so WPF gets incremental change notifications on it.
    private IEnumerable<object>? _filteredRepoRootItemsCache;
    private string? _filteredRepoRootItemsCacheKey;

    /// <summary>
    /// Filtered repository root items based on search text.
    /// </summary>
    public IEnumerable<object> FilteredRepositoryRootItems
    {
        get
        {
            var key = (RepositorySearchText ?? string.Empty).Trim();
            if (key.Length == 0)
                return RepositoryRootItems;

            if (_filteredRepoRootItemsCache != null && _filteredRepoRootItemsCacheKey == key)
                return _filteredRepoRootItemsCache;

            _filteredRepoRootItemsCacheKey = key;
            _filteredRepoRootItemsCache = BuildFilteredRepositoryRootItems(key);
            return _filteredRepoRootItemsCache;
        }
    }

    // Adapters for the three repo-tree mutation signals we observe.
    // Each has a different delegate signature (NotifyCollectionChangedEventHandler
    // vs. EventHandler<RepositoryInfo>), so they can't share a method —
    // but they all funnel into InvalidateFilteredRepoItemsCache.
    private void OnRepoRootItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => InvalidateFilteredRepoItemsCache();

    private void OnRepoAddedOrRemoved(object? sender, RepositoryInfo repo)
        => InvalidateFilteredRepoItemsCache();

    private void InvalidateFilteredRepoItemsCache()
    {
        // The empty-search fast path returns the live ObservableCollection
        // directly and doesn't need re-notification, but we raise anyway
        // for consistency — WPF no-ops when the value reference is equal.
        _filteredRepoRootItemsCache = null;
        _filteredRepoRootItemsCacheKey = null;
        OnPropertyChanged(nameof(FilteredRepositoryRootItems));
    }

    private List<object> BuildFilteredRepositoryRootItems(string searchText)
    {
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
        IServiceScopeFactory scopeFactory,
        IGitCommandRunner gitCommandRunner,
        IClipboardService clipboardService,
        IFileSystemService fileSystemService,
        IFolderWatcherService folderWatcherService,
        IPullRequestService pullRequestService,
        IDiffService diffService,
        IExternalToolConfigService externalToolConfig,
        IExternalToolDetectorService externalToolDetector,
        IExternalToolLauncherService externalToolLauncher,
        Services.Merge.IMergeEngine mergeEngine,
        Services.Merge.IWordDiffService wordDiffService,
        Services.Merge.IMergeBlameService mergeBlameService,
        IInteractiveRebaseService interactiveRebaseService,
        IPatchService patchService,
        IBisectService bisectService,
        IBranchColorPaletteRegistry branchColorPaletteRegistry,
        ICommitTemplateService commitTemplateService,
        INotificationService? notificationService = null,
        Services.Merge.IAiMergeAssistant? aiMergeAssistant = null,
        Services.Merge.IImageMergeService? imageMergeService = null)
    {
        _gitService = gitService;
        _mergeEngine = mergeEngine;
        _wordDiffService = wordDiffService;
        _aiMergeAssistant = aiMergeAssistant;
        _imageMergeService = imageMergeService;
        _mergeBlameService = mergeBlameService;
        _interactiveRebaseService = interactiveRebaseService ?? throw new ArgumentNullException(nameof(interactiveRebaseService));
        _patchService = patchService ?? throw new ArgumentNullException(nameof(patchService));
        _bisectService = bisectService ?? throw new ArgumentNullException(nameof(bisectService));
        _branchColorPaletteRegistry = branchColorPaletteRegistry ?? throw new ArgumentNullException(nameof(branchColorPaletteRegistry));
        _commitTemplateService = commitTemplateService ?? throw new ArgumentNullException(nameof(commitTemplateService));
        _gitFlowService = gitFlowService;
        _credentialService = credentialService;
        _settingsService = settingsService;
        _repositoryService = repositoryService;
        _autoFetchService = autoFetchService;
        _dispatcherService = dispatcherService;
        _dialogService = dialogService;
        _scopeFactory = scopeFactory;
        _diffService = diffService;
        _clipboardService = clipboardService;
        _folderWatcherService = folderWatcherService;
        _pullRequestService = pullRequestService;
        _externalToolConfig = externalToolConfig;
        _externalToolDetector = externalToolDetector;
        _externalToolLauncher = externalToolLauncher;
        _notificationService = notificationService;
        _fileWatcherService = new FileWatcherService();

        // Subscribe to folder watcher for new repository discovery
        _folderWatcherService.RepositoryDiscovered += OnRepositoryDiscovered;

        // Invalidate the filtered-repo-items cache (and re-notify WPF)
        // whenever the underlying repo tree changes — so an active search
        // filter reflects repos that get added/removed during the search.
        // Both top-level structural changes (section appearance/removal)
        // and nested add/remove of repos inside a group need to invalidate;
        // the service exposes dedicated add/remove events for the latter.
        _repositoryService.RepositoryRootItems.CollectionChanged += OnRepoRootItemsCollectionChanged;
        _repositoryService.RepositoryAdded += OnRepoAddedOrRemoved;
        _repositoryService.RepositoryRemoved += OnRepoAddedOrRemoved;

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

        _gitGraphViewModel = new GitGraphViewModel(gitService, settingsService, repositoryService, branchColorPaletteRegistry)
        {
            GetSessionToken = tokenGetter,
            // §5.14: feed the active RepositoryInfo through so per-repo
            // branch-colour overrides resolve against the right repo on
            // every load. Resolved at invocation time so SelectedRepository
            // changes during the session are picked up.
            GetActiveRepositoryInfo = () => SelectedRepository,
        };
        _commitDetailViewModel = new CommitDetailViewModel(gitService, clipboardService, fileSystemService, externalToolConfig, externalToolLauncher, settingsService)
            { GetSessionToken = tokenGetter };

        // Create AI and gitignore services for WorkingChangesViewModel.
        // This non-DI ctor path (used by the parameterless MainViewModel
        // entry, mostly tests / design-time) constructs the runner +
        // adapters by hand to keep the call site self-contained.
        var commitMessageParser = new CommitMessageParser();
        var ollamaService = new OllamaService();
        var aiCliRunner = new Leaf.Services.Ai.AiCliRunner();
        var aiCliAdapters = new Leaf.Services.Ai.Adapters.IAiCliAdapter[]
        {
            new Leaf.Services.Ai.Adapters.ClaudeCliAdapter(),
            new Leaf.Services.Ai.Adapters.GeminiCliAdapter(),
            new Leaf.Services.Ai.Adapters.CodexCliAdapter(),
        };
        var aiCommitService = new AiCommitMessageService(settingsService, ollamaService, commitMessageParser, aiCliRunner, aiCliAdapters);
        var gitignoreService = new GitignoreService(gitService);

        _workingChangesViewModel = new WorkingChangesViewModel(gitService, clipboardService, fileSystemService, dialogService, aiCommitService, gitignoreService, externalToolConfig, externalToolLauncher, settingsService, commitTemplateService)
            { GetSessionToken = tokenGetter };
        _workingChangesViewModel.FileSelected += OnWorkingChangesFileSelected;
        _workingChangesViewModel.FileDeletedOrDiscarded += OnFileDeletedOrDiscarded;
        _diffViewerViewModel = new DiffViewerViewModel(gitService) { GetSessionToken = tokenGetter };
        _diffViewerViewModel.CloseRequested += OnDiffViewerCloseRequested;
        _diffViewerViewModel.HunkReverted += OnDiffViewerHunkReverted;

        // Dedicated VM for the bisect detail's embedded diff. Separate
        // instance from the global _diffViewerViewModel so the bisect
        // pane's diff doesn't fight the IsDiffViewerVisible takeover
        // mode the rest of the app uses for full-screen diffs.
        // IsCloseable=false hides the X in the diff viewer's header —
        // the bisect detail view embeds the diff inline, so there's
        // nothing for "close" to mean here.
        BisectDiffViewerViewModel = new DiffViewerViewModel(gitService)
        {
            GetSessionToken = tokenGetter,
            IsCloseable = false,
        };
        _terminalViewModel = new TerminalViewModel(gitService, settingsService) { GetSessionToken = tokenGetter };
        _terminalViewModel.CommandExecuted += OnTerminalCommandExecuted;

        // CommandPaletteViewModel closes over this MainViewModel's state
        // and commands, so its construction lives here rather than in the
        // window's code-behind. The delegates deliberately resolve at
        // invocation time — `SelectedRepository` changes as the user
        // navigates, and the palette must see the current value.
        CommandPaletteViewModel = new CommandPaletteViewModel(
            _repositoryService,
            () => SelectedRepository,
            repo => SelectRepositoryCommand.Execute(repo),
            branch => CheckoutBranchCommand.Execute(branch));

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
            // §5.17 — picking a commit in the graph cancels any open
            // tag-detail mode so the right pane swaps back to the
            // commit detail view. Done before LoadCommitDetails so the
            // CommitDetailViewModel becomes visible without flashing
            // alongside TagDetailView.
            if (graph.SelectedCommit is not null)
                ClearTagDetailIfOpen();

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

    partial void OnSelectedRepositoryChanging(RepositoryInfo? oldValue, RepositoryInfo? newValue)
    {
        // C5 blame cache is keyed on (repoPath, filePath) + HEAD sha. HEAD
        // keying makes in-repo history changes self-invalidating, but a
        // repo switch leaves the old entries (and their per-file
        // SemaphoreSlim gates) dangling — a file with the same relative
        // path in the new repo could briefly surface the previous repo's
        // blame. Evict explicitly on transition so the singleton service
        // doesn't accumulate per-repo state across a long session.
        if (oldValue is not null
            && !string.Equals(oldValue.Path, newValue?.Path, StringComparison.OrdinalIgnoreCase))
        {
            _mergeBlameService.InvalidateRepo(oldValue.Path);
        }
    }

    partial void OnSelectedRepositoryChanged(RepositoryInfo? value)
    {
        TerminalViewModel?.SetWorkingDirectory(value?.Path);

        // §5.17 — drop the tag detail pane on every repo switch (and
        // when the user clears the selection). Without this, switching
        // to a new repo leaves TagDetailViewModel pointing at a TagInfo
        // owned by the previous repo, IsTagDetailMode stays true, and
        // the right pane keeps showing stale tag data instead of the
        // new repo's commit detail.
        ClearTagDetailIfOpen();

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
        _repositoryService.RepositoryRootItems.CollectionChanged -= OnRepoRootItemsCollectionChanged;
        _repositoryService.RepositoryAdded -= OnRepoAddedOrRemoved;
        _repositoryService.RepositoryRemoved -= OnRepoAddedOrRemoved;
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

        // Dispose current repository scope — cascades to IRepositorySession,
        // which cancels its token and releases the LibGit2Sharp handle.
        _currentScope?.Dispose();
        _currentScope = null;
        _currentSession = null;
        _currentScopeRepoPath = null;

        // Dispose file watcher
        _fileWatcherService.Dispose();

        // Dispose folder watcher
        _folderWatcherService.Dispose();
    }
}
