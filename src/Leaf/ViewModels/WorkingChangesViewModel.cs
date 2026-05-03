using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Utils;

namespace Leaf.ViewModels;

/// <summary>
/// Event arguments for file selection events.
/// </summary>
public class FileSelectedEventArgs : EventArgs
{
    public FileStatusInfo File { get; }
    public bool IsStaged { get; }

    public FileSelectedEventArgs(FileStatusInfo file, bool isStaged)
    {
        File = file;
        IsStaged = isStaged;
    }
}

/// <summary>
/// Event arguments for file deleted or discarded events.
/// </summary>
public class FileDeletedOrDiscardedEventArgs : EventArgs
{
    public string? FilePath { get; }
    public bool AffectsAllFiles { get; }

    public FileDeletedOrDiscardedEventArgs(string filePath) => FilePath = filePath;
    public FileDeletedOrDiscardedEventArgs(bool affectsAllFiles) => AffectsAllFiles = affectsAllFiles;
}

/// <summary>
/// ViewModel for the working changes staging area view.
/// Handles staging, unstaging, discarding, and committing files.
/// </summary>
public partial class WorkingChangesViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IClipboardService _clipboardService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private readonly IAiCommitMessageService _aiCommitService;
    private readonly IGitignoreService _gitignoreService;
    private readonly SettingsService _settingsService;
    private readonly IExternalToolConfigService _externalToolConfig;
    private readonly IExternalToolLauncherService _externalToolLauncher;
    private readonly ICommitTemplateService _commitTemplateService;
    private string? _repositoryPath;
    private CancellationTokenSource? _aiCancellationTokenSource;

    [ObservableProperty]
    private bool _showUnstagedTreeView;

    [ObservableProperty]
    private bool _showStagedTreeView;

    [ObservableProperty]
    private bool _isUnstagedExpanded = true;

    [ObservableProperty]
    private bool _isStagedExpanded = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenInExternalDiffToolCommand))]
    private bool _hasExternalDiffTool;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(FileChangesSummary))]
    private WorkingChangesInfo? _workingChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingChars))]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private string _commitDescription = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isAiAvailable;

    /// <summary>
    /// §5.15 master toggle. When false (the default — opt-in), the
    /// commit panel hides the templates icon button and the Ctrl+T
    /// shortcut becomes a no-op. Stored templates are preserved either
    /// way. Read from <see cref="AppSettings.CommitTemplatesEnabled"/>
    /// in <see cref="RefreshCommitTemplatesEnabled"/> after settings
    /// close.
    /// </summary>
    [ObservableProperty]
    private bool _isCommitTemplatesEnabled;

    [ObservableProperty]
    private ObservableCollection<PathTreeNode> _unstagedTreeItems = [];

    [ObservableProperty]
    private ObservableCollection<PathTreeNode> _stagedTreeItems = [];

    [ObservableProperty]
    private FileChangesSectionContext? _unstagedSectionContext;

    [ObservableProperty]
    private FileChangesSectionContext? _stagedSectionContext;

    // Amend state (plan §5.1). When enabled, the next commit replaces
    // HEAD — author preserved, message/description replaced, staged
    // changes folded into the new HEAD. `CanAmend` gates the checkbox
    // so users can't accidentally amend a commit that's already been
    // published; the check is refreshed on repo set and after each
    // commit. `_preAmendMessage`/`_preAmendDescription` let us restore
    // whatever the user had typed if they turn amend mode back off.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    [NotifyPropertyChangedFor(nameof(CommitButtonLabel))]
    private bool _isAmendMode;

    [ObservableProperty]
    private bool _canAmend;

    // Persisted UI state for the collapsible Options row that hosts the
    // amend checkbox. Loaded from settings at construction, saved on each
    // change so the panel remembers whether the user expanded it.
    [ObservableProperty]
    private bool _isOptionsExpanded;

    private string? _preAmendMessage;
    private string? _preAmendDescription;

    /// <summary>
    /// Event raised when a file is selected for diff viewing.
    /// </summary>
    public event EventHandler<FileSelectedEventArgs>? FileSelected;

    /// <summary>
    /// Event raised when a file is deleted or discarded.
    /// </summary>
    public event EventHandler<FileDeletedOrDiscardedEventArgs>? FileDeletedOrDiscarded;

    /// <summary>
    /// Maximum characters for commit message.
    /// </summary>
    public const int MaxMessageLength = 72;
    private const int MaxSummaryChars = 400000;

    /// <summary>
    /// Remaining characters for commit message.
    /// </summary>
    public int RemainingChars => MaxMessageLength - CommitMessage.Length;

    /// <summary>
    /// True if there are any working changes.
    /// </summary>
    public bool HasChanges => WorkingChanges?.HasChanges ?? false;

    /// <summary>
    /// True if can commit. In normal mode: requires staged files plus a
    /// non-empty message. In amend mode: just a non-empty message — the
    /// user may be amending solely to change the commit message, with no
    /// staged changes.
    /// </summary>
    public bool CanCommit =>
        !string.IsNullOrWhiteSpace(CommitMessage) &&
        CommitMessage.Length <= MaxMessageLength &&
        (IsAmendMode || WorkingChanges?.HasStagedChanges == true);

    /// <summary>
    /// Label shown on the primary commit button. Flips to "Amend" when
    /// amend mode is active so the user sees at a glance that their next
    /// action will rewrite HEAD rather than create a new commit.
    /// </summary>
    public string CommitButtonLabel => IsAmendMode ? "Amend" : "Commit";

    /// <summary>
    /// Summary of file changes for display.
    /// </summary>
    public string FileChangesSummary
    {
        get
        {
            if (WorkingChanges == null)
                return "No changes";

            var total = WorkingChanges.TotalChanges;
            var branch = WorkingChanges.BranchName;

            return total switch
            {
                0 => "No changes",
                1 => $"1 file change on {branch}",
                _ => $"{total} file changes on {branch}"
            };
        }
    }

    /// <summary>
    /// Returns the current repository's cancellation token. Set by
    /// MainViewModel so this VM's background git calls abort when the
    /// session is disposed on repo switch.
    /// </summary>
    public Func<CancellationToken>? GetSessionToken { get; set; }

    private CancellationToken SessionToken => GetSessionToken?.Invoke() ?? CancellationToken.None;

    public WorkingChangesViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IFileSystemService fileSystemService,
        IDialogService dialogService,
        IAiCommitMessageService aiCommitService,
        IGitignoreService gitignoreService,
        IExternalToolConfigService externalToolConfig,
        IExternalToolLauncherService externalToolLauncher,
        SettingsService settingsService,
        ICommitTemplateService commitTemplateService)
    {
        _gitService = gitService;
        _clipboardService = clipboardService;
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
        _aiCommitService = aiCommitService;
        _gitignoreService = gitignoreService;
        _externalToolConfig = externalToolConfig;
        _externalToolLauncher = externalToolLauncher;
        _settingsService = settingsService;
        _commitTemplateService = commitTemplateService ?? throw new ArgumentNullException(nameof(commitTemplateService));
        _isOptionsExpanded = _settingsService.LoadSettings().IsCommitOptionsExpanded;
        RefreshAiAvailability();
        RefreshCommitTemplatesEnabled();
        RefreshTemplates();
        _commitTemplateService.TemplatesChanged += OnTemplatesChanged;
        InitializeConventionalCommitsState();
    }

    private void OnTemplatesChanged(object? sender, EventArgs e) => RefreshTemplates();

    private void RefreshTemplates()
    {
        // ObservableCollection<T> mutation must happen on the UI thread —
        // CommitTemplates is bound to the popup picker. Build the new
        // list off-thread-safe (no I/O after GetAll returns) then publish
        // by replacing the collection.
        CommitTemplates = new ObservableCollection<Models.CommitTemplate>(_commitTemplateService.GetAll());
    }

    /// <summary>
    /// Templates available right now — built-ins, user globals, and any
    /// repo-scoped entries for the active repository. Re-published on
    /// every <see cref="ICommitTemplateService.TemplatesChanged"/> fire.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Models.CommitTemplate> _commitTemplates = [];

    partial void OnIsOptionsExpandedChanged(bool value)
    {
        var settings = _settingsService.LoadSettings();
        if (settings.IsCommitOptionsExpanded == value) return;
        settings.IsCommitOptionsExpanded = value;
        _settingsService.SaveSettings(settings);
    }

    /// <summary>
    /// Refresh whether any AI provider is connected.
    /// </summary>
    public void RefreshAiAvailability()
    {
        var settings = _settingsService.LoadSettings();
        IsAiAvailable = settings.IsClaudeConnected
                        || settings.IsGeminiConnected
                        || settings.IsCodexConnected
                        || !string.IsNullOrEmpty(settings.OllamaSelectedModel);
    }

    /// <summary>
    /// Re-read <see cref="AppSettings.CommitTemplatesEnabled"/>. Called
    /// after the settings dialog closes so a master-toggle change shows
    /// up on the commit panel without an app restart.
    /// </summary>
    public void RefreshCommitTemplatesEnabled()
    {
        IsCommitTemplatesEnabled = _settingsService.LoadSettings().CommitTemplatesEnabled;
    }

    /// <summary>
    /// Set the repository path and refresh working changes.
    /// </summary>
    public async Task SetRepositoryAsync(string? repoPath)
    {
        _repositoryPath = repoPath;
        IsAmendMode = false;
        // §5.15 — point the template service at the new repo so its
        // GetAll snapshot includes any repo-scoped templates from this
        // repository's .git/leaf/commit-templates.json.
        _commitTemplateService.SetActiveRepository(repoPath);
        await RefreshAsync();
        await RefreshAmendStateAsync();
        await RefreshExternalDiffToolAvailabilityAsync();
    }

    /// <summary>
    /// Clear all working changes state (used when no repository is selected).
    /// </summary>
    public void ClearWorkingChanges()
    {
        _repositoryPath = null;
        _commitTemplateService.SetActiveRepository(null);
        WorkingChanges = null;
        CommitMessage = string.Empty;
        CommitDescription = string.Empty;
        ErrorMessage = null;
        IsLoading = false;
        IsAmendMode = false;
        CanAmend = false;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(FileChangesSummary));
    }

    /// <summary>
    /// Set the working changes directly (synced from GitGraphViewModel).
    /// </summary>
    public void SetWorkingChanges(string repoPath, WorkingChangesInfo? workingChanges)
    {
        var repoChanged = !string.Equals(_repositoryPath, repoPath, StringComparison.OrdinalIgnoreCase);
        _repositoryPath = repoPath;
        WorkingChanges = workingChanges;

        if (workingChanges == null)
        {
            Log.Warn("WorkingChanges", "SetWorkingChanges: null data received");
        }

        if (repoChanged)
        {
            IsAmendMode = false;
        }

        // Force notification for dependent properties
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(FileChangesSummary));

        // Fire-and-forget — amend eligibility is a UI hint; worst case the
        // checkbox stays enabled for a tick longer than ideal.
        RefreshAmendStateAsync().FireAndForget(nameof(RefreshAmendStateAsync), isUserAction: false);
    }

    /// <summary>
    /// Recompute <see cref="CanAmend"/>: HEAD must exist and must not be
    /// the same commit the remote already has. Called on repo switch and
    /// after every commit/push that changes those conditions. Silently
    /// tolerates service failures — amend is a nicety, not a primary flow.
    /// </summary>
    private async Task RefreshAmendStateAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            CanAmend = false;
            return;
        }

        try
        {
            var head = await _gitService.GetHeadCommitAsync(_repositoryPath, cancellationToken: SessionToken);
            if (head == null)
            {
                CanAmend = false;
            }
            else
            {
                var isPushed = await _gitService.IsHeadPushedAsync(_repositoryPath, cancellationToken: SessionToken);
                CanAmend = !isPushed;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or System.IO.IOException
                                or UnauthorizedAccessException
                                or OperationCanceledException)
        {
            Log.Info("Amend", $"RefreshAmendState failed: {ex.GetType().Name}: {ex.Message}");
            CanAmend = false;
        }

        // If the user had amend mode on but the repo is no longer in an
        // amendable state (e.g. HEAD was just pushed), flip it off so the
        // next commit doesn't silently rewrite published history. The
        // OnIsAmendModeChanged handler restores their pre-amend draft.
        if (!CanAmend && IsAmendMode)
        {
            IsAmendMode = false;
        }
    }

    /// <summary>
    /// Handle amend-mode toggle: on enable, stash what the user had typed
    /// and populate message/description from HEAD. On disable, restore
    /// whatever was there before (so toggling doesn't lose drafts).
    /// </summary>
    partial void OnIsAmendModeChanged(bool value)
    {
        if (value)
        {
            _preAmendMessage = CommitMessage;
            _preAmendDescription = CommitDescription;
            LoadHeadMessageAsync().FireAndForget(nameof(LoadHeadMessageAsync), isUserAction: false);
        }
        else
        {
            CommitMessage = _preAmendMessage ?? string.Empty;
            CommitDescription = _preAmendDescription ?? string.Empty;
            _preAmendMessage = null;
            _preAmendDescription = null;
            // §5.15 Phase 4: keep the structured form in sync with the
            // restored pre-amend draft. Without this, exiting amend mode
            // while Conventional is on leaves the form holding the now-
            // stale HEAD message.
            SyncConventionalFieldsFromFreeform();
        }
    }

    private async Task LoadHeadMessageAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath)) return;

        try
        {
            var head = await _gitService.GetHeadCommitAsync(_repositoryPath, cancellationToken: SessionToken);
            if (head == null) return;

            // The user may have toggled amend mode off while this was in
            // flight. Dropping the result is safer than overwriting the
            // draft they just had restored.
            if (!IsAmendMode) return;

            // HEAD's full message is "<subject>\n\n<body...>" — split on
            // the first blank line so the subject and body map cleanly to
            // the message and description fields.
            var message = head.Message ?? string.Empty;
            var firstBlankLine = message.IndexOf("\n\n", StringComparison.Ordinal);
            if (firstBlankLine >= 0)
            {
                CommitMessage = message[..firstBlankLine].Trim();
                CommitDescription = message[(firstBlankLine + 2)..].TrimEnd();
            }
            else
            {
                CommitMessage = message.Trim();
                CommitDescription = string.Empty;
            }

            // §5.15 Phase 4: keep the structured form in sync when the user
            // amends a commit while Conventional mode is on, so editing
            // any structured field doesn't clobber HEAD's loaded message.
            SyncConventionalFieldsFromFreeform();
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or System.IO.IOException
                                or UnauthorizedAccessException
                                or OperationCanceledException)
        {
            Log.Info("Amend", $"LoadHeadMessage failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Refresh working changes from the repository.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            WorkingChanges = null;
            ErrorMessage = "No repository path set";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath, cancellationToken: SessionToken);
            ErrorMessage = $"Loaded: {WorkingChanges?.TotalChanges ?? 0} changes";
        }
        catch (Exception ex)
        {
            WorkingChanges = null;
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnWorkingChangesChanged(WorkingChangesInfo? value)
    {
        UnstagedTreeItems = BuildTree(value?.UnstagedFiles ?? []);
        StagedTreeItems = BuildTree(value?.StagedFiles ?? []);
        BuildSectionContexts();
    }

    /// <summary>
    /// Creates or updates the section context objects with current data and commands.
    /// </summary>
    private void BuildSectionContexts()
    {
        var isCompact = _settingsService.LoadSettings().CompactFileList;

        UnstagedSectionContext = new FileChangesSectionContext
        {
            SectionTitle = "Unstaged",
            IsStagedSection = false,
            FilesSource = WorkingChanges?.UnstagedFiles ?? [],
            TreeItemsSource = UnstagedTreeItems,
            PrimaryActionCommand = StageFileCommand,
            PrimaryActionText = "Stage",
            BulkActionCommand = StageAllCommand,
            BulkActionText = "Stage All",
            DiscardFileCommand = DiscardFileCommand,
            IgnoreFileCommand = IgnoreFileCommand,
            IgnoreExtensionCommand = IgnoreExtensionCommand,
            IgnoreDirectoryCommand = IgnoreDirectoryCommand,
            StashFileCommand = StashFileCommand,
            OpenFileCommand = OpenFileCommand,
            OpenInExplorerCommand = OpenInExplorerCommand,
            CopyFilePathCommand = CopyFilePathCommand,
            OpenInExternalDiffToolCommand = OpenInExternalDiffToolCommand,
            DeleteFileCommand = DeleteFileCommand,
            AdminDeleteCommand = AdminDeleteReservedFileCommand,
            FileSelectedCommand = SelectUnstagedFileCommand,
            FolderPrimaryActionCommand = StageFolderCommand,
            FolderDiscardCommand = DiscardFolderCommand,
            FolderIgnoreCommand = IgnoreFolderCommand,
            FolderOpenInExplorerCommand = OpenFolderInExplorerCommand,
            IsCompactFileList = isCompact
        };

        StagedSectionContext = new FileChangesSectionContext
        {
            SectionTitle = "Staged",
            IsStagedSection = true,
            FilesSource = WorkingChanges?.StagedFiles ?? [],
            TreeItemsSource = StagedTreeItems,
            PrimaryActionCommand = UnstageFileCommand,
            PrimaryActionText = "Unstage",
            BulkActionCommand = UnstageAllCommand,
            BulkActionText = "Unstage All",
            DiscardFileCommand = DiscardFileCommand,
            IgnoreFileCommand = IgnoreFileCommand,
            IgnoreExtensionCommand = IgnoreExtensionCommand,
            IgnoreDirectoryCommand = IgnoreDirectoryCommand,
            StashFileCommand = StashFileCommand,
            OpenFileCommand = OpenFileCommand,
            OpenInExplorerCommand = OpenInExplorerCommand,
            CopyFilePathCommand = CopyFilePathCommand,
            OpenInExternalDiffToolCommand = OpenInExternalDiffToolCommand,
            DeleteFileCommand = DeleteFileCommand,
            AdminDeleteCommand = null, // Not applicable for staged files
            FileSelectedCommand = SelectStagedFileCommand,
            FolderPrimaryActionCommand = UnstageFolderCommand,
            FolderDiscardCommand = DiscardFolderCommand,
            FolderIgnoreCommand = IgnoreFolderCommand,
            FolderOpenInExplorerCommand = OpenFolderInExplorerCommand,
            IsCompactFileList = isCompact
        };
    }

    /// <summary>
    /// Rebuild section contexts to pick up changed settings (e.g., compact file list).
    /// </summary>
    public void RefreshSectionContexts()
    {
        if (WorkingChanges != null)
            BuildSectionContexts();
    }

    /// <summary>
    /// Command to select an unstaged file for diff viewing.
    /// </summary>
    [RelayCommand]
    private void SelectUnstagedFile(FileStatusInfo? file)
    {
        if (file != null)
        {
            FileSelected?.Invoke(this, new FileSelectedEventArgs(file, isStaged: false));
        }
    }

    /// <summary>
    /// Command to select a staged file for diff viewing.
    /// </summary>
    [RelayCommand]
    private void SelectStagedFile(FileStatusInfo? file)
    {
        if (file != null)
        {
            FileSelected?.Invoke(this, new FileSelectedEventArgs(file, isStaged: true));
        }
    }

    /// <summary>
    /// Refreshes working changes and notifies dependent properties.
    /// </summary>
    private async Task RefreshAndNotifyAsync()
    {
        WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath!, cancellationToken: SessionToken);
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(FileChangesSummary));
    }

    /// <summary>
    /// Stage a single file.
    /// </summary>
    [RelayCommand]
    public async Task StageFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitService.StageFileAsync(_repositoryPath, file.Path, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage a single file.
    /// </summary>
    [RelayCommand]
    public async Task UnstageFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitService.UnstageFileAsync(_repositoryPath, file.Path, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Stage all modified files.
    /// </summary>
    [RelayCommand]
    public async Task StageAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        try
        {
            await _gitService.StageAllAsync(_repositoryPath, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage all failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage all staged files.
    /// </summary>
    [RelayCommand]
    public async Task UnstageAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        try
        {
            await _gitService.UnstageAllAsync(_repositoryPath, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage all failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Discard changes for a single file.
    /// </summary>
    [RelayCommand]
    public async Task DiscardFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Are you sure you want to discard changes to '{file.FileName}'?\n\nThis cannot be undone.",
            "Discard Changes");

        if (!confirmed)
            return;

        try
        {
            await _gitService.DiscardFileChangesAsync(_repositoryPath, file.Path, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
            FileDeletedOrDiscarded?.Invoke(this, new FileDeletedOrDiscardedEventArgs(file.Path));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add a specific file to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitignoreService.IgnoreFileAsync(_repositoryPath, file);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add all files with a specific extension to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreExtensionAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null || string.IsNullOrEmpty(file.Extension))
            return;

        try
        {
            await _gitignoreService.IgnoreExtensionAsync(_repositoryPath, file);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore extension failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add all files in a specific directory to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreDirectoryAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null || string.IsNullOrEmpty(file.Directory))
            return;

        try
        {
            await _gitignoreService.IgnoreDirectoryAsync(_repositoryPath, file);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore directory failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Stash a single file.
    /// </summary>
    [RelayCommand]
    public async Task StashFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            // Stage the file first, then stash only staged changes
            await _gitService.StageFileAsync(_repositoryPath, file.Path, cancellationToken: SessionToken);
            await _gitService.StashStagedAsync(_repositoryPath, $"Stash: {file.FileName}", cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stash failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Open the file's containing folder in Windows Explorer and select the file.
    /// </summary>
    [RelayCommand]
    public void OpenInExplorer(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        // Normalize path separators (Git uses forward slashes, worktree paths may too)
        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedFilePath));

        if (File.Exists(fullPath))
        {
            // Open Explorer and select the file
            _fileSystemService.OpenInExplorerAndSelect(fullPath);
        }
        else if (Directory.Exists(fullPath))
        {
            // Open the directory
            _fileSystemService.OpenInExplorer(fullPath);
        }
        else
        {
            // File doesn't exist (deleted), open the containing folder
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                _fileSystemService.RevealInExplorer(directory);
            }
        }
    }

    /// <summary>
    /// Open the file using the default associated application.
    /// </summary>
    [RelayCommand]
    public void OpenFile(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedFilePath));
        if (!File.Exists(fullPath))
            return;

        _fileSystemService.OpenWithDefaultApp(fullPath);
    }

    private static ObservableCollection<PathTreeNode> BuildTree(IEnumerable<FileStatusInfo> files)
    {
        var roots = new ObservableCollection<PathTreeNode>();
        var dirLookup = new Dictionary<string, PathTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            var normalized = file.Path.Replace('\\', '/');
            var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            PathTreeNode? parent = null;
            var currentPath = string.Empty;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                bool isFile = i == parts.Length - 1;

                if (isFile)
                {
                    var fileNode = new PathTreeNode(part, currentPath, isFile: true, file, isRoot: parent == null);
                    if (parent == null)
                    {
                        roots.Add(fileNode);
                    }
                    else
                    {
                        parent.Children.Add(fileNode);
                    }
                }
                else
                {
                    if (!dirLookup.TryGetValue(currentPath, out var dirNode))
                    {
                        dirNode = new PathTreeNode(part, currentPath, isFile: false);
                        dirLookup[currentPath] = dirNode;

                        if (parent == null)
                        {
                            roots.Add(dirNode);
                        }
                        else
                        {
                            parent.Children.Add(dirNode);
                        }
                    }

                    parent = dirNode;
                }
            }
        }

        SortNodes(roots);
        return roots;
    }

    private static void SortNodes(ObservableCollection<PathTreeNode> nodes)
    {
        var sorted = nodes
            .OrderBy(n => n.IsFile)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        nodes.Clear();
        foreach (var node in sorted)
        {
            nodes.Add(node);
            if (!node.IsFile && node.Children.Count > 0)
            {
                SortNodes(node.Children);
            }
        }
    }

    /// <summary>
    /// Copy the file's full path to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyFilePath(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedFilePath));
        _clipboardService.SetText(fullPath);
    }

    /// <summary>
    /// Diff a working-changes file in the configured external diff tool.
    /// For unstaged files the baseline is the index (or HEAD if the file
    /// isn't in the index) and the right side is the live working copy
    /// — so edits saved through the tool land back in the working tree.
    /// For staged files both sides are snapshots (HEAD vs index), so
    /// the tool sees read-only content. Disabled when no external diff
    /// tool is configured.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasExternalDiffTool))]
    public async Task OpenInExternalDiffToolAsync(FileStatusInfo? file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            var diffTool = await _externalToolConfig.GetCurrentToolAsync(
                _repositoryPath, ExternalToolKind.Diff, SessionToken);
            if (diffTool == null)
            {
                // Config changed out from under us between the CanExecute
                // check and here. Refresh and give up.
                HasExternalDiffTool = false;
                return;
            }

            // Pick the baseline that matches what the user sees in Leaf's
            // internal two-pane diff: unstaged = index-or-HEAD vs working,
            // staged = HEAD vs index. Using GetFileDiffAsync("HEAD", ...)
            // would diff parent-of-HEAD vs HEAD, which is wrong on both
            // counts.
            var (oldContent, stagedNewContent) = file.IsStaged
                ? await _gitService.GetStagedFileDiffAsync(_repositoryPath, file.Path, SessionToken)
                : await _gitService.GetUnstagedFileDiffAsync(_repositoryPath, file.Path, SessionToken);

            var normalizedFilePath = file.Path.Replace('/', '\\');
            var workingPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedFilePath));

            var tempDir = Path.Combine(Path.GetTempPath(), "LeafDiff", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var fileName = Path.GetFileName(file.Path);
            var extension = Path.GetExtension(file.Path);
            var leftPath = Path.Combine(tempDir, $"{fileName}.baseline{extension}");

            try
            {
                await File.WriteAllTextAsync(leftPath, oldContent, SessionToken);

                if (file.IsStaged)
                {
                    // Staged snapshot — write the index content to a temp
                    // file too. Editing it wouldn't round-trip anywhere.
                    var rightPath = Path.Combine(tempDir, $"{fileName}.staged{extension}");
                    await File.WriteAllTextAsync(rightPath, stagedNewContent, SessionToken);
                    await _externalToolLauncher.LaunchDiffAsync(diffTool, leftPath, rightPath, SessionToken);
                }
                else if (File.Exists(workingPath))
                {
                    // Unstaged — point the tool at the live working file
                    // so in-place edits make it back to disk.
                    await _externalToolLauncher.LaunchDiffAsync(diffTool, leftPath, workingPath, SessionToken);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("ExternalDiff", $"Failed to clean up temp directory: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or IOException
                                or UnauthorizedAccessException
                                or System.ComponentModel.Win32Exception
                                or OperationCanceledException)
        {
            Log.Warn("ExternalDiff", $"Open diff in external tool failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-check whether an external diff tool is configured for this
    /// repository. Called on repo switch and after the Settings dialog
    /// closes so the menu item's enabled state matches git config.
    /// </summary>
    public async Task RefreshExternalDiffToolAvailabilityAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            HasExternalDiffTool = false;
            return;
        }

        try
        {
            var tool = await _externalToolConfig.GetCurrentToolAsync(
                _repositoryPath, ExternalToolKind.Diff, SessionToken);
            HasExternalDiffTool = tool != null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or OperationCanceledException)
        {
            Log.Info("ExternalDiff", $"Availability probe failed: {ex.Message}");
            HasExternalDiffTool = false;
        }
    }

    /// <summary>
    /// Delete a file from the filesystem.
    /// </summary>
    [RelayCommand]
    public async Task DeleteFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Are you sure you want to delete '{file.FileName}'?\n\nThis will permanently delete the file from disk and cannot be undone.",
            "Delete File");

        if (!confirmed)
            return;

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, file.Path));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            // If the file was staged, remove it from the index too —
            // otherwise it lingers as a staged entry after disk deletion.
            if (file.IsStaged)
            {
                await _gitService.UnstageFileAsync(_repositoryPath, file.Path, cancellationToken: SessionToken);
            }

            await RefreshAndNotifyAsync();
            FileDeletedOrDiscarded?.Invoke(this, new FileDeletedOrDiscardedEventArgs(file.Path));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Delete failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Delete a Windows reserved filename (nul, con, prn, etc.) using admin privileges.
    /// These files cannot be deleted normally due to Windows restrictions.
    /// </summary>
    [RelayCommand]
    public async Task AdminDeleteReservedFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Delete reserved file '{file.FileName}'?\n\nThis requires administrator privileges and will run a command to rename and delete the file.",
            "Admin Delete");

        if (!confirmed)
            return;

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, file.Path));
            var directory = Path.GetDirectoryName(fullPath) ?? _repositoryPath;
            var tempName = $"_leaf_temp_{Guid.NewGuid():N}.tmp";

            // Build the batch script to rename and delete the reserved file
            // Uses \\?\ prefix to bypass Windows reserved name restrictions
            var script = $@"
@echo off
cd /d ""{directory}""
ren ""\\?\{fullPath}"" ""{tempName}""
del ""{Path.Combine(directory, tempName)}""
exit /b %errorlevel%
";

            var batchFile = Path.Combine(Path.GetTempPath(), $"leaf_admin_delete_{Guid.NewGuid():N}.bat");
            await File.WriteAllTextAsync(batchFile, script);

            // Run with admin privileges
            var startInfo = new ProcessStartInfo
            {
                FileName = batchFile,
                Verb = "runas", // Request admin elevation
                UseShellExecute = true,
                CreateNoWindow = false
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();

                // Clean up batch file — best-effort, if it sticks around
                // it's in %TEMP% and the OS will reclaim it. Trace-log per
                // plan §2.2 so recurring failures are diagnosable.
                try
                {
                    File.Delete(batchFile);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Info("WorkingChanges", $"Temp batch file delete skipped for {batchFile}: {ex.Message}");
                }

                if (process.ExitCode == 0)
                {
                    await RefreshAndNotifyAsync();
                    FileDeletedOrDiscarded?.Invoke(this, new FileDeletedOrDiscardedEventArgs(file.Path));
                }
                else
                {
                    ErrorMessage = $"Admin delete failed with exit code {process.ExitCode}";
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
            ErrorMessage = "Admin delete cancelled by user.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Admin delete failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Discard all working directory changes.
    /// </summary>
    [RelayCommand]
    public async Task DiscardAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Are you sure you want to discard all changes? This cannot be undone.",
            "Discard All Changes");

        if (!confirmed)
            return;

        try
        {
            await _gitService.DiscardAllChangesAsync(_repositoryPath, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
            FileDeletedOrDiscarded?.Invoke(this, new FileDeletedOrDiscardedEventArgs(affectsAllFiles: true));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Commit staged changes — or amend HEAD when <see cref="IsAmendMode"/>
    /// is active.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCommit))]
    public async Task CommitAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath) || !CanCommit)
            return;

        var amend = IsAmendMode;
        try
        {
            IsLoading = true;

            var description = string.IsNullOrWhiteSpace(CommitDescription)
                ? null
                : CommitDescription.Trim();

            await _gitService.CommitAsync(
                _repositoryPath,
                CommitMessage.Trim(),
                description,
                amend: amend,
                cancellationToken: SessionToken);

            // §5.15 Phase 4: remember Conventional Commits scope on
            // successful commit so the editable scope ComboBox can offer
            // it next time. Capture before clearing the structured fields.
            if (UseConventionalCommitsForm)
                RememberConventionalScope(ConventionalScope);

            // Clear form + flip out of amend mode on success. The
            // pre-amend buffers are deliberately not restored — after a
            // successful amend the buffers are stale and a fresh state is
            // what the user expects.
            _preAmendMessage = null;
            _preAmendDescription = null;
            IsAmendMode = false;
            // Reset structured fields when active so the next commit
            // doesn't carry over the previous one's body / footer / etc.
            // The toggle stays on — that's a persisted user preference.
            if (UseConventionalCommitsForm)
            {
                _suppressConventionalRebuild = true;
                try
                {
                    ConventionalScope = string.Empty;
                    ConventionalDescription = string.Empty;
                    ConventionalBody = string.Empty;
                    ConventionalIsBreaking = false;
                    ConventionalFooter = string.Empty;
                }
                finally
                {
                    _suppressConventionalRebuild = false;
                }
            }
            CommitMessage = string.Empty;
            CommitDescription = string.Empty;

            await RefreshAndNotifyAsync();
            await RefreshAmendStateAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{(amend ? "Amend" : "Commit")} failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task AutoFillCommitMessageAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            ErrorMessage = "No repository selected.";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // Cancel any existing AI generation — two rapid AutoFill clicks
            // can otherwise race the prior finally block's dispose/null set
            // against this block's Cancel+Dispose+assign sequence.
            var cancellationToken = CancellationTokenSourceExtensions
                .ReplaceAndCancel(ref _aiCancellationTokenSource)
                .Token;

            Log.Info("WorkingChanges", $"AutoFill start: repo={_repositoryPath}");

            // Get staged diff summary
            var summary = await _gitService.GetStagedSummaryAsync(_repositoryPath, cancellationToken: SessionToken);
            if (summary.Length > MaxSummaryChars)
            {
                ErrorMessage = $"Staged summary is too large to send ({summary.Length} chars).";
                Log.Warn("WorkingChanges", $"AutoFill blocked: summary length {summary.Length} exceeds limit {MaxSummaryChars}.");
                return;
            }

            Log.Info("WorkingChanges", $"AutoFill summary length: {summary.Length}");

            var (message, description, error) = await _aiCommitService.GenerateCommitMessageAsync(
                summary, _repositoryPath, cancellationToken);

            if (error != null)
            {
                ErrorMessage = error;
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                ErrorMessage = "AI returned an empty commit message.";
                return;
            }

            message = message.Trim();
            if (message.Length > MaxMessageLength)
            {
                message = message[..MaxMessageLength].TrimEnd();
                ErrorMessage = $"AI message trimmed to {MaxMessageLength} characters.";
            }

            CommitMessage = message;
            CommitDescription = description?.Trim() ?? string.Empty;

            // §5.15 Phase 4: in Conventional Commits mode the structured
            // fields drive CommitMessage/CommitDescription via Rebuild
            // on every field change, so writing these properties directly
            // leaves the form out of sync. Mirror the freeform values
            // back into the structured fields so the next field edit
            // doesn't clobber the AI output. No-op when the toggle is off.
            SyncConventionalFieldsFromFreeform();
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "AI generation cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"AI commit failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            CancellationTokenSourceExtensions.DisposeAndClear(ref _aiCancellationTokenSource);
        }
    }

    /// <summary>
    /// Cancels any in-progress AI commit message generation.
    /// </summary>
    [RelayCommand]
    public void CancelAutoFill()
    {
        // Local copy under atomic read — if the finally block nulls the field
        // between our null check and Cancel() we'd otherwise NRE or hit
        // ObjectDisposedException.
        var cts = Interlocked.CompareExchange(ref _aiCancellationTokenSource, null, null);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { /* already finished */ }
    }

    partial void OnCommitMessageChanged(string value)
    {
        // Notify CanCommit changed when message changes
        CommitCommand.NotifyCanExecuteChanged();
    }

    // --- Folder context menu commands ---

    /// <summary>
    /// Stage all files within a folder tree node.
    /// </summary>
    [RelayCommand]
    public async Task StageFolderAsync(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        try
        {
            foreach (var file in folder.GetAllFiles())
                await _gitService.StageFileAsync(_repositoryPath, file.Path, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage folder failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage all files within a folder tree node.
    /// </summary>
    [RelayCommand]
    public async Task UnstageFolderAsync(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        try
        {
            foreach (var file in folder.GetAllFiles())
                await _gitService.UnstageFileAsync(_repositoryPath, file.Path, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage folder failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Discard all changes in a folder tree node.
    /// </summary>
    [RelayCommand]
    public async Task DiscardFolderAsync(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        var files = folder.GetAllFiles().ToList();
        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Discard all changes in '{folder.RelativePath}/'?\n\n{files.Count} file(s) will be reverted. This cannot be undone.",
            "Discard Folder Changes");

        if (!confirmed)
            return;

        try
        {
            foreach (var file in files)
                await _gitService.DiscardFileChangesAsync(_repositoryPath, file.Path, cancellationToken: SessionToken);
            await RefreshAndNotifyAsync();
            FileDeletedOrDiscarded?.Invoke(this, new FileDeletedOrDiscardedEventArgs(affectsAllFiles: true));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard folder failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add a folder to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreFolderAsync(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        try
        {
            var trackedFiles = folder.GetAllFiles()
                .Where(f => f.Status != FileChangeStatus.Untracked)
                .ToList();
            await _gitignoreService.IgnoreDirectoryPathAsync(_repositoryPath, folder.RelativePath, trackedFiles);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore folder failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Open a folder in Windows Explorer.
    /// </summary>
    [RelayCommand]
    public void OpenFolderInExplorer(PathTreeNode folder)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || folder == null)
            return;

        var normalizedPath = folder.RelativePath.Replace('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_repositoryPath, normalizedPath));

        if (Directory.Exists(fullPath))
            _fileSystemService.OpenInExplorer(fullPath);
        else
            _fileSystemService.RevealInExplorer(_repositoryPath);
    }
}
