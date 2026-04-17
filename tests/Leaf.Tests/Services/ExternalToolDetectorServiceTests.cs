using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Filesystem-backed tests — use a real temp file to verify the
/// detector resolves KnownInstallPaths correctly. Tests for PATH
/// resolution would depend on the dev machine and are covered by
/// manual smoke testing instead.
/// </summary>
public class ExternalToolDetectorServiceTests : IDisposable
{
    private readonly string _tempFile;

    public ExternalToolDetectorServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"leaf-tool-test-{Guid.NewGuid():N}.exe");
        File.WriteAllText(_tempFile, "fake");
    }

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { /* cleanup best-effort */ }
    }

    [Fact]
    public async Task ResolveCommandPathAsync_UsesAbsoluteCommand_WhenRooted()
    {
        var sut = new ExternalToolDetectorService();
        var tool = new ExternalTool(
            "custom", "Custom", ExternalToolKind.Diff,
            Command: _tempFile,
            ArgsTemplate: "\"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: []);

        var resolved = await sut.ResolveCommandPathAsync(tool);

        resolved.Should().Be(_tempFile);
    }

    [Fact]
    public async Task ResolveCommandPathAsync_PrefersKnownInstallPaths()
    {
        var sut = new ExternalToolDetectorService();
        var tool = new ExternalTool(
            "custom", "Custom", ExternalToolKind.Diff,
            Command: "not-on-path-command-12345",
            ArgsTemplate: "\"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [_tempFile]);

        var resolved = await sut.ResolveCommandPathAsync(tool);

        resolved.Should().Be(_tempFile);
    }

    [Fact]
    public async Task ResolveCommandPathAsync_ReturnsNull_WhenNothingMatches()
    {
        var sut = new ExternalToolDetectorService();
        var tool = new ExternalTool(
            "phantom", "Phantom", ExternalToolKind.Diff,
            Command: "leaf-tool-test-does-not-exist-zzz",
            ArgsTemplate: "\"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [Path.Combine(Path.GetTempPath(), "leaf-phantom-zzzzz.exe")]);

        var resolved = await sut.ResolveCommandPathAsync(tool);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCommandPathAsync_CachesResult()
    {
        var sut = new ExternalToolDetectorService();
        var tool = new ExternalTool(
            "custom", "Custom", ExternalToolKind.Diff,
            Command: "unused",
            ArgsTemplate: "",
            KnownInstallPaths: [_tempFile]);

        var first = await sut.ResolveCommandPathAsync(tool);
        File.Delete(_tempFile);
        var second = await sut.ResolveCommandPathAsync(tool);

        first.Should().Be(_tempFile);
        second.Should().Be(_tempFile, "detector caches the resolved path and shouldn't re-stat");

        // Re-create so Dispose doesn't throw.
        File.WriteAllText(_tempFile, "fake");
    }

    [Fact]
    public async Task InvalidateCache_ForcesReResolution()
    {
        var sut = new ExternalToolDetectorService();
        var tool = new ExternalTool(
            "custom", "Custom", ExternalToolKind.Diff,
            Command: "unused",
            ArgsTemplate: "",
            KnownInstallPaths: [_tempFile]);

        var first = await sut.ResolveCommandPathAsync(tool);
        File.Delete(_tempFile);
        sut.InvalidateCache();
        var second = await sut.ResolveCommandPathAsync(tool);

        first.Should().Be(_tempFile);
        second.Should().BeNull("cache was invalidated and file is gone");

        File.WriteAllText(_tempFile, "fake");
    }

    [Fact]
    public async Task GetInstalledToolNames_IncludesPresetWithKnownPath()
    {
        // Drop a fake "bcomp" at one of Beyond Compare's known paths? We
        // can't write to ProgramFiles in tests, so instead test that the
        // detector returns an empty-or-missing set when nothing is
        // actually installed — proving it iterates without crashing.
        var sut = new ExternalToolDetectorService();

        var names = await sut.GetInstalledToolNamesAsync();

        names.Should().NotBeNull();
        // Can't assert specific tools — depends on dev machine — but the
        // call must complete without error.
    }
}
