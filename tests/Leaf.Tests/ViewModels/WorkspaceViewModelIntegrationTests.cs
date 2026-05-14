#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.Tests.Composition;
using Leaf.Tests.Fakes;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Higher-fidelity integration tests for <see cref="WorkspaceViewModel"/>.
/// These exercise the headline flows the audit called out as the
/// most valuable to pin: parent-stages-submodule-pointers during
/// commit-all-reviewed, pause/resume across <see cref="WorkspaceViewModel.RunMergeLoopAsync"/>,
/// and the <see cref="WorkspaceViewModel.LoadAsync"/> reentrancy guard
/// that the <c>_loadLock</c> semaphore introduces.
/// </summary>
public class WorkspaceViewModelIntegrationTests
{
    // ─── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Build a workspace with a Moq-based IGitService so call order
    /// can be verified. Reuses TestServices for the rest of the
    /// scaffolding (fakes for dispatcher, dialog, AI, etc.).
    /// </summary>
    private static (WorkspaceViewModel ws, Mock<IGitService> git) BuildWorkspaceWithMockGit()
    {
        var services = TestServices.CreateCollection();
        var git = new Mock<IGitService>(MockBehavior.Loose);
        services.Replace(ServiceDescriptor.Singleton<IGitService>(git.Object));
        var sp = TestServices.BuildProvider(services);
        return (sp.GetRequiredService<WorkspaceViewModel>(), git);
    }

    private static SubmoduleTileViewModel MakeTile(string path, string name, bool isParent, WorkspaceViewModel ws)
    {
        var tile = new SubmoduleTileViewModel(path, name, isParent, scope: null, graph: null) { Workspace = ws };
        ws.Tiles.Add(tile);
        return tile;
    }

    // ─── #1: CommitAllReviewedAsync staging order ────────────────

    [Fact]
    public async Task CommitAllReviewed_StagesSubmodulePathsInParent_AfterSubmodulesCommit()
    {
        // Headline regression: the parent's commit must include the new
        // submodule pointer SHAs. CommitComposingTileAsync stages each
        // submodule's relative path into the parent's index right
        // before the parent commits. Verify both the staging and the
        // ordering — subs commit first, then parent stages each sub
        // path, then parent commits.
        var (ws, git) = BuildWorkspaceWithMockGit();
        ws.Parent = new RepositoryInfo { Path = "C:/r/parent", Name = "parent" };

        var parent = MakeTile("C:/r/parent", "parent", isParent: true, ws);
        var subA = MakeTile("C:/r/parent/sub-a", "sub-a", isParent: false, ws);
        var subB = MakeTile("C:/r/parent/sub-b", "sub-b", isParent: false, ws);

        foreach (var t in new[] { parent, subA, subB })
        {
            t.Mode = TileMode.Composing;
            t.ComposingMessage = $"{t.Name} message";
            // Mark messages as hand-edited so the auto-regen path
            // doesn't fire (we're testing the staging order, not the
            // regen behaviour).
            t.AiOriginalMessage = "different";
        }
        ws.IsReviewing = true;

        // Record the call order of staging + committing.
        var callLog = new List<string>();
        var callLock = new object();
        void Log(string entry) { lock (callLock) callLog.Add(entry); }

        git.Setup(g => g.StageFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback<string, string, CancellationToken>((repo, file, _) => Log($"StageFile:{repo}:{file}"))
           .Returns(Task.CompletedTask);
        git.Setup(g => g.CommitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
           .Callback<string, string, string?, bool, CancellationToken>((repo, _, _, _, _) => Log($"Commit:{repo}"))
           .Returns(Task.CompletedTask);
        git.Setup(g => g.GetRepositoryInfoFastAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new RepositoryInfo());

        await ws.CommitAllReviewedAsync();

        // Every submodule commit must complete before any parent stage
        // or parent commit. Find the indices of the parent staging
        // events and the submodule commits; assert that no parent
        // stage event precedes any submodule commit.
        var parentStageIdx = callLog.FindIndex(e => e == "StageFile:C:/r/parent:sub-a");
        var parentStageBIdx = callLog.FindIndex(e => e == "StageFile:C:/r/parent:sub-b");
        var parentCommitIdx = callLog.FindIndex(e => e == "Commit:C:/r/parent");
        var subACommitIdx = callLog.FindIndex(e => e == "Commit:C:/r/parent/sub-a");
        var subBCommitIdx = callLog.FindIndex(e => e == "Commit:C:/r/parent/sub-b");

        parentStageIdx.Should().BeGreaterThan(subACommitIdx, "sub-a must commit before its pointer is staged in the parent");
        parentStageIdx.Should().BeGreaterThan(subBCommitIdx, "sub-b must commit before its pointer is staged in the parent");
        parentStageBIdx.Should().BeGreaterThan(subACommitIdx);
        parentStageBIdx.Should().BeGreaterThan(subBCommitIdx);
        parentCommitIdx.Should().BeGreaterThan(parentStageIdx, "parent must commit AFTER its index includes the submodule pointer paths");
        parentCommitIdx.Should().BeGreaterThan(parentStageBIdx);
    }

    [Fact]
    public async Task CommitAllReviewed_RegeneratesParentMessage_WhenUnedited()
    {
        // Audit item #16 regression guard. The parent's AI message is
        // generated at review-start, when submodule pointers haven't
        // moved yet. After submodules commit during Approve All, the
        // parent's diff includes the pointer bumps — so if the user
        // hasn't hand-edited the message, we regen so the commit text
        // reflects the now-final diff. The regen runs StageAllAsync
        // (which captures the new submodule SHAs) before producing the
        // new message, then CommitComposingTileAsync redundantly stages
        // the submodule paths and commits.
        var (ws, git) = BuildWorkspaceWithMockGit();
        ws.Parent = new RepositoryInfo { Path = "C:/r/parent", Name = "parent" };

        var parent = MakeTile("C:/r/parent", "parent", isParent: true, ws);
        var subA = MakeTile("C:/r/parent/sub-a", "sub-a", isParent: false, ws);

        // Parent's composer text equals the pristine AI output —
        // CommitAllReviewedAsync should treat it as unedited and regen.
        const string pristine = "bump submodules";
        parent.Mode = TileMode.Composing;
        parent.ComposingMessage = pristine;
        parent.AiOriginalMessage = pristine;
        parent.ComposingDescription = "";
        parent.AiOriginalDescription = "";

        subA.Mode = TileMode.Composing;
        subA.ComposingMessage = "sub-a msg";
        subA.AiOriginalMessage = "different";

        ws.IsReviewing = true;

        var stageAllCalls = new List<string>();
        git.Setup(g => g.StageAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback<string, CancellationToken>((repo, _) => stageAllCalls.Add(repo))
           .Returns(Task.CompletedTask);
        git.Setup(g => g.StageFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        git.Setup(g => g.CommitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        git.Setup(g => g.GetStagedSummaryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync("diff text including submodule pointer bumps");
        git.Setup(g => g.GetRepositoryInfoFastAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new RepositoryInfo());

        await ws.CommitAllReviewedAsync();

        // GenerateAiMessageForTileAsync was called for the parent → it
        // calls StageAllAsync(parent), which is the step that captures
        // the submodule pointer bumps into the parent's index BEFORE
        // the AI sees the diff. This guarantees the regenerated message
        // describes the right state.
        stageAllCalls.Should().Contain("C:/r/parent",
            "parent must be re-staged via StageAllAsync during the unedited-message regen");
    }

    // ─── #2: Merge pause / resume ────────────────────────────────

    [Fact]
    public async Task RunMergeLoop_PausesAtConflictTile_ResumeContinuesFromThere()
    {
        // Workspace order: sub-a, sub-b, sub-c, parent (WriteOrder).
        // sub-a merges OK, sub-b conflicts → pause. After the user
        // "resolves" (we make sub-b succeed on retry), Continue must
        // pick up at sub-b, not re-merge sub-a.
        var (ws, git) = BuildWorkspaceWithMockGit();
        ws.Parent = new RepositoryInfo { Path = "C:/r/parent", Name = "parent" };

        MakeTile("C:/r/parent", "parent", isParent: true, ws);
        var subA = MakeTile("C:/r/parent/sub-a", "sub-a", isParent: false, ws);
        var subB = MakeTile("C:/r/parent/sub-b", "sub-b", isParent: false, ws);
        var subC = MakeTile("C:/r/parent/sub-c", "sub-c", isParent: false, ws);

        var mergeAttempts = new Dictionary<string, int>();
        git.Setup(g => g.MergeBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
           .Returns<string, string, bool, CancellationToken>((repo, _, _, _) =>
           {
               mergeAttempts.TryGetValue(repo, out var count);
               mergeAttempts[repo] = count + 1;
               // First attempt at sub-b: conflict. Second attempt: success
               // (simulates the user resolving the conflict between runs).
               if (repo == subB.RepositoryPath && count == 0)
                   return Task.FromResult(new MergeResult { Success = false, HasConflicts = true });
               return Task.FromResult(new MergeResult { Success = true });
           });
        git.Setup(g => g.GetRepositoryInfoFastAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new RepositoryInfo());

        await ws.RunMergeLoopAsync("develop", MergeType.Normal, resumeFromTilePath: null);

        // First pass: sub-a merged, sub-b conflicted → paused.
        ws.HasPausedMerge.Should().BeTrue();
        ws.PausedMerge!.PausedAtTilePath.Should().Be(subB.RepositoryPath);
        mergeAttempts[subA.RepositoryPath].Should().Be(1);
        mergeAttempts[subB.RepositoryPath].Should().Be(1);
        mergeAttempts.ContainsKey(subC.RepositoryPath).Should().BeFalse("sub-c is past the pause point");

        // Resume — second attempt at sub-b now succeeds, so the loop
        // continues through sub-c and parent.
        await ws.RunMergeLoopAsync(ws.PausedMerge.Target, ws.PausedMerge.MergeType, resumeFromTilePath: ws.PausedMerge.PausedAtTilePath);

        ws.HasPausedMerge.Should().BeFalse("resume completed the merge");
        mergeAttempts[subA.RepositoryPath].Should().Be(1, "sub-a must not be re-merged on resume");
        mergeAttempts[subB.RepositoryPath].Should().Be(2);
        mergeAttempts[subC.RepositoryPath].Should().Be(1);
        mergeAttempts["C:/r/parent"].Should().Be(1);
    }

    // ─── TryRevParseAsync — remote-branch detection ──────────────

    [Theory]
    [InlineData("feature/x", true)]   // matches origin/feature/x via remote check
    [InlineData("main", true)]        // matches local main
    [InlineData("origin/main", true)] // matches remote main by full friendly name
    [InlineData("missing", false)]    // does not exist anywhere
    [InlineData("x", false)]          // last-segment fallback dropped — was a bug
    public async Task TryRevParseAsync_HandlesRemoteOnlyBranchWithSlash(string query, bool expected)
    {
        // The pen-test discovered that after the audit cleanup,
        // BranchInfo.Name comes through as the libgit2 FriendlyName
        // ("origin/feature/x" for remotes), so the second condition
        // had to compare against $"{RemoteName}/{branchName}", NOT
        // $"{RemoteName}/{b.Name}" (which would double-prefix to
        // "origin/origin/feature/x"). This test pins the contract.
        var (ws, git) = BuildWorkspaceWithMockGit();
        git.Setup(g => g.GetBranchesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new List<BranchInfo>
           {
               new() { Name = "main",              IsRemote = false },
               new() { Name = "origin/main",       IsRemote = true,  RemoteName = "origin" },
               new() { Name = "origin/feature/x",  IsRemote = true,  RemoteName = "origin" },
           });

        var result = await ws.TryRevParseAsync("C:/repo", query, CancellationToken.None);

        result.Should().Be(expected);
    }

    // ─── #3: LoadAsync reentrancy ────────────────────────────────

    [Fact]
    public async Task LoadAsync_ConcurrentCalls_DoNotDoubleTiles()
    {
        // Fire two LoadAsync calls in parallel with different parents.
        // The SemaphoreSlim _loadLock should serialize them so the
        // second call's DisposeTiles cleans up the first's tiles
        // before populating its own. Final Tiles should reflect only
        // the second parent.
        var (ws, git) = BuildWorkspaceWithMockGit();

        var parentA = new RepositoryInfo { Path = "C:/r/parent-a", Name = "parent-a" };
        var parentB = new RepositoryInfo { Path = "C:/r/parent-b", Name = "parent-b" };

        static SubmoduleInfo Sub(string name) => new()
        {
            Name = name,
            Path = name,
            Url = "",
            RecordedSha = "",
            Status = SubmoduleStatus.UpToDate,
        };
        var subsA = new List<SubmoduleInfo> { Sub("sub-a1"), Sub("sub-a2") };
        var subsB = new List<SubmoduleInfo> { Sub("sub-b1") };

        // Make the first GetSubmodulesAsync hang briefly so the second
        // LoadAsync is forced to wait on the semaphore. After the
        // first completes, the second runs DisposeTiles + populates
        // its own tiles.
        var firstReleased = new TaskCompletionSource<bool>();
        git.Setup(g => g.GetSubmodulesAsync(parentA.Path, It.IsAny<CancellationToken>()))
           .Returns(async (string _, CancellationToken _) =>
           {
               await firstReleased.Task;
               return subsA;
           });
        git.Setup(g => g.GetSubmodulesAsync(parentB.Path, It.IsAny<CancellationToken>()))
           .ReturnsAsync(subsB);
        git.Setup(g => g.GetRepositoryInfoFastAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new RepositoryInfo());

        var loadA = ws.LoadAsync(parentA, parentGraph: null!);
        // Let LoadA enter the semaphore.
        await Task.Delay(20);
        var loadB = ws.LoadAsync(parentB, parentGraph: null!);

        // Release LoadA so LoadB can proceed.
        firstReleased.SetResult(true);
        await Task.WhenAll(loadA, loadB);

        // Final state should be parent-b's tile set: 1 parent + 1 sub.
        ws.Parent!.Path.Should().Be(parentB.Path);
        ws.Tiles.Count.Should().Be(2, "parent-b plus its single submodule, with no doubling from parent-a");
        ws.Tiles.Where(t => t.IsParent).Should().ContainSingle();
    }
}
