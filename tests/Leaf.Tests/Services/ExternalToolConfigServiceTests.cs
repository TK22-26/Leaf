using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.Tests.Fakes;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Unit tests for the config service. Uses a stub IGitService that
/// records config reads/writes in-memory so we can assert the right
/// git-config keys are touched without a real repo.
/// </summary>
public class ExternalToolConfigServiceTests
{
    [Fact]
    public async Task SetSelectedToolAsync_WritesCmdAndSelector()
    {
        var git = new StubConfigGitService();
        var sut = new ExternalToolConfigService(git);
        var tool = ExternalToolPresets.TryGet("vscode", ExternalToolKind.Merge)!;

        await sut.SetSelectedToolAsync("/repo", tool, GitConfigScope.Local);

        git.Writes.Should().ContainKey("mergetool.vscode.cmd");
        git.Writes["mergetool.vscode.cmd"].Should().Contain("$MERGED");
        git.Writes["merge.tool"].Should().Be("vscode");
    }

    [Fact]
    public async Task SetSelectedToolAsync_BuiltInSentinel_OnlyClearsSelector()
    {
        var git = new StubConfigGitService
        {
            Reads = { ["merge.tool"] = "bcomp" }
        };
        var sut = new ExternalToolConfigService(git);
        var builtin = ExternalTool.BuiltIn(ExternalToolKind.Merge);

        await sut.SetSelectedToolAsync("/repo", builtin, GitConfigScope.Local);

        git.Unsets.Should().Contain("merge.tool");
        // Must not touch any cmd registration — other git clients keep
        // their own settings.
        git.Writes.Should().NotContainKey("mergetool.leaf-builtin.cmd");
    }

    [Fact]
    public async Task GetCurrentToolAsync_ReturnsPreset_WhenCmdMissing()
    {
        var git = new StubConfigGitService
        {
            Reads = { ["diff.tool"] = "bcomp" }
        };
        var sut = new ExternalToolConfigService(git);

        var tool = await sut.GetCurrentToolAsync("/repo", ExternalToolKind.Diff);

        tool.Should().NotBeNull();
        tool!.Name.Should().Be("bcomp");
        tool.DisplayName.Should().Be("Beyond Compare");
    }

    [Fact]
    public async Task GetCurrentToolAsync_NoSelection_ReturnsNull()
    {
        var git = new StubConfigGitService();
        var sut = new ExternalToolConfigService(git);

        var tool = await sut.GetCurrentToolAsync("/repo", ExternalToolKind.Diff);

        tool.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentToolAsync_BuiltInSelection_ReturnsNull()
    {
        var git = new StubConfigGitService
        {
            Reads = { ["diff.tool"] = ExternalTool.BuiltInName }
        };
        var sut = new ExternalToolConfigService(git);

        var tool = await sut.GetCurrentToolAsync("/repo", ExternalToolKind.Diff);

        tool.Should().BeNull("built-in sentinel signals 'no external tool'");
    }

    [Fact]
    public async Task GetCurrentToolAsync_CustomCmd_OverridesPreset()
    {
        var git = new StubConfigGitService
        {
            Reads =
            {
                ["merge.tool"] = "vscode",
                ["mergetool.vscode.cmd"] = "\"C:\\my\\code.exe\" --wait --merge $LOCAL $REMOTE $BASE $MERGED"
            }
        };
        var sut = new ExternalToolConfigService(git);

        var tool = await sut.GetCurrentToolAsync("/repo", ExternalToolKind.Merge);

        tool!.Command.Should().Be(@"C:\my\code.exe");
        tool.ArgsTemplate.Should().Be("--wait --merge $LOCAL $REMOTE $BASE $MERGED");
    }

    [Theory]
    [InlineData("bcomp \"$LOCAL\" \"$REMOTE\"", "bcomp", "\"$LOCAL\" \"$REMOTE\"")]
    [InlineData("\"C:\\Program Files\\Beyond Compare 5\\BCompare.exe\" \"$LOCAL\"",
        "C:\\Program Files\\Beyond Compare 5\\BCompare.exe", "\"$LOCAL\"")]
    [InlineData("code", "code", "")]
    public void SplitCmd_HandlesCommonShapes(string cmd, string expectedCommand, string expectedArgs)
    {
        var (command, args) = ExternalToolConfigService.SplitCmd(cmd);
        command.Should().Be(expectedCommand);
        args.Should().Be(expectedArgs);
    }

    // In-memory IGitService stub: records config writes/reads/unsets so
    // tests can assert on them. Everything else is no-op / default.
    private sealed class StubConfigGitService : FakeGitService
    {
        public Dictionary<string, string> Writes { get; } = new();
        public Dictionary<string, string> Reads { get; } = new();
        public List<string> Unsets { get; } = new();

        public override Task SetConfigAsync(string repoPath, string key, string value, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
        {
            Writes[key] = value;
            return Task.CompletedTask;
        }

        public override Task<string?> GetConfigAsync(string repoPath, string key, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Reads.TryGetValue(key, out var v) ? v : null);
        }

        public override Task UnsetConfigAsync(string repoPath, string key, GitConfigScope scope = GitConfigScope.Local, CancellationToken cancellationToken = default)
        {
            Unsets.Add(key);
            return Task.CompletedTask;
        }
    }
}
