using FluentAssertions;
using Leaf.Services;
using Leaf.Services.Git.Core;
using Leaf.Services.Git.Operations;
using Xunit;

namespace Leaf.Tests.Services.Git;

/// <summary>
/// Exercises <see cref="ConfigOperations"/> directly with a recording
/// <see cref="IGitCommandRunner"/>. Tests focus on the git-exit-code
/// semantics that are easy to regress without a harness — in particular
/// the exit-5 "key not found" path for <c>git config --unset</c>.
/// </summary>
public class ConfigOperationsTests
{
    [Fact]
    public async Task UnsetConfigAsync_KeyMissing_ReturnsSilently()
    {
        // git-config exits 5 with no stderr when the key doesn't exist.
        // Before the fix, the operation threw with an empty message
        // because the error check only matched against stderr text.
        var runner = new RecordingRunner(_ => new GitCommandResult(5, string.Empty, string.Empty, Success: false));
        var sut = new ConfigOperations(new StubContext(runner));

        await sut.Invoking(s => s.UnsetConfigAsync("/repo", "diff.tool"))
            .Should().NotThrowAsync("exit 5 means the key was already absent");
    }

    [Fact]
    public async Task UnsetConfigAsync_OtherFailure_Throws()
    {
        var runner = new RecordingRunner(_ => new GitCommandResult(
            ExitCode: 128,
            StandardOutput: string.Empty,
            StandardError: "fatal: not a git repository",
            Success: false));
        var sut = new ConfigOperations(new StubContext(runner));

        await sut.Invoking(s => s.UnsetConfigAsync("/repo", "diff.tool"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a git repository*");
    }

    [Fact]
    public async Task SetConfigAsync_GlobalScope_EmitsGlobalFlag()
    {
        var runner = new RecordingRunner(_ => new GitCommandResult(0, string.Empty, string.Empty, Success: true));
        var sut = new ConfigOperations(new StubContext(runner));

        await sut.SetConfigAsync("/repo", "diff.tool", "vscode", GitConfigScope.Global);

        runner.LastArgs.Should().Equal("config", "--global", "diff.tool", "vscode");
    }

    [Fact]
    public async Task SetConfigAsync_LocalScopeIsDefault_OmitsGlobalFlag()
    {
        var runner = new RecordingRunner(_ => new GitCommandResult(0, string.Empty, string.Empty, Success: true));
        var sut = new ConfigOperations(new StubContext(runner));

        await sut.SetConfigAsync("/repo", "user.name", "Test User");

        runner.LastArgs.Should().Equal("config", "user.name", "Test User");
    }

    [Fact]
    public async Task UnsetConfigAsync_GlobalScope_EmitsGlobalFlag()
    {
        var runner = new RecordingRunner(_ => new GitCommandResult(0, string.Empty, string.Empty, Success: true));
        var sut = new ConfigOperations(new StubContext(runner));

        await sut.UnsetConfigAsync("/repo", "diff.tool", GitConfigScope.Global);

        runner.LastArgs.Should().Equal("config", "--global", "--unset", "diff.tool");
    }

    // Records the arguments of the last invocation so tests can assert on
    // the exact CLI shape emitted by ConfigOperations.
    private sealed class RecordingRunner : IGitCommandRunner
    {
        private readonly Func<IReadOnlyList<string>, GitCommandResult> _respond;

        public RecordingRunner(Func<IReadOnlyList<string>, GitCommandResult> respond)
        {
            _respond = respond;
        }

        public IReadOnlyList<string> LastArgs { get; private set; } = [];

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            string? input = null,
            string? credentialKey = null,
            CancellationToken cancellationToken = default)
        {
            LastArgs = arguments;
            return Task.FromResult(_respond(arguments));
        }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            GitCommand command,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(workingDirectory, command.ToArguments(), cancellationToken: cancellationToken);
        }
    }

    // ConfigOperations only uses CommandRunner from the context, so the
    // parser/mapper members stay null-tolerant.
    private sealed class StubContext : IGitOperationContext
    {
        public StubContext(IGitCommandRunner runner)
        {
            CommandRunner = runner;
        }

        public IGitCommandRunner CommandRunner { get; }
        public IGitOutputParser OutputParser => throw new NotImplementedException();
        public IGitErrorMapper ErrorMapper => throw new NotImplementedException();
    }
}
