using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial — workspace (multi-tile submodule grid) entry
/// points. Exposes <see cref="IsGridMode"/> for the layout to bind on,
/// <see cref="HasSubmodules"/> so the toggle button hides on repos with
/// no submodules, and <see cref="ToggleWorkspaceModeCommand"/> for the
/// 4-squares button next to Pop in the action bar.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// True when the host is rendering the workspace grid. Bound to
    /// column-width and visibility triggers in <c>MainWindow.xaml</c>
    /// to hide the branches panel + right detail pane and show the
    /// tiled grid in their place.
    /// </summary>
    public bool IsGridMode => Workspace.Mode == WorkspaceMode.Grid;

    /// <summary>
    /// True when the active repository has at least one submodule
    /// (detected by the presence of <c>.gitmodules</c> at its root).
    /// Used to hide the grid-mode toggle on plain repos so the action
    /// bar doesn't carry a button that does nothing.
    /// </summary>
    /// <remarks>
    /// We don't rely on the sidebar's submodule branch-category here
    /// because that category lazy-loads — the toggle has to be visible
    /// (or not) immediately on repo selection, before background loads
    /// finish. Reading the marker file is one stat call and always
    /// authoritative.
    /// </remarks>
    public bool HasSubmodules
    {
        get
        {
            var path = SelectedRepository?.Path;
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                return File.Exists(Path.Combine(path, ".gitmodules"));
            }
            catch
            {
                // Path can theoretically be invalid (deleted repo);
                // treat that as "no submodules" so the toggle just
                // stays hidden. Anything worth reporting will have
                // already surfaced through the repo-load path.
                return false;
            }
        }
    }

    /// <summary>
    /// Flip between <see cref="WorkspaceMode.Single"/> and
    /// <see cref="WorkspaceMode.Grid"/>. Re-loads the tile set when
    /// entering grid mode; tears it down when leaving so per-tile
    /// scopes release their git handles. The chosen mode is persisted
    /// to <c>.git/config</c> so the next time the user opens this repo
    /// they land in the same view.
    /// </summary>
    /// <remarks>
    /// Entering grid mode does a submodule enumeration plus N parallel
    /// repo-info loads — easily a couple of seconds on a sizable
    /// monorepo. We wrap with <see cref="BeginBusyAsync"/> so the
    /// existing action-bar loading indicator fires while it works,
    /// rather than the UI appearing to hang on click.
    /// </remarks>
    [RelayCommand]
    public async Task ToggleWorkspaceModeAsync()
    {
        if (SelectedRepository is null || GitGraphViewModel is null) return;
        if (!HasSubmodules)
        {
            // The button shouldn't even be visible in this case (see
            // HasSubmodules), but guard the command anyway — a stale
            // keyboard shortcut could fire it after the parent was
            // swapped to a non-submodule repo.
            return;
        }

        var next = Workspace.Mode == WorkspaceMode.Grid ? WorkspaceMode.Single : WorkspaceMode.Grid;

        try
        {
            await BeginBusyAsync(next == WorkspaceMode.Grid
                ? "Loading workspace…"
                : "Closing workspace…");

            if (next == WorkspaceMode.Grid)
            {
                await Workspace.LoadAsync(SelectedRepository, GitGraphViewModel, CurrentRepositoryToken);
            }
            else
            {
                Workspace.Dispose();
            }

            Workspace.Mode = next;
            OnPropertyChanged(nameof(IsGridMode));
            await Workspace.SaveModeAsync(next, CurrentRepositoryToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Refresh the workspace-specific bindings after the active repo
    /// changes — invoked from <c>OnSelectedRepositoryChanged</c> in
    /// <c>MainViewModel.cs</c>. Updates <see cref="HasSubmodules"/> so
    /// the toggle visibility tracks the new repo, and resets the
    /// workspace back to Single so a stale tile set from the previous
    /// parent doesn't linger.
    /// </summary>
    internal void OnSelectedRepositoryChangedForWorkspace()
    {
        OnPropertyChanged(nameof(HasSubmodules));

        if (Workspace.Mode != WorkspaceMode.Single)
        {
            Workspace.Dispose();
            Workspace.Mode = WorkspaceMode.Single;
            OnPropertyChanged(nameof(IsGridMode));
        }
    }

    /// <summary>
    /// Subscribe to the workspace's "open this tile in single view"
    /// event. Called once during MainViewModel construction so the
    /// host can react to a user clicking the zoom-in icon on any tile
    /// — drops grid mode, asks the repository service to surface the
    /// tile's repo, and selects it through the existing single-view
    /// path so the rest of the app sees a normal repo switch.
    /// </summary>
    internal void WireWorkspaceEvents()
    {
        Workspace.TileOpenInSingleViewRequested += OnTileOpenInSingleView;
    }

    private async void OnTileOpenInSingleView(object? sender, SubmoduleTileViewModel tile)
    {
        try
        {
            // Drop grid mode first so the centre column reverts to the
            // normal single-view layout before we swap repos. Without
            // this, the tile would briefly render inside the grid
            // panel for the new active repo.
            if (Workspace.Mode != WorkspaceMode.Single)
            {
                Workspace.Dispose();
                Workspace.Mode = WorkspaceMode.Single;
                OnPropertyChanged(nameof(IsGridMode));
            }

            // Find or auto-register the tile's repository entry, then
            // select it via the normal flow so the sidebar tree
            // selection, branch load, etc. all happen consistently.
            var existing = _repositoryService.FindRepository(tile.RepositoryPath);
            if (existing != null)
            {
                await SelectRepositoryAsync(existing);
            }
            else
            {
                var info = await _gitService.GetRepositoryInfoFastAsync(tile.RepositoryPath, CurrentRepositoryToken);
                _repositoryService.AddRepository(info);
                await SelectRepositoryAsync(info);
            }
        }
        catch (Exception ex)
        {
            await ReportOperationFailureAsync("Open in single view", ex);
        }
    }
}
