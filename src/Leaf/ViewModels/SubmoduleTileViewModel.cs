using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Composition;
using Leaf.Models;
using Leaf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.ViewModels;

/// <summary>
/// A single rectangle in the workspace grid. Holds the per-repo state
/// for one tile — its own DI scope (so background work cancels cleanly
/// when the tile goes away), its own <see cref="GitGraphViewModel"/>
/// instance pointed at this repo, and the metadata the title bar
/// renders (name, branch chip, dirty count, parent marker).
/// </summary>
/// <remarks>
/// <para>The parent tile gets its <see cref="Graph"/> handed in from
/// outside (the existing app-lifetime <c>MainViewModel.GitGraphViewModel</c>)
/// so there's no duplicate graph instance for the active repo — the
/// same VM that powers the single-view's graph fills the parent tile
/// when the user is in grid mode.</para>
///
/// <para>Submodule tiles instead build their own VM with delegates that
/// resolve to the tile's own scope, and own its lifetime. Disposing
/// the tile disposes the scope (cancelling outstanding git work) and
/// clears the graph reference.</para>
/// </remarks>
public partial class SubmoduleTileViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Absolute path to the repo this tile shows. Used as the stable
    /// identity key for ordering, pinning, and lookup; the tile name
    /// shown in the title bar is derived from this path's basename.
    /// </summary>
    public string RepositoryPath { get; }

    /// <summary>
    /// Display name shown in the tile's title bar. For submodules this
    /// is the submodule's <c>Name</c> from <c>.gitmodules</c>; for the
    /// parent tile it's the repository folder name.
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// True when this tile represents the parent (root) repository, not
    /// a submodule. Drives the small "ROOT" dot on the title bar so the
    /// user can find the parent at a glance in a sea of tiles.
    /// </summary>
    public bool IsParent { get; }

    /// <summary>
    /// The graph VM rendered into the tile's body. For the parent it's
    /// the app-lifetime VM owned by <c>MainViewModel</c>; for a
    /// submodule it's a tile-owned VM whose <c>GetSessionToken</c>
    /// delegate resolves to this tile's own scope token.
    /// </summary>
    [ObservableProperty]
    private GitGraphViewModel? _graph;

    /// <summary>
    /// <see cref="RepositoryInfo"/> the tile binds for branch / dirty
    /// status. Reloaded by the workspace orchestrator when the tile is
    /// asked to refresh.
    /// </summary>
    [ObservableProperty]
    private RepositoryInfo? _repository;

    /// <summary>
    /// True when this submodule is in the user's pinned tile order.
    /// Drives the pushpin icon's filled-vs-outline state in the title
    /// bar. Set by the workspace orchestrator during ordering — the
    /// tile itself doesn't decide its own pin state.
    /// </summary>
    [ObservableProperty]
    private bool _isPinned;

    /// <summary>
    /// Current tile mode. <see cref="Models.TileMode.Normal"/> shows the
    /// graph + working changes; <see cref="Models.TileMode.Composing"/>
    /// hides the graph and renders an inline commit composer pre-filled
    /// with an AI-generated message.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComposing))]
    [NotifyCanExecuteChangedFor(nameof(CommitComposeCommand))]
    private Models.TileMode _mode = Models.TileMode.Normal;

    /// <summary>Convenience flag for view-side <c>Visibility</c> bindings.</summary>
    public bool IsComposing => Mode == Models.TileMode.Composing;

    /// <summary>
    /// Editable commit message during compose. Pre-filled with the AI
    /// subject when generation succeeds; the user can edit before
    /// committing. Two-way bound to the inline
    /// <see cref="Leaf.Controls.CommitInputControl"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitComposeCommand))]
    private string _composingMessage = string.Empty;

    /// <summary>Editable commit description body during compose. Pre-filled with the AI body.</summary>
    [ObservableProperty]
    private string _composingDescription = string.Empty;

    /// <summary>True while the workspace is generating the AI message for this tile.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitComposeCommand))]
    private bool _isGeneratingAi;

    /// <summary>
    /// Last AI generation error message, surfaced inline in the
    /// composer so the user can read it without opening the log. Empty
    /// string when the generation succeeded (or hasn't run yet).
    /// </summary>
    [ObservableProperty]
    private string _aiError = string.Empty;

    /// <summary>True while the per-tile Commit command is in flight.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitComposeCommand))]
    private bool _isCommitting;

    /// <summary>
    /// Back-reference set by <see cref="WorkspaceViewModel"/> after
    /// constructing the tile. Tile-level commands (toggle pin, open in
    /// single view, refresh) delegate here so the workspace stays the
    /// single point of authority for tile-list ordering, parent
    /// selection, and cross-tile state.
    /// </summary>
    internal WorkspaceViewModel? Workspace { get; set; }

    private readonly IServiceScope? _scope;
    private bool _disposed;

    /// <summary>
    /// Toggle this tile's pinned state. No-op on the parent tile —
    /// parent always sits at position 0, pinning makes no sense.
    /// </summary>
    [RelayCommand]
    public async Task TogglePinAsync()
    {
        if (IsParent || Workspace is null) return;
        await Workspace.TogglePinAsync(this);
    }

    /// <summary>
    /// Exit grid mode and surface this tile's repo as the active repo
    /// in single view. Bound to the zoom-in icon in the title bar and
    /// to the "Open in single view" overflow item.
    /// </summary>
    [RelayCommand]
    public async Task OpenInSingleViewAsync()
    {
        if (Workspace is null) return;
        await Workspace.OpenTileInSingleViewAsync(this);
    }

    /// <summary>
    /// Re-run the per-tile load — fetches a fresh RepositoryInfo, then
    /// asks the tile's GitGraphViewModel to rebuild against it. Used
    /// from the kebab menu's Refresh item; the rest of the tile keeps
    /// rendering its current state until the new data arrives.
    /// </summary>
    [RelayCommand]
    public async Task RefreshTileAsync()
    {
        if (Workspace is null) return;
        await Workspace.RefreshTileAsync(this);
    }

    /// <summary>
    /// Stage + AI-commit this tile's working changes. Delegates to
    /// <see cref="WorkspaceViewModel.CommitTileAsync"/> which reuses
    /// the headless <c>AutoCommitService</c> so v1 has a real commit
    /// path without depending on a per-tile composer popout (the
    /// proper popout lands with the workspace bulk commit dialog).
    /// </summary>
    [RelayCommand]
    public async Task CommitAsync()
    {
        if (Workspace is null) return;
        await Workspace.CommitTileAsync(this);
    }

    [RelayCommand]
    public async Task PushAsync()
    {
        if (Workspace is null) return;
        await Workspace.PushTileAsync(this);
    }

    [RelayCommand]
    public async Task PullAsync()
    {
        if (Workspace is null) return;
        await Workspace.PullTileAsync(this);
    }

    [RelayCommand]
    public async Task FetchAsync()
    {
        if (Workspace is null) return;
        await Workspace.FetchTileAsync(this);
    }

    /// <summary>
    /// True when the per-tile Commit button can fire — non-empty
    /// message, not currently generating or committing, and (for the
    /// parent tile) no submodule is still in compose mode. The last
    /// rule prevents the user from committing the parent while
    /// submodules are still pending, which would record stale
    /// submodule pointers in the parent commit.
    /// </summary>
    public bool CanCommitCompose
    {
        get
        {
            if (IsGeneratingAi || IsCommitting) return false;
            if (string.IsNullOrWhiteSpace(ComposingMessage)) return false;
            if (IsParent && Workspace?.IsAnySubmoduleComposing == true) return false;
            return true;
        }
    }

    /// <summary>
    /// Commit this tile's working changes using the (possibly edited)
    /// composer message + description, then revert the tile to
    /// <see cref="Models.TileMode.Normal"/> so the user sees the new
    /// commit in the refreshed graph.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCommitCompose))]
    public async Task CommitComposeAsync()
    {
        if (Workspace is null) return;
        IsCommitting = true;
        try
        {
            await Workspace.CommitComposingTileAsync(this);
        }
        finally
        {
            IsCommitting = false;
        }
    }

    /// <summary>
    /// Cancel the compose state for this tile only. Discards any
    /// edits to the composer fields; the tile returns to the normal
    /// graph view. Working-tree changes stay on disk — Cancel doesn't
    /// undo anything the user wrote outside the composer.
    /// </summary>
    [RelayCommand]
    public void CancelCompose()
    {
        if (Mode != Models.TileMode.Composing) return;
        ComposingMessage = string.Empty;
        ComposingDescription = string.Empty;
        AiError = string.Empty;
        IsGeneratingAi = false;
        Mode = Models.TileMode.Normal;
        // Tell the workspace so it can refresh "any submodule still
        // composing" state and the parent's Commit button enable flag.
        Workspace?.NotifyComposeStateChanged();
    }

    /// <summary>
    /// Re-run AI message generation for this tile. Fired from the
    /// inline Retry button when the initial generation failed.
    /// </summary>
    [RelayCommand]
    public async Task RetryAiAsync()
    {
        if (Workspace is null) return;
        await Workspace.GenerateAiMessageForTileAsync(this);
    }

    /// <summary>
    /// Re-evaluate <see cref="CanCommitCompose"/>. Called by the
    /// workspace when another tile's compose state changes (which can
    /// flip the parent tile's "is any submodule still composing"
    /// gating).
    /// </summary>
    internal void RefreshCanCommit()
    {
        CommitComposeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Open the tile's repository folder in Windows File Explorer.
    /// Useful when the user wants to drag a file into the working tree
    /// or run an external tool against the checkout.
    /// </summary>
    [RelayCommand]
    public void RevealInExplorer()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{RepositoryPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Swallow + log — RevealInExplorer is a convenience verb.
            // The button doesn't owe the user a modal failure dialog
            // if explorer.exe is unavailable for some reason.
            Log.Warn("Workspace", $"RevealInExplorer failed for {RepositoryPath}: {ex.Message}");
        }
    }

    private SubmoduleTileViewModel(
        string repositoryPath,
        string name,
        bool isParent,
        IServiceScope? scope,
        GitGraphViewModel? graph)
    {
        RepositoryPath = repositoryPath ?? throw new ArgumentNullException(nameof(repositoryPath));
        Name = name;
        IsParent = isParent;
        _scope = scope;
        Graph = graph;
    }

    /// <summary>
    /// Token that cancels when this tile is disposed. Pass it to any
    /// per-tile background git call so closing grid mode (or removing
    /// a submodule) aborts in-flight work promptly. The parent tile
    /// returns <see cref="CancellationToken.None"/> here because its
    /// cancellation contract belongs to <c>MainViewModel</c>; only the
    /// host knows when the parent's repo changes.
    /// </summary>
    public CancellationToken Token =>
        _scope?.ServiceProvider.GetService<IRepositorySession>()?.CancellationToken ?? CancellationToken.None;

    /// <summary>
    /// Factory for the parent tile. Adopts the host's existing graph
    /// VM (no second instance, no second scope) and just decorates it
    /// with workspace-tile chrome.
    /// </summary>
    public static SubmoduleTileViewModel CreateParent(
        string repositoryPath,
        string displayName,
        GitGraphViewModel sharedGraph,
        RepositoryInfo? repository)
    {
        return new SubmoduleTileViewModel(repositoryPath, displayName, isParent: true, scope: null, graph: sharedGraph)
        {
            Repository = repository,
        };
    }

    /// <summary>
    /// Factory for a submodule tile. Opens its own DI scope (so the
    /// tile owns a fresh <see cref="IRepositorySession"/> with an
    /// isolated cancellation token), constructs a tile-private
    /// <see cref="GitGraphViewModel"/> wired to that scope's token,
    /// and queues the initial repo load. Failures during scope creation
    /// leave the tile with a null graph — the title bar still renders,
    /// but the body shows a "couldn't load" placeholder.
    /// </summary>
    public static SubmoduleTileViewModel CreateSubmodule(
        IServiceScopeFactory scopeFactory,
        IGitService gitService,
        SettingsService settingsService,
        IRepositoryManagementService repositoryService,
        IBranchColorPaletteRegistry paletteRegistry,
        string repositoryPath,
        string submoduleName)
    {
        if (scopeFactory == null) throw new ArgumentNullException(nameof(scopeFactory));

        var scope = scopeFactory.CreateScope();
        IRepositorySession? session = null;
        try
        {
            scope.ServiceProvider.GetRequiredService<RepositoryScopeContext>().Path = repositoryPath;
            session = scope.ServiceProvider.GetService<IRepositorySession>();
        }
        catch (Exception ex)
        {
            Log.Warn("Workspace", $"Tile scope failed for {repositoryPath}: {ex.Message}");
        }

        // GitGraphViewModel takes app-lifetime deps + two callbacks that
        // give it (a) a fresh cancellation token per request and (b) the
        // active RepositoryInfo (used by the branch-colour palette).
        // For tiles, both resolve to the tile's own state — never the
        // host's selected repo.
        var tile = new SubmoduleTileViewModel(repositoryPath, submoduleName, isParent: false, scope: scope, graph: null);

        var graph = new GitGraphViewModel(gitService, settingsService, repositoryService, paletteRegistry)
        {
            GetSessionToken = () => session?.CancellationToken ?? CancellationToken.None,
            GetActiveRepositoryInfo = () => tile.Repository,
        };
        tile.Graph = graph;
        return tile;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The parent tile shares its graph with the host; never dispose
        // its graph reference here. Submodule tiles' scopes own their
        // graph exclusively, and disposing the scope cancels its session
        // — any in-flight git calls observe the token and unwind.
        if (!IsParent)
        {
            Graph = null;
            _scope?.Dispose();
        }
    }
}
