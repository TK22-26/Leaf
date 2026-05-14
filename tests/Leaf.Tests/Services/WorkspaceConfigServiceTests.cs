#nullable enable
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// End-to-end tests for <see cref="WorkspaceConfigService"/>. Spins up a
/// real git repo on disk, writes the workspace section via the service,
/// and verifies the values round-trip both through the service and via
/// raw <c>git config</c> — the same value a third-party Git client
/// would read.
/// </summary>
public class WorkspaceConfigServiceTests : IDisposable
{
    private readonly string _repoPath;
    private readonly WorkspaceConfigService _service;

    public WorkspaceConfigServiceTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), $"leaf-workspace-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_repoPath);
        RunGit("init", "-b", "main");
        RunGit("config", "user.email", "test@leaf.dev");
        RunGit("config", "user.name", "Test User");

        _service = new WorkspaceConfigService(new GitCommandRunner());
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_repoPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_repoPath, recursive: true);
        }
        catch { /* best-effort */ }
    }

    private void RunGit(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }

    private string? ReadConfigDirect(string key)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("config");
        psi.ArgumentList.Add("--local");
        psi.ArgumentList.Add("--get");
        psi.ArgumentList.Add(key);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 ? stdout.Trim() : null;
    }

    [Fact]
    public async Task GetMode_NoEntry_ReturnsSingle()
    {
        var mode = await _service.GetModeAsync(_repoPath);

        // Fresh repo with no leaf.workspace.mode set must default to
        // Single — preserves today's UX for users who never touch the
        // grid view.
        mode.Should().Be(WorkspaceMode.Single);
    }

    [Fact]
    public async Task SetMode_Grid_RoundTripsThroughGitConfig()
    {
        await _service.SetModeAsync(_repoPath, WorkspaceMode.Grid);

        // Service read.
        (await _service.GetModeAsync(_repoPath)).Should().Be(WorkspaceMode.Grid);
        // Raw git read — proves any other client (SourceTree, command
        // line) sees the same value the service wrote.
        ReadConfigDirect("leaf.workspace.mode").Should().Be("grid");
    }

    [Fact]
    public async Task SetMode_Single_PersistsExplicitly()
    {
        await _service.SetModeAsync(_repoPath, WorkspaceMode.Grid);
        await _service.SetModeAsync(_repoPath, WorkspaceMode.Single);

        (await _service.GetModeAsync(_repoPath)).Should().Be(WorkspaceMode.Single);
        ReadConfigDirect("leaf.workspace.mode").Should().Be("single");
    }

    [Fact]
    public async Task GetPinnedTileOrder_NoEntry_ReturnsEmpty()
    {
        var order = await _service.GetPinnedTileOrderAsync(_repoPath);

        order.Should().BeEmpty();
    }

    [Fact]
    public async Task SetPinnedTileOrder_RoundTripsAsCsv()
    {
        await _service.SetPinnedTileOrderAsync(_repoPath, new[] { "libs/a", "libs/b", "tools/c" });

        var order = await _service.GetPinnedTileOrderAsync(_repoPath);
        order.Should().Equal("libs/a", "libs/b", "tools/c");
        ReadConfigDirect("leaf.workspace.pinnedorder").Should().Be("libs/a,libs/b,tools/c");
    }

    [Fact]
    public async Task SetPinnedTileOrder_Empty_UnsetsTheKey()
    {
        await _service.SetPinnedTileOrderAsync(_repoPath, new[] { "libs/a" });
        await _service.SetPinnedTileOrderAsync(_repoPath, Array.Empty<string>());

        ReadConfigDirect("leaf.workspace.pinnedorder").Should().BeNull();
        (await _service.GetPinnedTileOrderAsync(_repoPath)).Should().BeEmpty();
    }
}
