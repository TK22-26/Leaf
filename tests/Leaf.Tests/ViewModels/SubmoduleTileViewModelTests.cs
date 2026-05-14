#nullable enable
using FluentAssertions;
using Leaf.Models;
using Leaf.Tests.Composition;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leaf.Tests.ViewModels;

/// <summary>
/// Pure-VM tests for <see cref="SubmoduleTileViewModel"/>'s mode
/// state machine and the parent-tile commit gating that prevents
/// the user from committing the parent while submodules are still
/// in compose (which would record stale pointer SHAs).
/// </summary>
public class SubmoduleTileViewModelTests
{
    private static SubmoduleTileViewModel MakeTile(string name, bool isParent) =>
        new(repositoryPath: "C:/repos/" + name, name: name, isParent: isParent, scope: null, graph: null);

    [Fact]
    public void DefaultMode_IsNormal()
    {
        var tile = MakeTile("docs", isParent: false);
        tile.Mode.Should().Be(TileMode.Normal);
        tile.IsComposing.Should().BeFalse();
    }

    [Fact]
    public void IsComposing_TracksMode()
    {
        var tile = MakeTile("docs", isParent: false);
        tile.Mode = TileMode.Composing;
        tile.IsComposing.Should().BeTrue();
        tile.Mode = TileMode.Normal;
        tile.IsComposing.Should().BeFalse();
    }

    [Fact]
    public void CanCommitCompose_BlockedByEmptyMessage()
    {
        var tile = MakeTile("docs", isParent: false);
        tile.Mode = TileMode.Composing;
        tile.ComposingMessage = "";
        tile.CanCommitCompose.Should().BeFalse();
        tile.ComposingMessage = "feat: x";
        tile.CanCommitCompose.Should().BeTrue();
    }

    [Fact]
    public void CanCommitCompose_BlockedWhileGenerating()
    {
        var tile = MakeTile("docs", isParent: false);
        tile.Mode = TileMode.Composing;
        tile.ComposingMessage = "ready";
        tile.IsGeneratingAi = true;
        tile.CanCommitCompose.Should().BeFalse();
    }

    [Fact]
    public void CanCommitCompose_BlockedWhileCommitting()
    {
        var tile = MakeTile("docs", isParent: false);
        tile.Mode = TileMode.Composing;
        tile.ComposingMessage = "ready";
        tile.IsCommitting = true;
        tile.CanCommitCompose.Should().BeFalse();
    }

    [Fact]
    public void Parent_CanCommitCompose_BlockedWhileAnySubmoduleStillComposing()
    {
        // The parent tile cannot commit until every dirty submodule
        // has finished composing — otherwise its index would record
        // the submodule SHAs as they were before the sub committed,
        // which is exactly the bug we fixed in CommitComposingTileAsync.
        var parent = MakeTile("parent", isParent: true);
        var sub = MakeTile("sub", isParent: false);

        var ws = BuildWorkspaceWithTiles(parent, sub);

        parent.Mode = TileMode.Composing;
        parent.ComposingMessage = "bump submodules";

        sub.Mode = TileMode.Composing;
        ws.NotifyComposeStateChanged();
        parent.CanCommitCompose.Should().BeFalse();

        sub.Mode = TileMode.Normal;
        ws.NotifyComposeStateChanged();
        parent.CanCommitCompose.Should().BeTrue();
    }

    [Fact]
    public void IsInitialized_InvertsIsUninitialized()
    {
        var tile = MakeTile("docs", isParent: false);
        tile.IsUninitialized.Should().BeFalse();
        tile.IsInitialized.Should().BeTrue();
        tile.IsUninitialized = true;
        tile.IsInitialized.Should().BeFalse();
    }

    /// <summary>
    /// Builds a real <see cref="WorkspaceViewModel"/> via the test
    /// service provider (with fakes wired in by
    /// <see cref="TestServices.CreateCollection"/>) and seeds it with
    /// the supplied tiles. Avoids a hand-rolled subclass that would
    /// fight the constructor's null guards.
    /// </summary>
    private static WorkspaceViewModel BuildWorkspaceWithTiles(params SubmoduleTileViewModel[] tiles)
    {
        var sp = TestServices.BuildProvider(TestServices.CreateCollection());
        var ws = sp.GetRequiredService<WorkspaceViewModel>();
        foreach (var t in tiles)
        {
            t.Workspace = ws;
            ws.Tiles.Add(t);
        }
        return ws;
    }
}
