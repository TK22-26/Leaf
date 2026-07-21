using FluentAssertions;
using Leaf.Services;
using Leaf.Services.Git.Core;
using Leaf.Services.Git.Operations;
using Xunit;

namespace Leaf.Tests.Services.Git;

/// <summary>
/// Unit tests for <see cref="StashOperations"/> CLI argument building and
/// failure propagation — the exact logic that regressed in issue #41
/// (StashAsync used LibGit2Sharp which cannot snapshot new gitlinks, and
/// StashStagedAsync discarded the command result so failures were silent).
/// </summary>
public class StashOperationsTests
{
    /// <summary>
    /// Recording runner: captures every argument list and returns a
    /// scripted result. IGitOperationContext is internal, so a hand-rolled
    /// fake (via InternalsVisibleTo) stands in for the Moq-style setup.
    /// </summary>
    private sealed class RecordingRunner : IGitCommandRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public GitCommandResult Result { get; set; } = new(0, "", "", true);

        public event EventHandler<GitCommandEventArgs>? CommandExecuted { add { } remove { } }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            string? input = null,
            string? credentialKey = null,
            IReadOnlyDictionary<string, string>? extraEnvironment = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);
            return Task.FromResult(Result);
        }

        public Task<GitCommandResult> RunAsync(string workingDirectory, GitCommand command, CancellationToken cancellationToken = default)
            => RunAsync(workingDirectory, command.ToArguments(), cancellationToken: cancellationToken);
    }

    private sealed class FakeContext(IGitCommandRunner runner) : IGitOperationContext
    {
        public IGitCommandRunner CommandRunner { get; } = runner;
        public IGitOutputParser OutputParser => throw new NotSupportedException("not used by these tests");
        public IGitErrorMapper ErrorMapper => throw new NotSupportedException("not used by these tests");
    }

    private readonly RecordingRunner _runner = new();
    private readonly StashOperations _ops;

    public StashOperationsTests()
    {
        _ops = new StashOperations(new FakeContext(_runner), conflictOps: null!);
    }

    [Fact]
    public async Task StashAsync_NullMessage_UsesDefaultMessageArgs()
    {
        await _ops.StashAsync(@"C:\repo");

        _runner.Calls.Should().ContainSingle();
        _runner.Calls[0].Should().Equal("stash", "push", "-m", "Stash from Leaf");
    }

    [Fact]
    public async Task StashAsync_ExplicitMessage_PassesItThrough()
    {
        await _ops.StashAsync(@"C:\repo", "WIP: my changes");

        _runner.Calls[0].Should().Equal("stash", "push", "-m", "WIP: my changes");
    }

    [Fact]
    public async Task StashAsync_CommandFails_ThrowsWithTrimmedStderr()
    {
        _runner.Result = new GitCommandResult(1, "", "error: something broke  \n", false);

        var act = () => _ops.StashAsync(@"C:\repo");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Failed to stash: error: something broke");
    }

    [Fact]
    public async Task StashStagedAsync_WithMessage_BuildsStagedArgs()
    {
        await _ops.StashStagedAsync(@"C:\repo", "staged only");

        _runner.Calls[0].Should().Equal("stash", "push", "--staged", "-m", "staged only");
    }

    [Fact]
    public async Task StashStagedAsync_NoMessage_OmitsMessageFlag()
    {
        await _ops.StashStagedAsync(@"C:\repo");

        _runner.Calls[0].Should().Equal("stash", "push", "--staged");
    }

    [Fact]
    public async Task StashStagedAsync_CommandFails_NoLongerSilent()
    {
        _runner.Result = new GitCommandResult(128, "", "fatal: bad state", false);

        var act = () => _ops.StashStagedAsync(@"C:\repo", "msg");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Failed to stash staged changes: fatal: bad state");
    }
}
