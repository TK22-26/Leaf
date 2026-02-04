using System;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Models;
using Leaf.Services;
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
    private readonly Window _ownerWindow;
    private readonly FileWatcherService _fileWatcherService;

    // Phase 0/1: Architecture Glue Services
    private readonly IDispatcherService _dispatcherService;
    private readonly IRepositoryEventHub _eventHub;
    private readonly IDialogService _dialogService;
    private readonly IRepositorySessionFactory _sessionFactory;
    private readonly IGitCommandRunner _gitCommandRunner;
    private readonly IClipboardService _clipboardService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IFolderWatcherService _folderWatcherService;
    private IRepositorySession? _currentSession;
    private bool _disposed;

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
    private bool _isTerminalVisible;

    [ObservableProperty]
    private bool _isBranchFilterActive;

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

    private bool _isRenameBranchInput;
    private string? _pendingRenameBranchName;

    public MainViewModel(
        IGitService gitService,
        CredentialService credentialService,
        SettingsService settingsService,
        IGitFlowService gitFlowService,
        IRepositoryManagementService repositoryService,
        IAutoFetchService autoFetchService,
        Window ownerWindow,
        IDispatcherService dispatcherService,
        IRepositoryEventHub eventHub,
        IDialogService dialogService,
        IRepositorySessionFactory sessionFactory,
        IGitCommandRunner gitCommandRunner,
        IClipboardService clipboardService,
        IFileSystemService fileSystemService,
        IFolderWatcherService folderWatcherService)
    {
        _gitService = gitService;
        _gitFlowService = gitFlowService;
        _credentialService = credentialService;
        _settingsService = settingsService;
        _repositoryService = repositoryService;
        _autoFetchService = autoFetchService;
        _ownerWindow = ownerWindow;
        _dispatcherService = dispatcherService;
        _eventHub = eventHub;
        _dialogService = dialogService;
        _sessionFactory = sessionFactory;
        _gitCommandRunner = gitCommandRunner;
        _clipboardService = clipboardService;
        _fileSystemService = fileSystemService;
        _folderWatcherService = folderWatcherService;
        _fileWatcherService = new FileWatcherService();

        // Subscribe to folder watcher for new repository discovery
        _folderWatcherService.RepositoryDiscovered += OnRepositoryDiscovered;

        // Start watching saved folders and scan for missed repos
        var watchedFolders = _settingsService.LoadSettings().WatchedFolders;
        if (watchedFolders.Count > 0)
        {
            _folderWatcherService.StartWatching(watchedFolders);
            _ = ScanWatchedFoldersAsync(watchedFolders);
        }

        // Subscribe to auto-fetch completion
        _autoFetchService.FetchCompleted += OnAutoFetchCompleted;

        _gitGraphViewModel = new GitGraphViewModel(gitService);
        _commitDetailViewModel = new CommitDetailViewModel(gitService, clipboardService, fileSystemService);

        // Create AI and gitignore services for WorkingChangesViewModel
        var commitMessageParser = new CommitMessageParser();
        var ollamaService = new OllamaService();
        var aiCommitService = new AiCommitMessageService(settingsService, ollamaService, commitMessageParser);
        var gitignoreService = new GitignoreService(gitService);

        _workingChangesViewModel = new WorkingChangesViewModel(gitService, clipboardService, fileSystemService, dialogService, aiCommitService, gitignoreService);
        _workingChangesViewModel.FileSelected += OnWorkingChangesFileSelected;
        _diffViewerViewModel = new DiffViewerViewModel(gitService);
        _diffViewerViewModel.CloseRequested += (s, e) => CloseDiffViewer();
        _diffViewerViewModel.HunkReverted += OnDiffViewerHunkReverted;
        _terminalViewModel = new TerminalViewModel(gitService, settingsService);
        _terminalViewModel.CommandExecuted += OnTerminalCommandExecuted;

        // Wire up file watcher events
        _fileWatcherService.WorkingDirectoryChanged += async (s, e) =>
        {
            await _dispatcherService.InvokeAsync(async () =>
            {
                // Refresh working changes in graph view
                if (_gitGraphViewModel != null)
                {
                    await _gitGraphViewModel.RefreshWorkingChangesAsync();

                    // Sync to staging view if visible
                    if (_workingChangesViewModel != null && SelectedRepository != null && IsWorkingChangesSelected)
                    {
                        _workingChangesViewModel.SetWorkingChanges(
                            SelectedRepository.Path,
                            _gitGraphViewModel.WorkingChanges);
                    }
                }
            });
        };

        _fileWatcherService.GitDirectoryChanged += async (s, e) =>
        {
            await _dispatcherService.InvokeAsync(async () =>
            {
                // Full refresh of git graph for commit changes
                if (_gitGraphViewModel != null && SelectedRepository != null)
                {
                    await _gitGraphViewModel.LoadRepositoryAsync(SelectedRepository.Path);
                }

                if (_workingChangesViewModel != null && SelectedRepository != null && IsWorkingChangesSelected)
                {
                    _workingChangesViewModel.SetWorkingChanges(
                        SelectedRepository.Path,
                        _gitGraphViewModel?.WorkingChanges);
                }

                if (SelectedRepository != null)
                {
                    var info = await _gitService.GetRepositoryInfoAsync(SelectedRepository.Path);
                    SelectedRepository.IsMergeInProgress = info.IsMergeInProgress;
                    SelectedRepository.MergingBranch = info.MergingBranch;
                    SelectedRepository.ConflictCount = info.ConflictCount;

                    await RefreshMergeConflictResolutionAsync();
                }
            });
        };

        // Wire up selection changes
        _gitGraphViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GitGraphViewModel.SelectedCommit))
            {
                LoadCommitDetails(_gitGraphViewModel.SelectedCommit);
            }
            else if (e.PropertyName == nameof(GitGraphViewModel.IsWorkingChangesSelected))
            {
                IsWorkingChangesSelected = _gitGraphViewModel.IsWorkingChangesSelected;
                if (IsWorkingChangesSelected && SelectedRepository != null)
                {
                    // Defer to avoid reentrancy during PropertyChanged
                    var repoPath = SelectedRepository.Path;
                    var workingChanges = _gitGraphViewModel.WorkingChanges;
                    _ = _dispatcherService.InvokeAsync(() =>
                    {
                        _workingChangesViewModel.SetWorkingChanges(repoPath, workingChanges);
                    });
                }
            }
            else if (e.PropertyName == nameof(GitGraphViewModel.WorkingChanges))
            {
                // Update working changes count in commit detail view
                if (_commitDetailViewModel != null && _gitGraphViewModel?.WorkingChanges != null)
                {
                    _commitDetailViewModel.UpdateWorkingChangesCount(_gitGraphViewModel.WorkingChanges.TotalChanges);
                }
            }
            else if (e.PropertyName == nameof(GitGraphViewModel.SelectedStash))
            {
                // Notify that Pop command availability changed
                PopStashCommand.NotifyCanExecuteChanged();

                // Load stash details when a stash is selected
                var selectedStash = _gitGraphViewModel.SelectedStash;
                if (selectedStash != null && SelectedRepository != null)
                {
                    _ = _commitDetailViewModel.LoadStashAsync(SelectedRepository.Path, selectedStash);
                }
            }
        };

        // Wire up commit detail events
        _commitDetailViewModel.NavigateToCommitRequested += (s, sha) =>
        {
            if (_gitGraphViewModel != null)
            {
                _gitGraphViewModel.SelectCommitBySha(sha);
            }
        };

        _commitDetailViewModel.SelectWorkingChangesRequested += (s, e) =>
        {
            if (_gitGraphViewModel != null)
            {
                _gitGraphViewModel.SelectWorkingChanges();
            }
        };

        // Load saved repositories on startup
        LoadSavedRepositories();

        // Start auto-fetch timer
        StartAutoFetchTimer();

        // Check for updates silently on startup
        _ = CheckForUpdatesSilentlyAsync();
    }

    partial void OnSelectedRepositoryChanged(RepositoryInfo? value)
    {
        TerminalViewModel?.SetWorkingDirectory(value?.Path);
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
    /// Disposes resources held by MainViewModel.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from events to prevent memory leaks
        _autoFetchService.FetchCompleted -= OnAutoFetchCompleted;

        // Dispose current repository session
        _currentSession?.Dispose();
        _currentSession = null;

        // Dispose file watcher
        _fileWatcherService.Dispose();

        // Dispose folder watcher
        _folderWatcherService.RepositoryDiscovered -= OnRepositoryDiscovered;
        _folderWatcherService.Dispose();
    }
}
