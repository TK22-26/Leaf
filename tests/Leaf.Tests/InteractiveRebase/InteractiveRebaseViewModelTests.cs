using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.ViewModels;
using LibGit2Sharp;
using Xunit;

namespace Leaf.Tests.InteractiveRebase;

/// <summary>
/// View-model logic for the interactive-rebase editor. The fake service
/// returns a deterministic plan and records what Start was called with,
/// so the tests focus on row-level commands (move/action/insert/remove),
/// status text derivations, and the Start / Cancel event surface.
/// </summary>
public class InteractiveRebaseViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesPlanFromService()
    {
        var service = new FakeRebaseService(
            [Item("aaa", "first"), Item("bbb", "second"), Item("ccc", "third")]);
        var sut = NewVm(service, fromSha: "aaa");

        await sut.LoadAsync();

        sut.Plan.Should().HaveCount(3);
        sut.Plan.Select(p => p.ShortSha).Should().Equal("aaa", "bbb", "ccc");
        sut.Plan.All(p => p.Action == RebaseTodoAction.Pick).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_ServiceThrows_SetsErrorMessage()
    {
        var service = new FakeRebaseService(loadException: new InvalidOperationException("nope"));
        var sut = NewVm(service);

        await sut.LoadAsync();

        sut.ErrorMessage.Should().Be("nope");
        sut.Plan.Should().BeEmpty();
    }

    [Fact]
    public async Task MoveItemUp_ReordersPlan()
    {
        var sut = await NewLoadedVm(Item("aaa", "a"), Item("bbb", "b"), Item("ccc", "c"));

        sut.MoveItemUpCommand.Execute(sut.Plan[2]); // c → middle

        sut.Plan.Select(p => p.ShortSha).Should().Equal("aaa", "ccc", "bbb");
    }

    [Fact]
    public async Task MoveItemUp_OnFirstItem_NoOps()
    {
        var sut = await NewLoadedVm(Item("aaa", "a"), Item("bbb", "b"));

        sut.MoveItemUpCommand.Execute(sut.Plan[0]);

        sut.Plan.Select(p => p.ShortSha).Should().Equal("aaa", "bbb");
    }

    [Fact]
    public async Task MoveItemDown_OnLastItem_NoOps()
    {
        var sut = await NewLoadedVm(Item("aaa", "a"), Item("bbb", "b"));

        sut.MoveItemDownCommand.Execute(sut.Plan[1]);

        sut.Plan.Select(p => p.ShortSha).Should().Equal("aaa", "bbb");
    }

    [Fact]
    public async Task ItemActionChange_RefreshesDerivedStatus()
    {
        // The XAML drives action change via two-way binding on the row's
        // Action property; we exercise that path here. The VM listens to
        // per-item PropertyChanged and refreshes the header / status text.
        var sut = await NewLoadedVm(Item("aaa", "a"), Item("bbb", "b"));
        var statusChanges = 0;
        sut.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(sut.StatusText)) statusChanges++; };

        sut.Plan[0].Action = RebaseTodoAction.Reword;

        sut.Plan[0].Action.Should().Be(RebaseTodoAction.Reword);
        sut.StatusText.Should().Contain("History will be rewritten");
        statusChanges.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task InsertExecAfter_AddsExecRowImmediatelyAfter()
    {
        var sut = await NewLoadedVm(Item("aaa", "a"), Item("bbb", "b"));

        sut.InsertExecAfterCommand.Execute(sut.Plan[0]);

        sut.Plan.Should().HaveCount(3);
        sut.Plan[1].Action.Should().Be(RebaseTodoAction.Exec);
        sut.Plan[1].ExecCommand.Should().NotBeNullOrWhiteSpace();
        sut.Plan[2].ShortSha.Should().Be("bbb");
    }

    [Fact]
    public async Task RemoveItem_OnlyRemovesSyntheticExecRows()
    {
        var sut = await NewLoadedVm(Item("aaa", "a"));
        sut.InsertExecAfterCommand.Execute(sut.Plan[0]);
        sut.Plan.Should().HaveCount(2);

        // Removing a commit row is a no-op — the user must Drop instead.
        sut.RemoveItemCommand.Execute(sut.Plan[0]);
        sut.Plan.Should().HaveCount(2);

        // Removing the synthetic exec row works.
        sut.RemoveItemCommand.Execute(sut.Plan[1]);
        sut.Plan.Should().HaveCount(1);
    }

    [Fact]
    public async Task StartAsync_CallsServiceWithCurrentPlanAndRaisesCompleted()
    {
        var service = new FakeRebaseService([Item("aaa", "a"), Item("bbb", "b")]);
        var sut = NewVm(service, fromSha: "aaa");
        await sut.LoadAsync();
        sut.Plan[1].Action = RebaseTodoAction.Drop;

        Leaf.Models.MergeResult? completed = null;
        sut.RebaseCompleted += (_, r) => completed = r;

        await sut.StartCommand.ExecuteAsync(null);

        service.LastStartFromSha.Should().Be("aaa");
        service.LastStartPlan.Should().NotBeNull();
        service.LastStartPlan!.Should().HaveCount(2);
        service.LastStartPlan![1].Action.Should().Be(RebaseTodoAction.Drop);
        completed.Should().NotBeNull();
        completed!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_ServiceReturnsConflicts_StillRaisesCompleted()
    {
        var conflicts = new Leaf.Models.MergeResult { Success = false, HasConflicts = true, ErrorMessage = "stopped at bbb" };
        var service = new FakeRebaseService([Item("aaa", "a"), Item("bbb", "b")], startResult: conflicts);
        var sut = NewVm(service, fromSha: "aaa");
        await sut.LoadAsync();

        Leaf.Models.MergeResult? completed = null;
        sut.RebaseCompleted += (_, r) => completed = r;

        await sut.StartCommand.ExecuteAsync(null);

        completed.Should().NotBeNull();
        completed!.HasConflicts.Should().BeTrue();
        // ErrorMessage is reserved for hard failures; conflicts are normal
        // rebase pause state and stay silent on the VM surface.
        sut.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Cancel_RaisesCancelledEvent()
    {
        var sut = NewVm(new FakeRebaseService([]));

        var raised = false;
        sut.Cancelled += (_, _) => raised = true;

        sut.CancelCommand.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public async Task IsRebasing_DisablesMutationCommands()
    {
        // Plan-mutation commands must drop CanExecute when a rebase is in
        // flight — the buttons would otherwise stay clickable while git
        // is running, and clicking Move/Insert/Remove on a stale plan
        // would diverge it from what git is processing.
        var sut = await NewLoadedVm(Item("aaa", "a"), Item("bbb", "b"));

        sut.MoveItemUpCommand.CanExecute(sut.Plan[1]).Should().BeTrue();
        sut.InsertExecAfterCommand.CanExecute(sut.Plan[0]).Should().BeTrue();

        // Use reflection / direct property write — IsRebasing is private
        // setter via the source generator; the public surface does not
        // expose a way to set it without driving Start, so we verify by
        // running Start with a service that blocks indefinitely.
        var blocking = new TaskCompletionSource<Leaf.Models.MergeResult>();
        var service = new BlockingFakeRebaseService(blocking.Task);
        var blocked = NewVm(service);
        blocked.Plan.Add(Item("aaa", "a"));
        var startTask = blocked.StartCommand.ExecuteAsync(null);

        blocked.IsRebasing.Should().BeTrue();
        blocked.MoveItemUpCommand.CanExecute(blocked.Plan[0]).Should().BeFalse();
        blocked.InsertExecAfterCommand.CanExecute(blocked.Plan[0]).Should().BeFalse();
        blocked.RemoveItemCommand.CanExecute(blocked.Plan[0]).Should().BeFalse();

        // Unblock so the test process doesn't hang.
        blocking.SetResult(new Leaf.Models.MergeResult { Success = true });
        await startTask;
    }

    [Fact]
    public async Task HeaderSummary_ReflectsNonExecCount()
    {
        var sut = await NewLoadedVm(Item("aaa", "a"), Item("bbb", "b"));

        sut.HeaderSummary.Should().Contain("2 commits from aaa");

        sut.InsertExecAfterCommand.Execute(sut.Plan[0]);
        // Exec rows are not commits — count should stay at 2.
        sut.HeaderSummary.Should().Contain("2 commits from aaa");
    }

    private static InteractiveRebaseViewModel NewVm(IInteractiveRebaseService service, string fromSha = "aaa")
    {
        return new InteractiveRebaseViewModel(service, new FakeSession(), fromSha, "subject");
    }

    private static async Task<InteractiveRebaseViewModel> NewLoadedVm(params RebaseTodoItem[] items)
    {
        var vm = NewVm(new FakeRebaseService(items));
        await vm.LoadAsync();
        return vm;
    }

    private static RebaseTodoItem Item(string sha, string subject)
    {
        return new RebaseTodoItem
        {
            Sha = sha,
            ShortSha = sha,
            Subject = subject,
            OriginalMessage = subject,
            Action = RebaseTodoAction.Pick,
        };
    }

    private sealed class BlockingFakeRebaseService : IInteractiveRebaseService
    {
        private readonly Task<Leaf.Models.MergeResult> _start;
        public BlockingFakeRebaseService(Task<Leaf.Models.MergeResult> start) => _start = start;
        public Task<IReadOnlyList<RebaseTodoItem>> LoadPlanAsync(
            IRepositorySession session, string fromCommitSha, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RebaseTodoItem>>([]);
        public Task<Leaf.Models.MergeResult> StartAsync(
            IRepositorySession session, string fromCommitSha,
            IReadOnlyList<RebaseTodoItem> plan, CancellationToken cancellationToken = default) => _start;
    }

    private sealed class FakeRebaseService : IInteractiveRebaseService
    {
        private readonly IReadOnlyList<RebaseTodoItem> _planFromLoad;
        private readonly Exception? _loadException;
        private readonly Leaf.Models.MergeResult _startResult;

        public FakeRebaseService(
            IReadOnlyList<RebaseTodoItem>? plan = null,
            Exception? loadException = null,
            Leaf.Models.MergeResult? startResult = null)
        {
            _planFromLoad = plan ?? Array.Empty<RebaseTodoItem>();
            _loadException = loadException;
            _startResult = startResult ?? new Leaf.Models.MergeResult { Success = true };
        }

        public string? LastStartFromSha { get; private set; }
        public IReadOnlyList<RebaseTodoItem>? LastStartPlan { get; private set; }

        public Task<IReadOnlyList<RebaseTodoItem>> LoadPlanAsync(
            IRepositorySession session, string fromCommitSha, CancellationToken cancellationToken = default)
        {
            if (_loadException != null) throw _loadException;
            return Task.FromResult(_planFromLoad);
        }

        public Task<Leaf.Models.MergeResult> StartAsync(
            IRepositorySession session, string fromCommitSha,
            IReadOnlyList<RebaseTodoItem> plan, CancellationToken cancellationToken = default)
        {
            LastStartFromSha = fromCommitSha;
            LastStartPlan = plan;
            return Task.FromResult(_startResult);
        }
    }

    private sealed class FakeSession : IRepositorySession
    {
        public string RepositoryPath => "C:/repo";
        public string GitDirectory => "C:/repo/.git";
        public bool IsValid => true;
        public bool IsDisposed { get; private set; }
        public bool IsBareRepository => false;
        public CancellationToken CancellationToken => CancellationToken.None;
        public long Generation => 1;

        public Task<T> RunWithRepositoryAsync<T>(Func<Repository, T> operation, CancellationToken ct = default) =>
            throw new NotSupportedException("FakeSession doesn't open a real repo.");

        public Task RunWithRepositoryAsync(Action<Repository> operation, CancellationToken ct = default) =>
            throw new NotSupportedException("FakeSession doesn't open a real repo.");

        public void Dispose() => IsDisposed = true;
    }
}
