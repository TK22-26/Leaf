using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;
using Leaf.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.ViewModels;

/// <summary>
/// Orchestrator for the submodule workspace. Owns the list of tiles
/// (parent first, pinned submodules next in user order, remaining
/// submodules alphabetically), manages their lifecycle around grid /
/// single mode transitions, and exposes the workspace-level bulk
/// commands (Commit all, Push all, Pull all, Fetch all, Switch to
/// branch, Merge into branch) for the toolbar above the grid.
/// </summary>
/// <remarks>
/// <para>The workspace VM is constructed per host VM (one per
/// <c>MainViewModel</c>) and lives for the host's lifetime. The grid
/// of tiles inside is rebuilt every time the user enters grid mode for
/// a different parent — <see cref="LoadAsync"/> tears down the prior
/// tile set and pages in the new one based on the parent's submodule
/// list.</para>
///
/// <para>Bulk commands are intentionally left empty in Phase A; this
/// file establishes the shape (collection of tiles, mode property,
/// host-supplied dependencies) so the layout in Phase B has something
/// to bind to. Phase C fills the commands in.</para>
/// </remarks>
public partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGitService _gitService;
    private readonly SettingsService _settingsService;
    private readonly IRepositoryManagementService _repositoryService;
    private readonly IBranchColorPaletteRegistry _paletteRegistry;
    private readonly IWorkspaceConfigService _workspaceConfig;
    private readonly CredentialService _credentialService;
    private readonly IAiCommitMessageService _aiCommitService;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// Ordered tile collection. WPF binds the workspace grid against
    /// this; the order here drives the visual order (parent → pinned →
    /// alpha rest). Mutations happen on the UI thread inside
    /// <see cref="LoadAsync"/>.
    /// </summary>
    public ObservableCollection<SubmoduleTileViewModel> Tiles { get; } = [];

    /// <summary>
    /// Serialises <see cref="LoadAsync"/> calls. Without it, the
    /// repo-selection restore path (fire-and-forget) and a concurrent
    /// user click on the Grid toggle each ran their own DisposeTiles +
    /// add cycle interleaved, doubling every tile. The semaphore
    /// guarantees the second caller waits until the first finishes,
    /// then runs DisposeTiles itself for a clean rebuild.
    /// </summary>
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    /// <summary>
    /// Current view mode. <see cref="WorkspaceMode.Single"/> hides the
    /// grid and shows the existing single-repo body; <see cref="WorkspaceMode.Grid"/>
    /// shows the tiled grid and hides the Branches panel + right detail
    /// pane.
    /// </summary>
    [ObservableProperty]
    private WorkspaceMode _mode = WorkspaceMode.Single;

    /// <summary>
    /// Parent repository the workspace is centred on. Used by tile
    /// ordering (parent is always first) and by the bulk commands
    /// (parent is the orchestrator — pushed last, committed last).
    /// </summary>
    [ObservableProperty]
    private RepositoryInfo? _parent;

    public WorkspaceViewModel(
        IServiceScopeFactory scopeFactory,
        IGitService gitService,
        SettingsService settingsService,
        IRepositoryManagementService repositoryService,
        IBranchColorPaletteRegistry paletteRegistry,
        IWorkspaceConfigService workspaceConfig,
        CredentialService credentialService,
        IAiCommitMessageService aiCommitService,
        INotificationService notificationService,
        IDialogService dialogService)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));
        _paletteRegistry = paletteRegistry ?? throw new ArgumentNullException(nameof(paletteRegistry));
        _workspaceConfig = workspaceConfig ?? throw new ArgumentNullException(nameof(workspaceConfig));
        _credentialService = credentialService ?? throw new ArgumentNullException(nameof(credentialService));
        _aiCommitService = aiCommitService ?? throw new ArgumentNullException(nameof(aiCommitService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

    /// <summary>
    /// Build the tile set for <paramref name="parent"/>. The parent's
    /// own <see cref="GitGraphViewModel"/> is supplied by the host so
    /// the parent tile reuses the existing app-lifetime graph rather
    /// than spinning up a duplicate. Submodule tiles get their own
    /// scope + graph each.
    /// </summary>
    /// <remarks>
    /// Safe to call repeatedly — disposes the previous tile set first.
    /// Pinned tile order comes from <c>[leaf "workspace"]
    /// pinnedorder</c>; unpinned submodules fall through alphabetically.
    /// </remarks>
    public async Task LoadAsync(RepositoryInfo parent, GitGraphViewModel parentGraph, CancellationToken cancellationToken = default)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await LoadAsyncCore(parent, parentGraph, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadAsyncCore(RepositoryInfo parent, GitGraphViewModel parentGraph, CancellationToken cancellationToken)
    {
        Parent = parent;
        DisposeTiles();

        // Parent tile first — always position 0, always top-left.
        var parentName = string.IsNullOrEmpty(parent.Name)
            ? Path.GetFileName(parent.Path) ?? parent.Path
            : parent.Name;
        var parentTile = SubmoduleTileViewModel.CreateParent(parent.Path, parentName, parentGraph, parent);
        parentTile.Workspace = this;
        Tiles.Add(parentTile);

        // Snapshot the parent's submodule list. We use the explicit
        // GitService call rather than rummaging through BranchCategories
        // so the workspace works whether or not the sidebar has loaded
        // its submodule section yet.
        var submodules = await _gitService.GetSubmodulesAsync(parent.Path, cancellationToken).ConfigureAwait(true);
        if (submodules.Count == 0)
        {
            return;
        }

        // Apply the pinned-order preference: pinned submodules render
        // immediately after the parent in pin order; everything else
        // alphabetised. The pinned list comes from .git/config so any
        // git client sees the same ordering on read.
        var pinnedOrder = await _workspaceConfig.GetPinnedTileOrderAsync(parent.Path, cancellationToken).ConfigureAwait(true);
        var pinnedSet = new HashSet<string>(pinnedOrder, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<SubmoduleInfo>();
        foreach (var pinnedPath in pinnedOrder)
        {
            var match = submodules.FirstOrDefault(s => string.Equals(s.Path, pinnedPath, StringComparison.OrdinalIgnoreCase));
            if (match != null) ordered.Add(match);
        }
        ordered.AddRange(submodules
            .Where(s => !pinnedSet.Contains(s.Path))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase));

        foreach (var sm in ordered)
        {
            // Absolute path = parent's path joined with submodule's
            // relative path. We don't materialise tiles for
            // uninitialised submodules differently from initialised
            // ones here — the tile's body decides what to render based
            // on its loaded RepositoryInfo (Phase F adds the
            // "Initialize" CTA for empty checkouts).
            var fullPath = Path.GetFullPath(Path.Combine(parent.Path, sm.Path));
            var tile = SubmoduleTileViewModel.CreateSubmodule(
                _scopeFactory, _gitService, _settingsService,
                _repositoryService, _paletteRegistry,
                fullPath, sm.Name);
            tile.Workspace = this;
            tile.IsPinned = pinnedSet.Contains(sm.Path);
            Tiles.Add(tile);

            // Kick off the tile's repo info + graph load in the
            // background. Fire-and-forget so all tiles paginate in
            // parallel without blocking grid-mode entry; each load is
            // bounded by the tile's own session cancellation token, so
            // closing grid mode tears them down cleanly.
            _ = LoadTileAsync(tile);
        }
    }

    private async Task LoadTileAsync(SubmoduleTileViewModel tile)
    {
        try
        {
            // Uninitialized-submodule probe. If the recorded path has
            // no .git entry (file or directory) the submodule was
            // declared in .gitmodules but never checked out — common
            // after a fresh non-recursive clone of the parent. Flag
            // the tile so the body switches to the Initialize CTA;
            // skip the rest of the load since GetRepositoryInfoFastAsync
            // would just throw on an empty directory.
            if (!tile.IsParent && IsSubmoduleUninitialized(tile.RepositoryPath))
            {
                tile.IsUninitialized = true;
                return;
            }
            tile.IsUninitialized = false;

            var info = await _gitService.GetRepositoryInfoFastAsync(tile.RepositoryPath, tile.Token).ConfigureAwait(true);
            tile.Repository = info;

            // Auto-register submodule tiles as tracked repositories so
            // path-keyed services (AutoCommitService, the repo lookup
            // used by some toast clicks, etc.) can find them. Mirrors
            // OpenSubmoduleAsRepositoryAsync's contract: IsUserAdded
            // stays false so the submodule shows up under its parent
            // in the sidebar rather than as a top-level entry. Parent
            // tiles are already registered by definition — they're
            // SelectedRepository — so skip.
            if (!tile.IsParent && _repositoryService.FindRepository(tile.RepositoryPath) is null)
            {
                info.ParentRepositoryPath = Parent?.Path;
                info.IsUserAdded = false;
                _repositoryService.AddRepository(info);
            }

            if (tile.Graph != null)
            {
                await tile.Graph.LoadRepositoryAsync(tile.RepositoryPath).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Tile disposed while loading — expected.
        }
        catch (Exception ex)
        {
            Log.Warn("Workspace", $"LoadTileAsync failed for {tile.RepositoryPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Persist the user's chosen mode for the active parent. Best-effort
    /// — failure to write doesn't affect the runtime state, the next
    /// successful write reconciles.
    /// </summary>
    public async Task SaveModeAsync(WorkspaceMode mode, CancellationToken cancellationToken = default)
    {
        if (Parent is null) return;
        await _workspaceConfig.SetModeAsync(Parent.Path, mode, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Fires when a tile asks to be opened in single view (zoom-in
    /// icon, "Open in single view" overflow item). The host
    /// <c>MainViewModel</c> subscribes and re-routes single-view
    /// selection through the existing repository-management flow so
    /// the rest of the app sees a normal repo switch.
    /// </summary>
    public event EventHandler<SubmoduleTileViewModel>? TileOpenInSingleViewRequested;

    /// <summary>
    /// Flip a submodule's pinned state and reorder the tile list so
    /// the change is reflected immediately. Persists through
    /// <see cref="IWorkspaceConfigService"/> so the order survives a
    /// restart and stays visible to other Git clients.
    /// </summary>
    public async Task TogglePinAsync(SubmoduleTileViewModel tile, CancellationToken cancellationToken = default)
    {
        if (Parent is null || tile.IsParent) return;

        // Pin key is the submodule's PATH RELATIVE TO THE PARENT —
        // matches what .gitmodules stores, so any Git client sees a
        // stable identifier. Tiles hold an absolute repo path, so
        // re-derive the relative segment.
        var rel = ToRelativePath(Parent.Path, tile.RepositoryPath);
        if (string.IsNullOrEmpty(rel)) return;

        var current = (await _workspaceConfig.GetPinnedTileOrderAsync(Parent.Path, cancellationToken).ConfigureAwait(true)).ToList();
        if (current.Remove(rel))
        {
            // Removed → unpinned. The tile falls back into the
            // alphabetical block below the remaining pinned tiles.
            tile.IsPinned = false;
        }
        else
        {
            // Newly pinned — append so the user's last-pinned tile
            // lands at the bottom of the pinned section. (A future
            // drag-to-reorder gesture can edit this list at arbitrary
            // positions; the storage model already allows it.)
            current.Add(rel);
            tile.IsPinned = true;
        }
        await _workspaceConfig.SetPinnedTileOrderAsync(Parent.Path, current, cancellationToken).ConfigureAwait(true);
        ReorderTiles(current);
    }

    /// <summary>
    /// Invoked by a tile's <c>OpenInSingleView</c> command. Forwards
    /// to <see cref="TileOpenInSingleViewRequested"/> so the host can
    /// drop grid mode and route through its existing repo-selection
    /// plumbing — the workspace deliberately doesn't manipulate
    /// <c>MainViewModel.SelectedRepository</c> directly.
    /// </summary>
    public Task OpenTileInSingleViewAsync(SubmoduleTileViewModel tile)
    {
        TileOpenInSingleViewRequested?.Invoke(this, tile);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Drilldown shortcut: Ctrl+1..9 opens the tile at position
    /// <paramref name="oneBasedIndex"/> in single view. Index 1 is the
    /// parent (always first), 2..N follow the displayed tile order
    /// (pinned submodules first, then the rest). Out-of-range indices
    /// are no-ops so an unused Ctrl+N gesture doesn't surprise the
    /// user with an error.
    /// </summary>
    [RelayCommand]
    public async Task FocusTileByIndexAsync(int oneBasedIndex)
    {
        if (Mode != WorkspaceMode.Grid) return;
        var zero = oneBasedIndex - 1;
        if (zero < 0 || zero >= Tiles.Count) return;
        await OpenTileInSingleViewAsync(Tiles[zero]);
    }

    /// <summary>
    /// Re-run the per-tile load for <paramref name="tile"/>. Pulls a
    /// fresh RepositoryInfo and asks the tile's GitGraphViewModel to
    /// reload. The parent tile reuses the host's graph, so a parent
    /// refresh updates that single shared VM.
    /// </summary>
    public Task RefreshTileAsync(SubmoduleTileViewModel tile)
    {
        return LoadTileAsync(tile);
    }

    /// <summary>
    /// Stage-all + AI-commit a single tile. Uses the same
    /// <see cref="IAiCommitMessageService"/> the single-repo
    /// working-changes pane uses, so PATH resolution / .cmd-wrapper
    /// handling / provider selection all match what the user gets
    /// elsewhere in the app.
    /// </summary>
    public async Task CommitTileAsync(SubmoduleTileViewModel tile)
    {
        try
        {
            // Empty repo guard — nothing to commit, don't drag the user through an AI call.
            var changes = await _gitService.GetWorkingChangesAsync(tile.RepositoryPath, tile.Token);
            if (!changes.HasChanges)
            {
                _notificationService.Show("Nothing to commit", $"{tile.Name}: no changes.",
                    NotificationType.Information, Models.NotificationCategory.MergeAndRebase);
                return;
            }

            // Stage everything.
            await _gitService.StageAllAsync(tile.RepositoryPath, tile.Token);

            // Generate the message via the real AI pipeline (same path
            // the single-repo working-changes pane drives). The
            // service handles provider selection, PATH resolution
            // through AiCliRunner, .cmd-wrapper invocation, etc.
            var diffText = await _gitService.GetStagedSummaryAsync(tile.RepositoryPath, cancellationToken: tile.Token);
            var (msg, desc, err) = await _aiCommitService.GenerateCommitMessageAsync(diffText, tile.RepositoryPath, tile.Token);
            if (!string.IsNullOrEmpty(err) || string.IsNullOrWhiteSpace(msg))
            {
                _notificationService.Show(
                    "Commit failed",
                    $"{tile.Name}: AI commit message generation failed: {err ?? "empty message"}",
                    NotificationType.Error);
                return;
            }

            await _gitService.CommitAsync(tile.RepositoryPath, msg!, desc, cancellationToken: tile.Token);
            await LoadTileAsync(tile);
            _notificationService.Show(
                "Commit complete",
                $"{tile.Name}: {msg}",
                NotificationType.Success,
                Models.NotificationCategory.MergeAndRebase);
        }
        catch (Exception ex)
        {
            Log.Error("Workspace", $"CommitTile {tile.RepositoryPath} threw", ex);
            _notificationService.Show("Commit failed", $"{tile.Name}: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Push the tile's current branch using the credential key
    /// resolved from its tracking remote URL. Path-only call into
    /// <see cref="IGitService.PushAsync"/> — no per-repo session
    /// rotation, just the same code the toolbar Push button drives
    /// against a single repo, parameterised by the tile's path.
    /// </summary>
    public async Task PushTileAsync(SubmoduleTileViewModel tile)
    {
        try
        {
            var remotes = await _gitService.GetRemotesAsync(tile.RepositoryPath, cancellationToken: tile.Token);
            if (remotes.Count == 0)
            {
                // Local-only repo (a fresh init without a remote
                // configured). Nothing to push — surface as an info
                // skip rather than a red "failed" toast.
                _notificationService.Show("Push skipped", $"{tile.Name} has no remote configured.",
                    NotificationType.Information, Models.NotificationCategory.SyncOperations);
                return;
            }
            var trackingRemote = remotes.FirstOrDefault(r => r.Name == "origin")?.Url
                                ?? remotes.FirstOrDefault()?.Url;
            var credentialKey = _credentialService.ResolveActiveCredentialKey(trackingRemote);

            await _gitService.PushAsync(tile.RepositoryPath, credentialKey: credentialKey, cancellationToken: tile.Token);
            await LoadTileAsync(tile);
            _notificationService.Show("Push complete", $"{tile.Name}", NotificationType.Success, Models.NotificationCategory.SyncOperations);
        }
        catch (Exception ex)
        {
            Log.Error("Workspace", $"PushTile {tile.RepositoryPath} threw", ex);
            _notificationService.Show("Push failed", $"{tile.Name}: {ex.Message}", NotificationType.Error);
        }
    }

    public async Task PullTileAsync(SubmoduleTileViewModel tile)
    {
        try
        {
            var remotes = await _gitService.GetRemotesAsync(tile.RepositoryPath, cancellationToken: tile.Token);
            if (remotes.Count == 0)
            {
                _notificationService.Show("Pull skipped", $"{tile.Name} has no remote configured.",
                    NotificationType.Information, Models.NotificationCategory.SyncOperations);
                return;
            }
            var trackingRemote = remotes.FirstOrDefault(r => r.Name == "origin")?.Url
                                ?? remotes.FirstOrDefault()?.Url;
            var credentialKey = _credentialService.ResolveActiveCredentialKey(trackingRemote);

            await _gitService.PullAsync(tile.RepositoryPath, credentialKey, cancellationToken: tile.Token);
            await LoadTileAsync(tile);
            _notificationService.Show("Pull complete", $"{tile.Name}", NotificationType.Success, Models.NotificationCategory.SyncOperations);
        }
        catch (Exception ex)
        {
            Log.Error("Workspace", $"PullTile {tile.RepositoryPath} threw", ex);
            _notificationService.Show("Pull failed", $"{tile.Name}: {ex.Message}", NotificationType.Error);
        }
    }

    public async Task FetchTileAsync(SubmoduleTileViewModel tile)
    {
        try
        {
            var remotes = await _gitService.GetRemotesAsync(tile.RepositoryPath, cancellationToken: tile.Token);
            if (remotes.Count == 0)
            {
                _notificationService.Show("Fetch skipped", $"{tile.Name} has no remote configured.",
                    NotificationType.Information, Models.NotificationCategory.SyncOperations);
                return;
            }
            var originRemote = remotes.FirstOrDefault(r => r.Name == "origin") ?? remotes.First();
            var credentialKey = _credentialService.ResolveActiveCredentialKey(originRemote.Url);

            await _gitService.FetchAsync(
                tile.RepositoryPath,
                originRemote.Name,
                credentialKey,
                cancellationToken: tile.Token);
            await LoadTileAsync(tile);
            _notificationService.Show("Fetch complete", $"{tile.Name}", NotificationType.Success, Models.NotificationCategory.SyncOperations);
        }
        catch (Exception ex)
        {
            Log.Error("Workspace", $"FetchTile {tile.RepositoryPath} threw", ex);
            _notificationService.Show("Fetch failed", $"{tile.Name}: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> looks like an uninitialized
    /// submodule checkout — the directory exists (or doesn't yet) but
    /// has no <c>.git</c> file/dir, so git would refuse to operate on
    /// it. Used by <see cref="LoadTileAsync"/> to swap the tile body
    /// to an Initialize CTA instead of letting the normal load path
    /// throw.
    /// </summary>
    internal static bool IsSubmoduleUninitialized(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!Directory.Exists(path)) return true;
        var dotGit = Path.Combine(path, ".git");
        // .git can be a directory (standalone) or a file (linked / submodule pointing into parent's modules store).
        return !Directory.Exists(dotGit) && !File.Exists(dotGit);
    }

    /// <summary>
    /// Run <c>git submodule update --init -- &lt;relativePath&gt;</c>
    /// from the parent repo, then reload the tile. Fired from the
    /// per-tile Initialize CTA when <see cref="SubmoduleTileViewModel.IsUninitialized"/>
    /// is true.
    /// </summary>
    internal async Task InitializeSubmoduleTileAsync(SubmoduleTileViewModel tile)
    {
        if (Parent is null || tile.IsParent) return;
        if (tile.IsInitializing) return;
        var rel = ToRelativePath(Parent.Path, tile.RepositoryPath);
        if (string.IsNullOrEmpty(rel)) return;

        tile.IsInitializing = true;
        try
        {
            await _gitService.InitAndUpdateSubmodulesAsync(Parent.Path, new[] { rel }, recursive: false, tile.Token);
            await LoadTileAsync(tile);
            _notificationService.Show(
                "Submodule initialized",
                $"{tile.Name} is ready.",
                NotificationType.Success,
                Models.NotificationCategory.SyncOperations);
        }
        catch (Exception ex)
        {
            Log.Error("Workspace", $"InitializeSubmodule {tile.RepositoryPath} threw", ex);
            _notificationService.Show("Initialize failed", $"{tile.Name}: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            tile.IsInitializing = false;
        }
    }

    // ─── Workspace-level bulk commands ──────────────────────────────────

    /// <summary>
    /// True while any of the workspace bulk commands is in flight. Binds
    /// to a progress strip above the grid so the user sees something
    /// happening — N parallel git operations can run for tens of
    /// seconds on a big monorepo.
    /// </summary>
    [ObservableProperty]
    private bool _isBulkOperationActive;

    /// <summary>One-line description of the currently-running bulk op, shown in the progress strip.</summary>
    [ObservableProperty]
    private string _bulkOperationStatus = string.Empty;

    /// <summary>
    /// True while any tile is in <see cref="Models.TileMode.Composing"/>.
    /// The action-bar Commit-all / Cancel-review buttons watch this to
    /// know whether they're in "enter review" or "commit reviewed"
    /// mode, and the parent tile's <c>CommitComposeCommand</c> uses it
    /// to disable itself while submodule tiles are still pending.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnySubmoduleComposing))]
    private bool _isReviewing;

    /// <summary>
    /// True when at least one SUBMODULE tile is composing. Drives the
    /// parent tile's Commit-button gating: the parent commit records
    /// the submodules' SHAs, so committing it while submodules are
    /// still in compose would record stale pointers.
    /// </summary>
    public bool IsAnySubmoduleComposing =>
        Tiles.Any(t => !t.IsParent && t.Mode == Models.TileMode.Composing);

    /// <summary>
    /// Refresh both <see cref="IsReviewing"/> and
    /// <see cref="IsAnySubmoduleComposing"/> from the live tile state,
    /// then notify the parent tile's Commit button so it re-evaluates.
    /// Called whenever a tile transitions Compose↔Normal — kicks the
    /// derived gates without WPF needing to wire per-tile mode change
    /// notifications.
    /// </summary>
    internal void NotifyComposeStateChanged()
    {
        IsReviewing = Tiles.Any(t => t.Mode == Models.TileMode.Composing);
        OnPropertyChanged(nameof(IsAnySubmoduleComposing));
        // Re-evaluate every tile's Commit button — for the parent tile
        // the gate flips when the last submodule leaves compose.
        foreach (var tile in Tiles)
        {
            tile.RefreshCanCommit();
        }
    }

    /// <summary>
    /// Tile-iteration order for write operations: submodules first,
    /// parent last. Critical for Commit-all (submodule SHAs must exist
    /// before the parent commits its new pointers) and Push-all (same
    /// reason — pushing the parent first dangles its submodule
    /// references).
    /// </summary>
    private IEnumerable<SubmoduleTileViewModel> WriteOrder() =>
        Tiles.Where(t => !t.IsParent).Concat(Tiles.Where(t => t.IsParent));

    /// <summary>
    /// Commit-all entry point. Default behaviour kicks off "review
    /// mode": each dirty tile transitions to
    /// <see cref="Models.TileMode.Composing"/> with an AI-generated
    /// commit message draft (generated in parallel), and the user
    /// reviews / edits / commits per-tile in their own time.
    /// </summary>
    /// <remarks>
    /// <para>When <see cref="AppSettings.WorkspaceCommitSkipReview"/> is
    /// true, fall back to the prior one-click path: AI in parallel +
    /// immediate commit, no compose state. Power-user opt-in for users
    /// who trust the AI output and never want to review.</para>
    ///
    /// <para>Commits happen per-tile via the inline Commit button;
    /// dependency order (submodules first, parent last) is enforced by
    /// the parent tile's <c>CommitComposeCommand</c> gating itself off
    /// while any submodule is still in compose.</para>
    /// </remarks>
    [RelayCommand]
    public async Task CommitAllAsync()
    {
        var skipReview = _settingsService.LoadSettings().WorkspaceCommitSkipReview;
        if (skipReview)
        {
            await CommitAllImmediateAsync();
            return;
        }
        await EnterReviewModeAsync();
    }

    /// <summary>
    /// Existing one-shot commit path (used by the skip-review setting).
    /// Parallel AI + commit in dep order; no compose state.
    /// </summary>
    private Task CommitAllImmediateAsync()
    {
        return RunBulkAsync("Committing all repos…", async () =>
        {
            var submodules = Tiles.Where(t => !t.IsParent).ToList();
            var parent = Tiles.FirstOrDefault(t => t.IsParent);

            if (submodules.Count > 0)
            {
                BulkOperationStatus = $"Committing {submodules.Count} submodule(s) in parallel…";
                await RunTilesThrottledAsync(submodules, CommitTileAsync);
            }

            if (parent != null)
            {
                BulkOperationStatus = $"Committing {parent.Name}…";
                await CommitTileAsync(parent);
            }

            BulkOperationStatus = "Refreshing tiles…";
            foreach (var tile in Tiles)
            {
                await LoadTileAsync(tile);
            }
        });
    }

    /// <summary>
    /// Move every dirty tile into <see cref="Models.TileMode.Composing"/>,
    /// stage its working tree (so the AI sees what's about to be
    /// committed), then fan out AI message generation in parallel. Each
    /// tile's composer surfaces as its generation completes — the user
    /// can edit any tile while others are still generating.
    /// </summary>
    private async Task EnterReviewModeAsync()
    {
        // Identify the dirty tiles up front. Clean tiles stay in
        // Normal mode and aren't part of review.
        var dirtyTiles = new List<SubmoduleTileViewModel>();
        foreach (var tile in Tiles)
        {
            try
            {
                var changes = await _gitService.GetWorkingChangesAsync(tile.RepositoryPath, tile.Token);
                if (changes.HasChanges)
                {
                    dirtyTiles.Add(tile);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Workspace", $"EnterReview: GetWorkingChangesAsync failed for {tile.Name}: {ex.Message}");
            }
        }

        if (dirtyTiles.Count == 0)
        {
            _notificationService.Show("Nothing to commit", "No dirty repos in the workspace.",
                NotificationType.Information, Models.NotificationCategory.MergeAndRebase);
            return;
        }

        // Transition every dirty tile into Composing BEFORE generation
        // starts, so the user sees the placeholder/spinner immediately
        // and the action-bar buttons flip to review mode.
        foreach (var tile in dirtyTiles)
        {
            tile.Mode = Models.TileMode.Composing;
            tile.ComposingMessage = string.Empty;
            tile.ComposingDescription = string.Empty;
            tile.AiError = string.Empty;
            tile.IsGeneratingAi = true;
        }
        NotifyComposeStateChanged();

        // Fan AI generation out in parallel. Each tile updates its own
        // ComposingMessage / AiError independently when its generation
        // completes; the user can start editing tile #1 while tile #5
        // is still on the network.
        await RunTilesThrottledAsync(dirtyTiles, GenerateAiMessageForTileAsync);
    }

    /// <summary>
    /// Generate an AI commit message for <paramref name="tile"/> and
    /// populate its composer fields. Idempotent — fires the same
    /// pipeline the single-repo working-changes pane uses, so PATH /
    /// .cmd-wrapper / provider handling is identical.
    /// </summary>
    internal async Task GenerateAiMessageForTileAsync(SubmoduleTileViewModel tile)
    {
        tile.IsGeneratingAi = true;
        tile.AiError = string.Empty;
        try
        {
            // Stage all so the AI sees the same diff that the eventual
            // commit will record. Staging is idempotent on already-
            // staged files.
            await _gitService.StageAllAsync(tile.RepositoryPath, tile.Token);
            var diff = await _gitService.GetStagedSummaryAsync(tile.RepositoryPath, cancellationToken: tile.Token);
            var (msg, desc, err) = await _aiCommitService.GenerateCommitMessageAsync(diff, tile.RepositoryPath, tile.Token);
            if (!string.IsNullOrEmpty(err) || string.IsNullOrWhiteSpace(msg))
            {
                tile.AiError = err ?? "AI returned an empty message.";
                tile.ComposingMessage = string.Empty;
                tile.ComposingDescription = string.Empty;
                tile.AiOriginalMessage = string.Empty;
                tile.AiOriginalDescription = string.Empty;
            }
            else
            {
                tile.ComposingMessage = msg!;
                tile.ComposingDescription = desc ?? string.Empty;
                tile.AiOriginalMessage = msg!;
                tile.AiOriginalDescription = desc ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Workspace", $"GenerateAiMessage for {tile.Name} threw", ex);
            tile.AiError = ex.Message;
        }
        finally
        {
            tile.IsGeneratingAi = false;
        }
    }

    /// <summary>
    /// Commit a tile that's currently in compose mode using its
    /// (possibly edited) ComposingMessage / ComposingDescription, then
    /// return it to Normal. Staging already happened during
    /// <see cref="GenerateAiMessageForTileAsync"/>; we don't re-stage
    /// here so the user's intermediate edits in the working tree don't
    /// silently get pulled in.
    /// </summary>
    internal async Task CommitComposingTileAsync(SubmoduleTileViewModel tile)
    {
        if (tile.Mode != Models.TileMode.Composing) return;
        if (string.IsNullOrWhiteSpace(tile.ComposingMessage)) return;

        try
        {
            // Parent-only: stage every submodule path so the new SHAs
            // produced by the submodule commits during this review batch
            // land in the parent's index. Without this, the parent's
            // index reflects only what was staged at review-start (its
            // own working-tree edits, when submodule HEADs hadn't moved
            // yet) and the pointer bumps would be left unstaged after
            // the commit. Submodule paths only — we don't sweep in
            // arbitrary working-tree edits the user may have made
            // between AI gen and Approve All (the same rule the
            // single-tile commit comment below cites).
            if (tile.IsParent && Parent is not null)
            {
                foreach (var sub in Tiles.Where(t => !t.IsParent))
                {
                    var rel = ToRelativePath(Parent.Path, sub.RepositoryPath);
                    if (string.IsNullOrEmpty(rel)) continue;
                    await _gitService.StageFileAsync(tile.RepositoryPath, rel, tile.Token);
                }
            }

            await _gitService.CommitAsync(
                tile.RepositoryPath,
                tile.ComposingMessage,
                string.IsNullOrWhiteSpace(tile.ComposingDescription) ? null : tile.ComposingDescription,
                cancellationToken: tile.Token);

            _notificationService.Show(
                "Commit complete",
                $"{tile.Name}: {tile.ComposingMessage}",
                NotificationType.Success,
                Models.NotificationCategory.MergeAndRebase);

            tile.ComposingMessage = string.Empty;
            tile.ComposingDescription = string.Empty;
            tile.AiError = string.Empty;
            tile.Mode = Models.TileMode.Normal;
            NotifyComposeStateChanged();

            await LoadTileAsync(tile);
        }
        catch (Exception ex)
        {
            Log.Error("Workspace", $"CommitComposingTile {tile.RepositoryPath} threw", ex);
            tile.AiError = ex.Message;
            _notificationService.Show("Commit failed", $"{tile.Name}: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Commit every tile that's still in compose mode, in dependency
    /// order (submodules first, parent last). The per-tile commit
    /// buttons let the user commit one-by-one; this command is the
    /// one-click "I've reviewed, ship them all" path.
    /// </summary>
    [RelayCommand]
    public async Task CommitAllReviewedAsync()
    {
        if (!IsReviewing) return;

        // Submodules first. After they commit, the parent's working
        // tree will have new submodule pointer paths that weren't
        // visible when the parent's AI message was generated at
        // review-start.
        foreach (var tile in Tiles.Where(t => !t.IsParent).ToList())
        {
            if (tile.Mode != Models.TileMode.Composing) continue;
            if (string.IsNullOrWhiteSpace(tile.ComposingMessage)) continue;
            await CommitComposingTileAsync(tile);
        }

        // Parent last. If the user hasn't hand-edited the parent's
        // composer text, regenerate it now so the message reflects
        // the now-complete diff (own changes + submodule pointer
        // bumps). If they did edit, respect the edit — the
        // submodule pointer paths are still staged inside
        // CommitComposingTileAsync, so the commit content is correct
        // regardless.
        var parent = Tiles.FirstOrDefault(t => t.IsParent && t.Mode == Models.TileMode.Composing);
        if (parent is not null)
        {
            var unedited =
                string.Equals(parent.ComposingMessage, parent.AiOriginalMessage, StringComparison.Ordinal) &&
                string.Equals(parent.ComposingDescription, parent.AiOriginalDescription, StringComparison.Ordinal);
            if (unedited)
            {
                await GenerateAiMessageForTileAsync(parent);
            }

            if (!string.IsNullOrWhiteSpace(parent.ComposingMessage))
            {
                await CommitComposingTileAsync(parent);
            }
        }
    }

    /// <summary>
    /// Discard review mode entirely. Returns every composing tile to
    /// Normal without committing anything. The user's working-tree
    /// changes stay on disk (we only staged + generated messages, no
    /// commit happened) so nothing is lost.
    /// </summary>
    [RelayCommand]
    public void CancelReview()
    {
        foreach (var tile in Tiles)
        {
            if (tile.Mode == Models.TileMode.Composing)
            {
                tile.CancelCompose();
            }
        }
        NotifyComposeStateChanged();
    }

    /// <summary>
    /// Push every repo. Submodules first, parent last. If any
    /// submodule push fails, the parent push is skipped — its
    /// submodule pointers would dangle on the remote otherwise.
    /// </summary>
    [RelayCommand]
    public async Task PushAllAsync()
    {
        await RunBulkAsync("Pushing all repos…", async () =>
        {
            var subFailures = 0;
            foreach (var tile in Tiles.Where(t => !t.IsParent))
            {
                BulkOperationStatus = $"Pushing {tile.Name}…";
                try
                {
                    await PushTileAsync(tile);
                }
                catch
                {
                    subFailures++;
                }
            }

            var parent = Tiles.FirstOrDefault(t => t.IsParent);
            if (parent != null)
            {
                if (subFailures > 0)
                {
                    _notificationService.Show(
                        "Parent push skipped",
                        $"{subFailures} submodule push(es) failed — pushing the parent would dangle its submodule references.",
                        NotificationType.Warning,
                        Models.NotificationCategory.SyncOperations);
                }
                else
                {
                    BulkOperationStatus = $"Pushing {parent.Name}…";
                    await PushTileAsync(parent);
                }
            }
        });
    }

    /// <summary>
    /// Pull every repo. Pulls run in parallel — conflicts in one repo
    /// don't block the rest, they just leave that repo in a paused
    /// state for the user to resolve via the merge editor (existing
    /// pause-and-route path).
    /// </summary>
    [RelayCommand]
    public async Task PullAllAsync()
    {
        await RunBulkAsync("Pulling all repos…", async () =>
        {
            BulkOperationStatus = $"Pulling {Tiles.Count} repo(s)…";
            await RunTilesThrottledAsync(Tiles, PullTileAsync);
        });
    }

    /// <summary>Fetch every repo, in parallel.</summary>
    [RelayCommand]
    public async Task FetchAllAsync()
    {
        await RunBulkAsync("Fetching all repos…", async () =>
        {
            BulkOperationStatus = $"Fetching {Tiles.Count} repo(s)…";
            await RunTilesThrottledAsync(Tiles, FetchTileAsync);
        });
    }

    /// <summary>
    /// Cap on concurrent per-tile git operations during bulk commands.
    /// Each tile op spins up a process (git CLI) or a LibGit2Sharp call;
    /// without a cap, a 20-submodule monorepo would fork 20 gits at
    /// once and hammer disk + creds. Four is a pragmatic default —
    /// matches typical fetch/clone parallelism in other tools.
    /// </summary>
    private const int MaxParallelTileOps = 4;

    /// <summary>
    /// Run <paramref name="op"/> over every tile in <paramref name="tiles"/>
    /// with a parallelism cap. Preserves the "fire all in parallel"
    /// shape of the caller while preventing the unbounded process
    /// fan-out that <c>Task.WhenAll(Select(op))</c> would produce.
    /// </summary>
    internal static async Task RunTilesThrottledAsync(
        IEnumerable<SubmoduleTileViewModel> tiles,
        Func<SubmoduleTileViewModel, Task> op,
        int maxParallel = MaxParallelTileOps)
    {
        using var gate = new SemaphoreSlim(maxParallel, maxParallel);
        var tasks = tiles.Select(async tile =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { await op(tile).ConfigureAwait(false); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared bulk-op wrapper: flips the IsBulkOperationActive flag so
    /// the progress strip shows, sets a status string, and clears both
    /// on completion (success or failure).
    /// </summary>
    private async Task RunBulkAsync(string initialStatus, Func<Task> body)
    {
        if (IsBulkOperationActive) return;
        IsBulkOperationActive = true;
        BulkOperationStatus = initialStatus;
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            Log.Error("Workspace", $"Bulk op failed: {ex.Message}", ex);
            _notificationService.Show("Workspace operation failed", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsBulkOperationActive = false;
            BulkOperationStatus = string.Empty;
        }
    }

    /// <summary>
    /// Open the Switch-workspace-to-branch dialog, then checkout the
    /// chosen branch on every repo that has it. Repos without the
    /// branch are skipped + counted; the summary toast reports the
    /// outcome ("Switched 4 of 6 repos to feature/x; 2 don't have that
    /// branch"). Repos with a dirty working tree fail-loud per repo
    /// the same way single-repo checkout fails, so the user can't
    /// accidentally trash uncommitted work.
    /// </summary>
    [RelayCommand]
    public async Task SwitchWorkspaceBranchAsync()
    {
        var dialogVm = new WorkspaceSwitchBranchDialogViewModel();
        var dialog = new Views.WorkspaceSwitchBranchDialog { DataContext = dialogVm };
        if (!await _dialogService.ShowDialogAsync(dialog)) return;

        var branchName = dialogVm.BranchName.Trim();
        // The dialog's "Switch" button is gated on CanSwitch (non-empty
        // + git's branch-name rules); this guard is paranoia in case
        // the binding hadn't flushed yet.
        if (string.IsNullOrEmpty(branchName)) return;
        var createIfMissing = dialogVm.CreateIfMissing;
        var stashChanges = dialogVm.StashChanges;

        await RunBulkAsync($"Switching workspace to {branchName}…", async () =>
        {
            var switched = new List<string>();
            var created = new List<string>();
            var stashed = new List<string>();
            var skipped = new List<string>();
            var failed = new List<(string Name, string Error)>();

            foreach (var tile in Tiles)
            {
                BulkOperationStatus = $"Checking {tile.Name}…";

                var hasBranch = await TryRevParseAsync(tile.RepositoryPath, branchName, tile.Token);

                if (!hasBranch && !createIfMissing)
                {
                    // No branch and the user didn't opt into creating —
                    // skip and report. This is the safer default.
                    skipped.Add(tile.Name);
                    continue;
                }

                try
                {
                    // Stash first if requested AND the repo is dirty.
                    // Clean repos don't get a no-op stash that produces
                    // a confusing "(no changes to stash)" warning.
                    if (stashChanges)
                    {
                        var changes = await _gitService.GetWorkingChangesAsync(tile.RepositoryPath, tile.Token);
                        if (changes.HasChanges)
                        {
                            await _gitService.StashAsync(
                                tile.RepositoryPath,
                                $"leaf: workspace-switch-to-{branchName}",
                                tile.Token);
                            stashed.Add(tile.Name);
                        }
                    }

                    if (hasBranch)
                    {
                        await _gitService.CheckoutAsync(tile.RepositoryPath, branchName, cancellationToken: tile.Token);
                    }
                    else
                    {
                        // CreateIfMissing path — create the branch at
                        // the repo's current HEAD and check it out in
                        // one shot. CreateBranchAsync's checkout=true
                        // matches what `git switch -c <branch>` does.
                        await _gitService.CreateBranchAsync(
                            tile.RepositoryPath,
                            branchName,
                            checkout: true,
                            cancellationToken: tile.Token);
                        created.Add(tile.Name);
                    }

                    await LoadTileAsync(tile);
                    switched.Add(tile.Name);
                }
                catch (Exception ex)
                {
                    Log.Warn("Workspace", $"SwitchBranch {tile.Name} → {branchName} failed: {ex.Message}");
                    failed.Add((tile.Name, ex.Message));
                }
            }

            BuildSwitchSummary(branchName, switched, created, stashed, skipped, failed);
        });
    }

    private async Task<bool> TryRevParseAsync(string repoPath, string branchName, CancellationToken cancellationToken)
    {
        // Branch existence probe — enumerates the repo's branch list
        // and matches on name. Both local and remote-only branches
        // count; "feature/x" should match when only "origin/feature/x"
        // exists too, because git checkout will create the local
        // tracking branch in that case.
        try
        {
            var branches = await _gitService.GetBranchesAsync(repoPath, cancellationToken);
            return branches.Any(b =>
                string.Equals(b.Name, branchName, StringComparison.Ordinal) ||
                (b.IsRemote && b.RemoteName != null &&
                 string.Equals($"{b.RemoteName}/{b.Name}", branchName, StringComparison.Ordinal)) ||
                string.Equals(b.Name, branchName.Split('/').LastOrDefault() ?? branchName, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private void BuildSwitchSummary(
        string branch,
        List<string> switched,
        List<string> created,
        List<string> stashed,
        List<string> skipped,
        List<(string, string)> failed)
    {
        var lines = new List<string>();
        if (switched.Count > 0)
            lines.Add($"Switched {switched.Count} repo(s) to '{branch}'.");
        if (created.Count > 0)
            lines.Add($"Created the branch on {created.Count}: {string.Join(", ", created)}.");
        if (stashed.Count > 0)
            lines.Add($"Stashed dirty changes on {stashed.Count}: {string.Join(", ", stashed)}. Pop manually to recover.");
        if (skipped.Count > 0)
            lines.Add($"{skipped.Count} don't have '{branch}': {string.Join(", ", skipped)}.");
        if (failed.Count > 0)
            lines.Add($"{failed.Count} failed: {string.Join("; ", failed.Select(f => $"{f.Item1} — {f.Item2}"))}.");

        var description = string.Join("\n", lines);
        if (failed.Count > 0)
            _notificationService.Show("Workspace switch finished with errors", description, NotificationType.Warning);
        else
            _notificationService.Show("Workspace switch complete", description, NotificationType.Success, Models.NotificationCategory.BranchCheckout);
    }

    /// <summary>
    /// Snapshot of a workspace merge that paused on a conflict.
    /// Carries the merge parameters + the tile we stopped on so
    /// <see cref="ContinueMergeAsync"/> can pick up without
    /// re-asking the user for branch + merge type.
    /// </summary>
    public sealed record PausedMergeState(string Target, MergeType MergeType, string PausedAtTilePath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPausedMerge))]
    [NotifyCanExecuteChangedFor(nameof(ContinueMergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelPausedMergeCommand))]
    private PausedMergeState? _pausedMerge;

    /// <summary>
    /// True when the previous <see cref="MergeWorkspaceAsync"/> paused
    /// on a conflict. Drives the visibility of the Continue / Cancel
    /// merge buttons in the workspace action bar.
    /// </summary>
    public bool HasPausedMerge => PausedMerge is not null;

    /// <summary>
    /// Open the workspace merge dialog, then merge every repo's
    /// currently-checked-out branch into the chosen target. Submodules
    /// merge first (so their tips advance before the parent records
    /// new submodule SHAs), then the parent. A conflict in any repo
    /// pauses the workflow at that repo and stores the resume state
    /// so the user can hit "Continue merge" once they've resolved.
    /// </summary>
    [RelayCommand]
    public async Task MergeWorkspaceAsync()
    {
        var dialogVm = new WorkspaceMergeDialogViewModel();
        var dialog = new Views.WorkspaceMergeDialog { DataContext = dialogVm };
        if (!await _dialogService.ShowDialogAsync(dialog)) return;

        var target = dialogVm.TargetBranch.Trim();
        if (string.IsNullOrEmpty(target)) return;
        var mergeType = dialogVm.MergeType;

        // Fresh run — clear any prior pause state.
        PausedMerge = null;
        await RunMergeLoopAsync(target, mergeType, resumeFromTilePath: null);
    }

    /// <summary>
    /// Resume a workspace merge that paused on a conflict. Re-enters
    /// the loop at the paused tile (so if the user committed the
    /// merge resolution, it's already done and the loop moves past
    /// it; if they only resolved partially, the merge attempt
    /// recurs and the pause survives).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanContinueMerge))]
    public async Task ContinueMergeAsync()
    {
        if (PausedMerge is null) return;
        var snapshot = PausedMerge;
        await RunMergeLoopAsync(snapshot.Target, snapshot.MergeType, resumeFromTilePath: snapshot.PausedAtTilePath);
    }

    private bool CanContinueMerge() => PausedMerge is not null;

    /// <summary>
    /// Discard the paused-merge state without resuming. Used when
    /// the user decides not to finish the workspace-wide merge —
    /// the individual repo-level merge state (MERGE_HEAD on the
    /// conflicted repo) is unaffected; only the workspace's
    /// "continue across all repos" intent is dropped.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanContinueMerge))]
    public void CancelPausedMerge()
    {
        PausedMerge = null;
    }

    private async Task RunMergeLoopAsync(string target, MergeType mergeType, string? resumeFromTilePath)
    {
        await RunBulkAsync($"Merging workspace into {target}…", async () =>
        {
            var tiles = WriteOrder().ToList();
            var startIndex = 0;
            if (resumeFromTilePath is not null)
            {
                var idx = tiles.FindIndex(t =>
                    string.Equals(t.RepositoryPath, resumeFromTilePath, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) startIndex = idx;
            }

            for (var i = startIndex; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                BulkOperationStatus = $"Merging {tile.Name} into {target}…";
                MergeResult result;
                try
                {
                    result = mergeType switch
                    {
                        MergeType.Squash => await _gitService.SquashMergeAsync(tile.RepositoryPath, target, cancellationToken: tile.Token),
                        MergeType.FastForwardOnly => await _gitService.FastForwardAsync(tile.RepositoryPath, target, cancellationToken: tile.Token),
                        _ => await _gitService.MergeBranchAsync(tile.RepositoryPath, target, cancellationToken: tile.Token),
                    };
                }
                catch (Exception ex)
                {
                    _notificationService.Show(
                        "Workspace merge failed",
                        $"{tile.Name}: {ex.Message}",
                        NotificationType.Error);
                    return;
                }

                await LoadTileAsync(tile);

                if (result.HasConflicts)
                {
                    PausedMerge = new PausedMergeState(target, mergeType, tile.RepositoryPath);
                    _notificationService.Show(
                        "Workspace merge paused",
                        $"Conflicts in {tile.Name}. Resolve them, then hit Continue merge to finish the remaining repos.",
                        NotificationType.Warning,
                        Models.NotificationCategory.MergeAndRebase);
                    return;
                }

                if (!result.Success)
                {
                    PausedMerge = new PausedMergeState(target, mergeType, tile.RepositoryPath);
                    _notificationService.Show(
                        "Workspace merge halted",
                        $"{tile.Name}: {result.ErrorMessage ?? "unknown failure"}.",
                        NotificationType.Error);
                    return;
                }
            }

            PausedMerge = null;
            _notificationService.Show(
                "Workspace merge complete",
                $"All repos merged into {target}.",
                NotificationType.Success,
                Models.NotificationCategory.MergeAndRebase);
        });
    }

    /// <summary>
    /// Reorder the live <see cref="Tiles"/> collection so the parent
    /// stays at position 0, pinned tiles follow in user order, and the
    /// remaining tiles fall into alphabetical order below. Mutates the
    /// ObservableCollection in place so WPF re-arranges the panel
    /// without rebuilding any tile views.
    /// </summary>
    private void ReorderTiles(IReadOnlyList<string> pinnedOrder)
    {
        if (Parent is null || Tiles.Count <= 1) return;

        var snapshot = Tiles.ToList();
        var parentTile = snapshot.FirstOrDefault(t => t.IsParent);
        if (parentTile is null) return;

        var others = snapshot.Where(t => !t.IsParent).ToList();

        var pinned = new List<SubmoduleTileViewModel>();
        foreach (var rel in pinnedOrder)
        {
            var absolute = Path.GetFullPath(Path.Combine(Parent.Path, rel));
            var match = others.FirstOrDefault(t => string.Equals(t.RepositoryPath, absolute, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                match.IsPinned = true;
                pinned.Add(match);
            }
        }
        var unpinned = others
            .Where(t => !pinned.Contains(t))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var t in unpinned) t.IsPinned = false;

        Tiles.Clear();
        Tiles.Add(parentTile);
        foreach (var t in pinned) Tiles.Add(t);
        foreach (var t in unpinned) Tiles.Add(t);
    }

    /// <summary>
    /// Compute <paramref name="full"/>'s path relative to <paramref name="root"/>,
    /// expressed with forward slashes so it round-trips identically to
    /// the <c>.gitmodules</c> path field. Returns an empty string when
    /// the paths can't be related (different volumes, etc.).
    /// </summary>
    private static string ToRelativePath(string root, string full)
    {
        try
        {
            var rel = Path.GetRelativePath(root, full);
            return rel.Replace('\\', '/');
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        DisposeTiles();
    }

    private void DisposeTiles()
    {
        // Snapshot to avoid mutating while disposing — the
        // ObservableCollection's clear fires CollectionChanged which
        // may walk the existing items.
        var snapshot = Tiles.ToArray();
        Tiles.Clear();
        foreach (var tile in snapshot)
        {
            tile.Dispose();
        }

        // Reset review state — without this, exiting grid mode while
        // tiles were composing (e.g. via the per-tile zoom-in) left
        // IsReviewing=true on the workspace, so the action-bar "Commit
        // reviewed" / "Cancel review" buttons stayed visible even
        // though no tiles existed to commit.
        if (IsReviewing)
        {
            IsReviewing = false;
            OnPropertyChanged(nameof(IsAnySubmoduleComposing));
        }
    }
}
