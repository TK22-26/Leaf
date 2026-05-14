#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Coverage for the static helpers Phase F added to
/// <see cref="WorkspaceViewModel"/> — uninitialized-submodule
/// detection and throttled bulk-op execution.
/// </summary>
public class WorkspaceViewModelHelperTests
{
    [Fact]
    public void IsSubmoduleUninitialized_ReturnsTrueForMissingPath()
    {
        // Non-existent paths are treated as uninitialized — the tile
        // body switches to the Initialize CTA which runs `git submodule
        // update --init` to clone the repo into the path.
        var missing = Path.Combine(Path.GetTempPath(), "leaf-tests-uninit-" + Guid.NewGuid().ToString("N"));
        WorkspaceViewModel.IsSubmoduleUninitialized(missing).Should().BeTrue();
    }

    [Fact]
    public void IsSubmoduleUninitialized_ReturnsTrueForEmptyDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "leaf-tests-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WorkspaceViewModel.IsSubmoduleUninitialized(dir).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsSubmoduleUninitialized_ReturnsFalseWhenDotGitDirExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "leaf-tests-dotgitdir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        try
        {
            WorkspaceViewModel.IsSubmoduleUninitialized(dir).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsSubmoduleUninitialized_ReturnsFalseWhenDotGitFileExists()
    {
        // Linked worktrees and submodules use a .git *file* pointing
        // into the parent's modules store. The probe must accept
        // both forms.
        var dir = Path.Combine(Path.GetTempPath(), "leaf-tests-dotgitfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".git"), "gitdir: ../.git/modules/x");
        try
        {
            WorkspaceViewModel.IsSubmoduleUninitialized(dir).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunTilesThrottled_RespectsCap()
    {
        // The throttle's whole job is to keep concurrent in-flight
        // ops below the cap. We assert that no observed concurrency
        // exceeded the requested limit.
        const int cap = 3;
        const int count = 12;
        var inflight = 0;
        var peak = 0;
        var lockObj = new object();

        var sp = Composition.TestServices.BuildProvider(Composition.TestServices.CreateCollection());
        var ws = sp.GetRequiredService<WorkspaceViewModel>();
        var tiles = Enumerable.Range(0, count).Select(i =>
        {
            var t = new SubmoduleTileViewModel("C:/r/" + i, "r" + i, isParent: false, scope: null, graph: null);
            t.Workspace = ws;
            return t;
        }).ToList();

        await WorkspaceViewModel.RunTilesThrottledAsync(tiles, async _ =>
        {
            int seen;
            lock (lockObj) { seen = ++inflight; if (seen > peak) peak = seen; }
            await Task.Delay(20);
            lock (lockObj) { inflight--; }
        }, maxParallel: cap);

        peak.Should().BeLessOrEqualTo(cap);
    }

    [Fact]
    public async Task RunTilesThrottled_RunsAllTiles()
    {
        var sp = Composition.TestServices.BuildProvider(Composition.TestServices.CreateCollection());
        var ws = sp.GetRequiredService<WorkspaceViewModel>();
        var tiles = Enumerable.Range(0, 7).Select(i =>
        {
            var t = new SubmoduleTileViewModel("C:/r/" + i, "r" + i, isParent: false, scope: null, graph: null);
            t.Workspace = ws;
            return t;
        }).ToList();

        var seen = new HashSet<string>();
        await WorkspaceViewModel.RunTilesThrottledAsync(tiles, t =>
        {
            lock (seen) seen.Add(t.RepositoryPath);
            return Task.CompletedTask;
        });

        seen.Should().HaveCount(7);
    }
}
