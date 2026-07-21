using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.RepoTree;
using Moq;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Unit tests for <see cref="RepoTreeService"/> — the ordering rules
/// (submodules before parent), pointer staging, child-failure skips,
/// and tree-root resolution that both the workspace grid and the MCP
/// server depend on.
/// </summary>
public class RepoTreeServiceTests
{
    private readonly Mock<IGitService> _git = new(MockBehavior.Loose);
    private readonly Mock<ICredentialService> _credentials = new(MockBehavior.Loose);
    private readonly List<string> _callLog = [];

    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo-tree-root"));
    private static string Sub(params string[] segments) => Path.GetFullPath(Path.Combine([Root, .. segments]));

    private RepoTreeService CreateService() => new(_git.Object, _credentials.Object);

    private void SetupValidRepo() =>
        _git.Setup(g => g.IsValidRepositoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

    private void SetupSubmodules(string repoPath, params SubmoduleInfo[] submodules) =>
        _git.Setup(g => g.GetSubmodulesAsync(repoPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. submodules]);

    private void SetupNoSubmodules() =>
        _git.Setup(g => g.GetSubmodulesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

    private static SubmoduleInfo MakeSubmodule(string relPath, bool initialized = true) => new()
    {
        Name = relPath,
        Path = relPath,
        Url = $"https://example.test/{relPath}.git",
        RecordedSha = "0000000000000000000000000000000000000000",
        WorkingSha = initialized ? "0000000000000000000000000000000000000000" : null,
        Status = initialized ? SubmoduleStatus.UpToDate : SubmoduleStatus.Uninitialized,
    };

    private static WorkingChangesInfo DirtyChanges() => new()
    {
        UnstagedFiles = [new FileStatusInfo { Path = "file.txt", Status = FileChangeStatus.Modified }],
    };

    private static WorkingChangesInfo CleanChanges() => new();

    private void SetupChanges(string repoPath, WorkingChangesInfo changes) =>
        _git.Setup(g => g.GetWorkingChangesAsync(repoPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(changes)
            .Callback(() => _callLog.Add($"changes:{repoPath}"));

    private void SetupCommitPipeline(string sha = "abc1234")
    {
        _git.Setup(g => g.StageAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<string, CancellationToken>((p, _) => _callLog.Add($"stageAll:{p}"));
        _git.Setup(g => g.StageFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<string, string, CancellationToken>((p, f, _) => _callLog.Add($"stageFile:{p}:{f}"));
        _git.Setup(g => g.CommitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<string, string, string?, bool, CancellationToken>((p, _, _, _, _) => _callLog.Add($"commit:{p}"));
        _git.Setup(g => g.GetHeadCommitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommitInfo { Sha = sha });
    }

    // ─── GetTreeAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetTreeAsync_EmitsPostOrder_DeepestFirst_RootLast()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("modules/A"), MakeSubmodule("B", initialized: false));
        SetupSubmodules(Sub("modules", "A"), MakeSubmodule("nested"));
        SetupSubmodules(Sub("modules", "A", "nested"));
        SetupSubmodules(Sub("B"));

        var tree = await CreateService().GetTreeAsync(Root);

        tree.Select(n => n.RelativePath).Should().Equal("modules/A/nested", "modules/A", "B", ".");
        tree.Select(n => n.Depth).Should().Equal(2, 1, 1, 0);
        tree[0].ParentPath.Should().Be(Sub("modules", "A"));
        tree[1].ParentPath.Should().Be(Root);
        tree[2].IsInitialized.Should().BeFalse();
        tree[3].ParentPath.Should().BeNull();
        tree[3].Path.Should().Be(Root);
    }

    [Fact]
    public async Task GetTreeAsync_UninitializedSubmodule_IsNotRecursedInto()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("B", initialized: false));

        var tree = await CreateService().GetTreeAsync(Root);

        tree.Should().HaveCount(2);
        _git.Verify(g => g.GetSubmodulesAsync(Sub("B"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTreeAsync_SubmoduleCycle_Throws()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        // A claims the root itself as its submodule — a cycle.
        SetupSubmodules(Sub("A"), MakeSubmodule(".."));

        var act = () => CreateService().GetTreeAsync(Root);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cycle*");
    }

    [Fact]
    public async Task GetTreeAsync_NestingBeyondDepthCap_Throws()
    {
        SetupValidRepo();
        // Every repo reports one more nested submodule, forever.
        _git.Setup(g => g.GetSubmodulesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeSubmodule("deeper")]);

        var act = () => CreateService().GetTreeAsync(Root);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task GetTreeAsync_NotARepository_Throws()
    {
        _git.Setup(g => g.IsValidRepositoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => CreateService().GetTreeAsync(Root);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a git repository*");
    }

    // ─── CommitTreeAsync ────────────────────────────────────────────────

    private static TreeCommitOptions MessagesFor(params (string RelPath, string? Message)[] messages) => new()
    {
        MessageProvider = (node, _) =>
        {
            var match = messages.FirstOrDefault(m => m.RelPath == node.RelativePath);
            return Task.FromResult<(string, string?)?>(
                match.Message is null ? null : (match.Message, null));
        },
    };

    [Fact]
    public async Task CommitTreeAsync_CommitsChildFirst_StagesPointer_ThenParent()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        SetupSubmodules(Sub("A"));
        SetupChanges(Sub("A"), DirtyChanges());
        SetupChanges(Root, DirtyChanges());
        SetupCommitPipeline();

        var result = await CreateService().CommitTreeAsync(Root, MessagesFor(("A", "child msg"), (".", "parent msg")));

        result.AllSucceeded.Should().BeTrue();
        result.Entries.Should().HaveCount(2);
        result.Entries[0].Should().Match<TreeOpEntry>(e =>
            e.RelativePath == "A" && e.Outcome == TreeOpOutcome.Succeeded && e.CommitSha == "abc1234");
        result.Entries[1].Outcome.Should().Be(TreeOpOutcome.Succeeded);

        // The load-bearing sequence: child commits, THEN its pointer is
        // staged in the parent, THEN the parent's dirtiness is evaluated
        // and the parent commits.
        var childCommit = _callLog.IndexOf($"commit:{Sub("A")}");
        var pointerStage = _callLog.IndexOf($"stageFile:{Root}:A");
        var parentChanges = _callLog.IndexOf($"changes:{Root}");
        var parentCommit = _callLog.IndexOf($"commit:{Root}");
        childCommit.Should().BeGreaterThanOrEqualTo(0);
        pointerStage.Should().BeGreaterThan(childCommit);
        parentChanges.Should().BeGreaterThan(pointerStage);
        parentCommit.Should().BeGreaterThan(parentChanges);
    }

    [Fact]
    public async Task CommitTreeAsync_CleanChild_SkippedClean_NoPointerStaging()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        SetupSubmodules(Sub("A"));
        SetupChanges(Sub("A"), CleanChanges());
        SetupChanges(Root, CleanChanges());
        SetupCommitPipeline();

        var result = await CreateService().CommitTreeAsync(Root, MessagesFor(("A", "m"), (".", "m")));

        result.AllSucceeded.Should().BeTrue();
        result.Entries.Should().OnlyContain(e => e.Outcome == TreeOpOutcome.SkippedClean);
        _git.Verify(g => g.StageFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _git.Verify(g => g.CommitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CommitTreeAsync_MissingMessageOnDirtyRepo_FailsLoud_AncestorsSkipped()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        SetupSubmodules(Sub("A"));
        SetupChanges(Sub("A"), DirtyChanges());
        SetupChanges(Root, DirtyChanges());
        SetupCommitPipeline();

        // No message for the dirty child; the parent has one.
        var result = await CreateService().CommitTreeAsync(Root, MessagesFor(("A", null), (".", "parent msg")));

        result.AllSucceeded.Should().BeFalse();
        result.Entries[0].Outcome.Should().Be(TreeOpOutcome.Failed);
        result.Entries[0].Detail.Should().Contain("no commit message");
        result.Entries[1].Outcome.Should().Be(TreeOpOutcome.SkippedChildFailed);
        _git.Verify(g => g.CommitAsync(Root, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CommitTreeAsync_ChildFailure_PropagatesThroughGrandparent()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        SetupSubmodules(Sub("A"), MakeSubmodule("nested"));
        SetupSubmodules(Sub("A", "nested"));
        SetupChanges(Sub("A", "nested"), DirtyChanges());
        SetupChanges(Sub("A"), DirtyChanges());
        SetupChanges(Root, DirtyChanges());
        SetupCommitPipeline();
        _git.Setup(g => g.CommitAsync(Sub("A", "nested"), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateService().CommitTreeAsync(
            Root, MessagesFor(("A/nested", "m"), ("A", "m"), (".", "m")));

        result.Entries.Select(e => e.Outcome).Should().Equal(
            TreeOpOutcome.Failed, TreeOpOutcome.SkippedChildFailed, TreeOpOutcome.SkippedChildFailed);
        result.AllSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task CommitTreeAsync_StageAllFalse_DoesNotStageWorkingTree()
    {
        SetupValidRepo();
        SetupNoSubmodules();
        SetupChanges(Root, DirtyChanges());
        SetupCommitPipeline();

        var options = new TreeCommitOptions
        {
            MessageProvider = (_, _) => Task.FromResult<(string, string?)?>(("m", null)),
            StageAll = false,
        };
        await CreateService().CommitTreeAsync(Root, options);

        _git.Verify(g => g.StageAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _git.Verify(g => g.CommitAsync(Root, "m", null, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── PushTreeAsync ──────────────────────────────────────────────────

    private void SetupRemotes(string repoPath, params RemoteInfo[] remotes) =>
        _git.Setup(g => g.GetRemotesAsync(repoPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. remotes]);

    private void SetupPush()
    {
        _git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<string, string?, string?, IProgress<string>?, CancellationToken>((p, _, _, _, _) => _callLog.Add($"push:{p}"));
    }

    private static RemoteInfo Origin() => new() { Name = "origin", Url = "https://example.test/repo.git" };

    [Fact]
    public async Task PushTreeAsync_PushesChildrenBeforeParent()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        SetupSubmodules(Sub("A"));
        SetupRemotes(Sub("A"), Origin());
        SetupRemotes(Root, Origin());
        SetupPush();

        var result = await CreateService().PushTreeAsync(Root);

        result.AllSucceeded.Should().BeTrue();
        _callLog.Should().Equal($"push:{Sub("A")}", $"push:{Root}");
    }

    [Fact]
    public async Task PushTreeAsync_NoRemote_SkipsWithoutFailing_ParentStillPushes()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        SetupSubmodules(Sub("A"));
        SetupRemotes(Sub("A"));
        SetupRemotes(Root, Origin());
        SetupPush();

        var result = await CreateService().PushTreeAsync(Root);

        result.AllSucceeded.Should().BeTrue();
        result.Entries[0].Outcome.Should().Be(TreeOpOutcome.SkippedNoRemote);
        result.Entries[1].Outcome.Should().Be(TreeOpOutcome.Succeeded);
    }

    [Fact]
    public async Task PushTreeAsync_ChildPushFails_ParentIsSkipped()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        SetupSubmodules(Sub("A"));
        SetupRemotes(Sub("A"), Origin());
        SetupRemotes(Root, Origin());
        _git.Setup(g => g.PushAsync(Sub("A"), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("rejected"));

        var result = await CreateService().PushTreeAsync(Root);

        result.AllSucceeded.Should().BeFalse();
        result.Entries[0].Outcome.Should().Be(TreeOpOutcome.Failed);
        result.Entries[0].Detail.Should().Contain("rejected");
        result.Entries[1].Outcome.Should().Be(TreeOpOutcome.SkippedChildFailed);
        _git.Verify(g => g.PushAsync(Root, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PushTreeAsync_UninitializedSubmodule_NeverPushed()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("B", initialized: false));
        SetupRemotes(Root, Origin());
        SetupPush();

        var result = await CreateService().PushTreeAsync(Root);

        result.Entries[0].Outcome.Should().Be(TreeOpOutcome.SkippedUninitialized);
        result.Entries[1].Outcome.Should().Be(TreeOpOutcome.Succeeded);
        _git.Verify(g => g.PushAsync(Sub("B"), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Fetch / Pull ───────────────────────────────────────────────────

    [Fact]
    public async Task FetchTreeAsync_FailureInOneRepo_DoesNotBlockOthers()
    {
        SetupValidRepo();
        SetupSubmodules(Root, MakeSubmodule("A"));
        SetupSubmodules(Sub("A"));
        SetupRemotes(Sub("A"), Origin());
        SetupRemotes(Root, Origin());
        _git.Setup(g => g.FetchAsync(Sub("A"), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("offline"));
        _git.Setup(g => g.FetchAsync(Root, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateService().FetchTreeAsync(Root);

        result.Entries.Should().HaveCount(2);
        result.Entries.Single(e => e.RelativePath == "A").Outcome.Should().Be(TreeOpOutcome.Failed);
        result.Entries.Single(e => e.RelativePath == ".").Outcome.Should().Be(TreeOpOutcome.Succeeded);
    }

    // ─── StageSubmodulePointersAsync ────────────────────────────────────

    [Fact]
    public async Task StageSubmodulePointersAsync_NormalizesToForwardSlashes()
    {
        SetupCommitPipeline();

        await CreateService().StageSubmodulePointersAsync(Root, [@"modules\A", "B"]);

        _callLog.Should().Equal($"stageFile:{Root}:modules/A", $"stageFile:{Root}:B");
    }

    [Fact]
    public async Task StageSubmodulePointersAsync_EmptyPath_Throws()
    {
        var act = () => CreateService().StageSubmodulePointersAsync(Root, [" "]);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── ResolveTreeRootAsync ───────────────────────────────────────────

    [Fact]
    public async Task ResolveTreeRootAsync_WalksSuperprojectChainToOutermostRoot()
    {
        var nested = Sub("modules", "A", "nested");
        var mid = Sub("modules", "A");

        _git.Setup(g => g.GetRepositoryRootAsync(Path.Combine(nested, "src"), It.IsAny<CancellationToken>())).ReturnsAsync(nested);
        _git.Setup(g => g.GetSuperprojectWorkingTreeAsync(nested, It.IsAny<CancellationToken>())).ReturnsAsync(mid);
        _git.Setup(g => g.GetRepositoryRootAsync(mid, It.IsAny<CancellationToken>())).ReturnsAsync(mid);
        _git.Setup(g => g.GetSuperprojectWorkingTreeAsync(mid, It.IsAny<CancellationToken>())).ReturnsAsync(Root);
        _git.Setup(g => g.GetRepositoryRootAsync(Root, It.IsAny<CancellationToken>())).ReturnsAsync(Root);
        _git.Setup(g => g.GetSuperprojectWorkingTreeAsync(Root, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var root = await CreateService().ResolveTreeRootAsync(Path.Combine(nested, "src"));

        root.Should().Be(Root);
    }

    [Fact]
    public async Task ResolveTreeRootAsync_StandaloneRepo_ReturnsItsRoot()
    {
        _git.Setup(g => g.GetRepositoryRootAsync(Root, It.IsAny<CancellationToken>())).ReturnsAsync(Root);
        _git.Setup(g => g.GetSuperprojectWorkingTreeAsync(Root, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var root = await CreateService().ResolveTreeRootAsync(Root);

        root.Should().Be(Root);
    }

    // ─── GetTreeStatusAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetTreeStatusAsync_ReportsDirtyAndPointerDrift()
    {
        SetupValidRepo();
        var drifted = new SubmoduleInfo
        {
            Name = "A",
            Path = "A",
            Url = "https://example.test/A.git",
            RecordedSha = "1111111111111111111111111111111111111111",
            WorkingSha = "2222222222222222222222222222222222222222",
            Status = SubmoduleStatus.OutOfSync,
        };
        SetupSubmodules(Root, drifted);
        SetupSubmodules(Sub("A"));
        SetupChanges(Sub("A"), DirtyChanges());
        SetupChanges(Root, CleanChanges());
        _git.Setup(g => g.GetRepositoryInfoFastAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryInfo { AheadBy = 2, BehindBy = 0 });

        var status = await CreateService().GetTreeStatusAsync(Root);

        status.Repos.Should().HaveCount(2);
        status.DirtyCount.Should().Be(1);
        status.UnpushedCount.Should().Be(2);
        var rootEntry = status.Repos.Single(r => r.RelativePath == ".");
        rootEntry.SubmodulePointerChanges.Should().ContainSingle(c =>
            c.Path == "A" && c.Status == nameof(SubmoduleStatus.OutOfSync));
        var subEntry = status.Repos.Single(r => r.RelativePath == "A");
        subEntry.IsDirty.Should().BeTrue();
        subEntry.UnstagedFiles.Should().ContainSingle(f => f.Path == "file.txt" && f.Status == nameof(FileChangeStatus.Modified));
    }
}
