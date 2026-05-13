using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Models;
using Leaf.Services;
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

    /// <summary>
    /// Ordered tile collection. WPF binds the workspace grid against
    /// this; the order here drives the visual order (parent → pinned →
    /// alpha rest). Mutations happen on the UI thread inside
    /// <see cref="LoadAsync"/>.
    /// </summary>
    public ObservableCollection<SubmoduleTileViewModel> Tiles { get; } = [];

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
        IWorkspaceConfigService workspaceConfig)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));
        _paletteRegistry = paletteRegistry ?? throw new ArgumentNullException(nameof(paletteRegistry));
        _workspaceConfig = workspaceConfig ?? throw new ArgumentNullException(nameof(workspaceConfig));
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

        Parent = parent;
        DisposeTiles();

        // Parent tile first — always position 0, always top-left.
        var parentName = string.IsNullOrEmpty(parent.Name)
            ? Path.GetFileName(parent.Path) ?? parent.Path
            : parent.Name;
        Tiles.Add(SubmoduleTileViewModel.CreateParent(parent.Path, parentName, parentGraph, parent));

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
            var info = await _gitService.GetRepositoryInfoFastAsync(tile.RepositoryPath, tile.Token).ConfigureAwait(true);
            tile.Repository = info;
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
    }
}
