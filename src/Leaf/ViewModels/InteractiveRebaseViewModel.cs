using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// Backs the interactive-rebase editor window. Exposes the live plan as
/// an <see cref="ObservableCollection{T}"/>, the row-level commands the
/// view binds to (action change, move up/down, insert exec, reset to
/// pick), and the terminal Start/Cancel commands that drive
/// <see cref="IInteractiveRebaseService"/>.
/// </summary>
/// <remarks>
/// The view-model is intentionally repository-aware (holds a path + a
/// session token getter) so it can be lifetime-decoupled from
/// <see cref="MainViewModel"/> — the host opens it as a modal window and
/// listens for <see cref="RebaseCompleted"/> to close + refresh. This
/// matches how <see cref="ReflogViewModel"/> + <see cref="Views.Merge.MergeEditorView"/>
/// integrate today.
/// </remarks>
public partial class InteractiveRebaseViewModel : ObservableObject
{
    private readonly IInteractiveRebaseService _rebaseService;
    private readonly IRepositorySession _session;

    public InteractiveRebaseViewModel(
        IInteractiveRebaseService rebaseService,
        IRepositorySession session,
        string fromCommitSha,
        string fromCommitSubject)
    {
        _rebaseService = rebaseService ?? throw new ArgumentNullException(nameof(rebaseService));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        FromCommitSha = fromCommitSha ?? throw new ArgumentNullException(nameof(fromCommitSha));
        FromCommitSubject = fromCommitSubject ?? string.Empty;

        Plan = [];
        Plan.CollectionChanged += OnPlanChanged;
    }

    /// <summary>Raised after Start finishes (success, conflicts, or error). Carries the result so the host can decide what to do next.</summary>
    public event EventHandler<MergeResult>? RebaseCompleted;

    /// <summary>Raised when the user clicks Cancel before starting. The host closes the window without touching the repo.</summary>
    public event EventHandler? Cancelled;

    public string FromCommitSha { get; }

    /// <summary>Subject of the user-picked commit, displayed in the header so the user can confirm the entry-point.</summary>
    public string FromCommitSubject { get; }

    /// <summary>Header summary, e.g. "Rebasing 5 commits from a3f2c91".</summary>
    public string HeaderSummary
    {
        get
        {
            var n = Plan.Count(p => p.Action != RebaseTodoAction.Exec);
            var sha = FromCommitSha.Length >= 7 ? FromCommitSha[..7] : FromCommitSha;
            return n == 1
                ? $"Rebasing 1 commit from {sha}"
                : $"Rebasing {n} commits from {sha}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveItemUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveItemDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertExecAfterCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveItemCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isRebasing;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<RebaseTodoItem> Plan { get; }

    /// <summary>Bottom-bar status text, derived from loading/rebasing flags.</summary>
    public string StatusText
    {
        get
        {
            if (IsRebasing) return "Running git rebase…";
            if (IsLoading) return "Loading commits…";
            var willRewrite = Plan.Any(p => p.WillRewriteCommit);
            var execs = Plan.Count(p => p.Action == RebaseTodoAction.Exec);
            if (!willRewrite && execs == 0) return "No changes — start would be a no-op.";
            return willRewrite
                ? "History will be rewritten — published branches will need a force push."
                : $"{execs} exec command{(execs == 1 ? "" : "s")} will run during rebase.";
        }
    }

    /// <summary>
    /// Pull the commits between <c>fromSha^..HEAD</c> through the service
    /// and seed the plan. Called once on window open; subsequent edits
    /// stay in-memory until Start.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var items = await _rebaseService.LoadPlanAsync(_session, FromCommitSha, cancellationToken);
            Plan.Clear();
            foreach (var item in items)
            {
                Plan.Add(item);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            ErrorMessage = ex.Message;
            Log.Warn("InteractiveRebase", $"LoadAsync failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HeaderSummary));
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutatePlan))]
    private void MoveItemUp(RebaseTodoItem? item)
    {
        if (item == null) return;
        var index = Plan.IndexOf(item);
        if (index <= 0) return;
        Plan.Move(index, index - 1);
    }

    [RelayCommand(CanExecute = nameof(CanMutatePlan))]
    private void MoveItemDown(RebaseTodoItem? item)
    {
        if (item == null) return;
        var index = Plan.IndexOf(item);
        if (index < 0 || index >= Plan.Count - 1) return;
        Plan.Move(index, index + 1);
    }

    [RelayCommand(CanExecute = nameof(CanMutatePlan))]
    private void InsertExecAfter(RebaseTodoItem? item)
    {
        if (item == null) return;
        var index = Plan.IndexOf(item);
        if (index < 0) return;
        Plan.Insert(index + 1, new RebaseTodoItem
        {
            Sha = string.Empty,
            ShortSha = string.Empty,
            Subject = "(exec command)",
            OriginalMessage = string.Empty,
            Action = RebaseTodoAction.Exec,
            ExecCommand = "echo replace this command",
        });
    }

    [RelayCommand(CanExecute = nameof(CanMutatePlan))]
    private void RemoveItem(RebaseTodoItem? item)
    {
        // Remove only makes sense for synthetic Exec rows the user inserted;
        // commit rows are removed via the Drop action so the rewritten
        // history is explicit.
        if (item == null || item.Action != RebaseTodoAction.Exec) return;
        if (string.IsNullOrEmpty(item.Sha)) Plan.Remove(item);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (Plan.Count == 0) return;
        IsRebasing = true;
        ErrorMessage = null;
        try
        {
            var result = await _rebaseService.StartAsync(
                _session, FromCommitSha, [.. Plan], _session.CancellationToken);

            if (!result.Success && !result.HasConflicts)
            {
                ErrorMessage = result.ErrorMessage;
            }
            RebaseCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Log.Error("InteractiveRebase", "StartAsync failed", ex);
            RebaseCompleted?.Invoke(this, new MergeResult { Success = false, ErrorMessage = ex.Message });
        }
        finally
        {
            IsRebasing = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    private bool CanMutatePlan() => !IsLoading && !IsRebasing;
    private bool CanStart() => !IsLoading && !IsRebasing && Plan.Count > 0;

    private void OnPlanChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HeaderSummary));
        OnPropertyChanged(nameof(StatusText));
        StartCommand.NotifyCanExecuteChanged();
        // Wire/unwire ItemPropertyChanged so derived header text updates
        // when the user toggles individual row actions without going
        // through SetActionCommand (e.g. ComboBox direct two-way binding).
        if (e.NewItems != null)
        {
            foreach (RebaseTodoItem item in e.NewItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (RebaseTodoItem item in e.OldItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RebaseTodoItem.Action))
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HeaderSummary));
        }
    }
}
