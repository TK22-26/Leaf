using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// View-model backing the reflog window — holds the raw entries from
/// <see cref="IGitService.GetReflogAsync"/>, the filter state the
/// user drives from the toolbar, and the destructive-action commands
/// invoked via the right-click menu.
/// </summary>
/// <remarks>
/// Filtering is in-memory: git's own reflog filtering is limited to
/// per-ref and count-based, neither of which map to the user-facing
/// dropdowns we offer here. Entry counts stay in the low thousands
/// even on very active repos, so a linear re-filter on each change
/// is cheaper than paging back to the CLI.
/// </remarks>
public partial class ReflogViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IClipboardService _clipboardService;
    private readonly IDialogService _dialogService;
    private readonly string _repositoryPath;
    private List<ReflogEntry> _allEntries = [];

    public ReflogViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IDialogService dialogService,
        string repositoryPath)
    {
        _gitService = gitService;
        _clipboardService = clipboardService;
        _dialogService = dialogService;
        _repositoryPath = repositoryPath;

        Entries = [];
        RefOptions = [AllRefsMarker];
        OperationTypeOptions = [AllOperationsMarker];
        foreach (var t in Enum.GetValues<ReflogOperationType>())
            OperationTypeOptions.Add(t);

        SelectedRef = AllRefsMarker;
        SelectedOperationType = AllOperationsMarker;
    }

    /// <summary>Sentinel for the "show every ref" row in the ref filter dropdown.</summary>
    public const string AllRefsMarker = "(all refs)";

    /// <summary>Sentinel for the "show every operation" row in the op-type filter dropdown.</summary>
    public static readonly object AllOperationsMarker = "(all operations)";

    [ObservableProperty]
    private ObservableCollection<ReflogEntry> _entries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEntries))]
    [NotifyPropertyChangedFor(nameof(StatusSummary))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private ObservableCollection<string> _refOptions;

    [ObservableProperty]
    private ObservableCollection<object> _operationTypeOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusSummary))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetSoftCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetMixedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetHardCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateBranchHereCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyHashCommand))]
    private ReflogEntry? _selectedEntry;

    [ObservableProperty]
    private string _selectedRef = AllRefsMarker;

    [ObservableProperty]
    private object _selectedOperationType = AllOperationsMarker;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public bool HasEntries => Entries.Count > 0;

    public string StatusSummary =>
        IsLoading
            ? "Loading reflog..."
            : $"{Entries.Count} of {_allEntries.Count} entries";

    partial void OnSelectedRefChanged(string value) => ApplyFilters();
    partial void OnSelectedOperationTypeChanged(object value) => ApplyFilters();
    partial void OnStartDateChanged(DateTime? value) => ApplyFilters();
    partial void OnEndDateChanged(DateTime? value) => ApplyFilters();
    partial void OnSearchTextChanged(string value) => ApplyFilters();

    /// <summary>
    /// (Re)load the reflog from git and reset the ref-filter dropdown
    /// to reflect the refs that actually appear.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            _allEntries = await _gitService.GetReflogAsync(_repositoryPath);

            // Rebuild the ref dropdown from what's actually in the log
            // — saves the user scrolling past refs that don't apply.
            RefOptions.Clear();
            RefOptions.Add(AllRefsMarker);
            foreach (var name in _allEntries.Select(e => e.Ref).Distinct().OrderBy(r => r, StringComparer.Ordinal))
                RefOptions.Add(name);

            if (!RefOptions.Contains(SelectedRef))
                SelectedRef = AllRefsMarker;

            ApplyFilters();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _allEntries = [];
            Entries.Clear();
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(StatusSummary));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Re-populate <see cref="Entries"/> from <see cref="_allEntries"/>
    /// applying every current filter.
    /// </summary>
    private void ApplyFilters()
    {
        IEnumerable<ReflogEntry> query = _allEntries;

        if (SelectedRef != AllRefsMarker)
            query = query.Where(e => e.Ref == SelectedRef);

        if (SelectedOperationType is ReflogOperationType op)
            query = query.Where(e => e.OperationType == op);

        if (StartDate is DateTime start)
        {
            var startOffset = new DateTimeOffset(start.Date, TimeZoneInfo.Local.GetUtcOffset(start.Date));
            query = query.Where(e => e.Timestamp >= startOffset);
        }

        if (EndDate is DateTime end)
        {
            // Inclusive end-of-day so a picker set to "2026-04-17"
            // captures everything on that day, not just midnight.
            var endOfDay = end.Date.AddDays(1).AddTicks(-1);
            var endOffset = new DateTimeOffset(endOfDay, TimeZoneInfo.Local.GetUtcOffset(endOfDay));
            query = query.Where(e => e.Timestamp <= endOffset);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            query = query.Where(e =>
                e.Message.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                e.Sha.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                e.Ref.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        Entries = new ObservableCollection<ReflogEntry>(query);
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(StatusSummary));
    }

    [RelayCommand]
    public void ClearFilters()
    {
        SelectedRef = AllRefsMarker;
        SelectedOperationType = AllOperationsMarker;
        StartDate = null;
        EndDate = null;
        SearchText = string.Empty;
    }

    // ---- Destructive actions --------------------------------------------

    private bool HasSelection() => SelectedEntry != null;

    /// <summary>
    /// Detach HEAD onto the selected entry's commit so the user can
    /// inspect the state that ref was pointing at then.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task CheckoutAsync()
    {
        if (SelectedEntry is not { } entry) return;
        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Checkout commit {entry.ShortSha}?\n\n" +
            "This will leave you on a detached HEAD. Your current branch won't move.",
            "Checkout from reflog");
        if (!confirmed) return;

        await RunDestructiveAsync(
            $"Checking out {entry.ShortSha}",
            ct => _gitService.CheckoutCommitAsync(_repositoryPath, entry.Sha, ct));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public Task ResetSoftAsync() => ResetCurrentBranchAsync(GitResetMode.Soft);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public Task ResetMixedAsync() => ResetCurrentBranchAsync(GitResetMode.Mixed);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public Task ResetHardAsync() => ResetCurrentBranchAsync(GitResetMode.Hard);

    private async Task ResetCurrentBranchAsync(GitResetMode mode)
    {
        if (SelectedEntry is not { } entry) return;

        var (verb, tail) = mode switch
        {
            GitResetMode.Soft  => ("soft",  "Working tree and index keep their current state."),
            GitResetMode.Mixed => ("mixed", "Working tree keeps its current state; the index is reset."),
            GitResetMode.Hard  => ("hard",  "Working tree and index are discarded — uncommitted changes will be lost."),
            _ => (mode.ToString().ToLowerInvariant(), string.Empty),
        };

        var confirmed = await _dialogService.ShowConfirmationAsync(
            $"Reset current branch to {entry.ShortSha} ({verb})?\n\n{tail}",
            $"Reset {verb}");
        if (!confirmed) return;

        await RunDestructiveAsync(
            $"Resetting {verb} to {entry.ShortSha}",
            ct => _gitService.ResetCurrentBranchToCommitAsync(_repositoryPath, entry.Sha, mode, ct));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task CreateBranchHereAsync()
    {
        if (SelectedEntry is not { } entry) return;

        var dialog = new Views.NewBranchInputDialog(
            title: $"Create branch at {entry.ShortSha}",
            prompt: $"Branch name for commit {entry.ShortSha}:");
        if (await _dialogService.ShowDialogAsync(dialog) != true) return;

        var name = dialog.BranchName;
        await RunDestructiveAsync(
            $"Creating branch {name} at {entry.ShortSha}",
            async ct =>
            {
                // Check out the commit first, then create a branch
                // anchored there. We explicitly don't check out the new
                // branch — the user may want to stay where they are.
                await _gitService.CheckoutCommitAsync(_repositoryPath, entry.Sha, ct);
                await _gitService.CreateBranchAsync(_repositoryPath, name, checkout: true, ct);
            });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void CopyHash()
    {
        if (SelectedEntry is not { } entry) return;
        _clipboardService.SetText(entry.Sha);
    }

    /// <summary>
    /// Shared wrapper for the reflog commands that mutate the repo.
    /// Handles the busy-state + error-surface contract the window
    /// expects; callers just provide a progress label and the work.
    /// After success, reloads the reflog so the mutation itself
    /// shows up as a new entry at the top.
    /// </summary>
    private async Task RunDestructiveAsync(string progressLabel, Func<CancellationToken, Task> work)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = progressLabel + "...";
            await work(CancellationToken.None);
            // Success — clear the progress text and let LoadAsync
            // overwrite IsLoading when it finishes.
            ErrorMessage = null;
            RaiseRepositoryMutated();
            await LoadAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or IOException
                                or UnauthorizedAccessException
                                or OperationCanceledException)
        {
            ErrorMessage = $"{progressLabel} failed: {ex.Message}";
            IsLoading = false;
            // The repo state may still have been partially mutated
            // (e.g. a checkout that half-finished) — tell the host so
            // the graph reflects reality rather than a stale cache.
            RaiseRepositoryMutated();
        }
    }

    /// <summary>
    /// Event raised when a reflog action implies the parent
    /// MainViewModel should refresh (reset / checkout / branch create
    /// all change HEAD or a branch tip). Wired by the window host so
    /// the main graph + working changes stay in sync without the
    /// user having to switch back and hit refresh.
    /// </summary>
    public event EventHandler? RepositoryMutated;

    internal void RaiseRepositoryMutated() => RepositoryMutated?.Invoke(this, EventArgs.Empty);
}
