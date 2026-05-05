#nullable enable
using System.IO;
using System.Text;
using FluentAssertions;
using Leaf.Services.Ai;
using Xunit;

namespace Leaf.Tests.Services.Ai;

/// <summary>
/// Tests for <see cref="AiCliRunner"/>. The runner spawns real child
/// processes to exercise the full transport (PATH resolution, batch-file
/// wrapping, stdin / stdout / stderr drain, timeout, kill). All shims are
/// <c>.bat</c> files written into <see cref="Path.GetTempPath"/> and
/// deleted in the test's <c>finally</c>.
/// </summary>
public class AiCliRunnerTests
{
    private static AiCliInvocation Inv(string exe, string stdin = "", params string[] args)
        => new(exe, args, stdin, WorkingDirectory: null);

    [Fact]
    public async Task SuccessfulInvocation_ReturnsStdout()
    {
        var shim = CreateBatShim("echo HELLO\r\nexit /b 0");
        try
        {
            var result = await new AiCliRunner().RunAsync(Inv(shim), timeoutSeconds: 10);
            result.Success.Should().BeTrue();
            result.ExitCode.Should().Be(0);
            result.Stdout.Trim().Should().Be("HELLO");
            result.Detail.Should().BeEmpty();
        }
        finally { File.Delete(shim); }
    }

    [Fact]
    public async Task NonZeroExit_FailsWithDetail()
    {
        var shim = CreateBatShim("echo broken 1>&2\r\nexit /b 7");
        try
        {
            var result = await new AiCliRunner().RunAsync(Inv(shim), timeoutSeconds: 10);
            result.Success.Should().BeFalse();
            result.ExitCode.Should().Be(7);
            result.Detail.Should().Contain("exit 7");
            result.Detail.Should().Contain("broken");
        }
        finally { File.Delete(shim); }
    }

    [Fact]
    public async Task EmptyStdout_IsTreatedAsFailure()
    {
        // Some CLIs exit 0 with no output on a "ran but produced nothing"
        // edge case; from the caller's perspective that's still useless,
        // so we surface it as a failure with a clear detail.
        var shim = CreateBatShim("exit /b 0");
        try
        {
            var result = await new AiCliRunner().RunAsync(Inv(shim), timeoutSeconds: 10);
            result.Success.Should().BeFalse();
            result.Detail.Should().Be("no output");
        }
        finally { File.Delete(shim); }
    }

    [Fact]
    public async Task MissingExecutable_FailsWithoutThrowing()
    {
        var result = await new AiCliRunner().RunAsync(
            Inv("definitely-not-a-real-cli-leaftest"),
            timeoutSeconds: 5);
        result.Success.Should().BeFalse();
        // Either resolution fails (Win32Exception "not found") or Process.Start
        // returns null — both produce a non-success result, no throw.
        result.Detail.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Timeout_KillsProcessAndReturnsTimeoutDetail()
    {
        // ping -n 5 sleeps ~5s. With a 1s timeout we should kill it well
        // before completion and surface a timeout result.
        var shim = CreateBatShim("ping -n 5 127.0.0.1 >nul\r\necho should-not-reach\r\nexit /b 0");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await new AiCliRunner().RunAsync(Inv(shim), timeoutSeconds: 1);
            sw.Stop();
            result.Success.Should().BeFalse();
            result.Detail.Should().Contain("timed out");
            // Sanity: we shouldn't have waited the full 5s the shim sleeps for.
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
        }
        finally { File.Delete(shim); }
    }

    [Fact]
    public async Task Cancellation_PropagatesAndKillsProcess()
    {
        var shim = CreateBatShim("ping -n 5 127.0.0.1 >nul\r\nexit /b 0");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var act = () => new AiCliRunner().RunAsync(Inv(shim), timeoutSeconds: 30, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { File.Delete(shim); }
    }

    [Fact]
    public async Task StdinWritten_AndAvailableOnStdout()
    {
        // Echoes whatever it received on stdin back on stdout via 'find /v ""'
        // (Windows trick: list non-empty lines through the input stream).
        var shim = CreateBatShim("findstr /R \".*\"");
        try
        {
            var result = await new AiCliRunner().RunAsync(
                Inv(shim, stdin: "the-prompt-payload"),
                timeoutSeconds: 10);
            result.Success.Should().BeTrue();
            result.Stdout.Should().Contain("the-prompt-payload");
        }
        finally { File.Delete(shim); }
    }

    private static string CreateBatShim(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leaf-cli-runner-test-{Guid.NewGuid():N}.bat");
        File.WriteAllText(path, "@echo off\r\n" + body + "\r\n", new UTF8Encoding(false));
        return path;
    }
}
