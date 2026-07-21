using System.Diagnostics;
using System.IO;
using System.Text;

namespace Leaf.Services;

/// <summary>
/// Safe git CLI execution using ProcessStartInfo.ArgumentList.
/// No string escaping needed - .NET handles platform-specific argument passing.
/// </summary>
public class GitCommandRunner : IGitCommandRunner
{
    // Git emits UTF-8 by default on all platforms. The .NET default for
    // ProcessStartInfo.StandardOutputEncoding is the console's code page
    // (typically Windows-1252 on en-US) which silently corrupts non-ASCII
    // output — see the merge engine tests for a concrete failure case.
    // Explicit UTF-8 (no BOM) matches Git's wire format exactly.
    private static readonly Encoding GitOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public event EventHandler<GitCommandEventArgs>? CommandExecuted;

    /// <inheritdoc />
    public Task<GitCommandResult> RunAsync(
        string workingDirectory,
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(workingDirectory, command.ToArguments(), null, null, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? input = null,
        string? credentialKey = null,
        IReadOnlyDictionary<string, string>? extraEnvironment = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // ALWAYS redirect stdin, even with no input to write — the pipe
            // is closed right after start. Inheriting the parent's stdin is
            // never what a background git wants: under the GUI it's a dead
            // handle, and under a stdio host (Leaf.Mcp) it's the JSON-RPC
            // channel itself, where inheriting it both lets a stray
            // stdin-reading git steal protocol bytes and deadlocks git's
            // msys startup against the host's concurrent pipe reader.
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = GitOutputEncoding,
            StandardErrorEncoding = GitOutputEncoding,
        };

        // CRITICAL: Prevent git from hanging on credential prompts in background
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";  // Git Credential Manager

        // Force English/POSIX locale so any callers that parse git's
        // human-readable output ("Bisecting: N revisions left … (roughly K
        // steps)", "<sha> is the first bad commit", "Your branch is ahead
        // of …") aren't fooled by translated strings on a French / Japanese
        // / etc. machine. Matches what GitCliHelpers already does for its
        // synchronous spawns. Caller can override via extraEnvironment if
        // they specifically need the user's locale.
        startInfo.Environment["LC_ALL"] = "C";

        // When a credential key is supplied, route git through Leaf.AskPass.exe
        // instead of embedding the PAT in the URL or relying on GCM. Only the
        // key (not the PAT) is exposed via environment variables.
        if (!string.IsNullOrEmpty(credentialKey))
        {
            var askPass = AskPassPathResolver.ExecutablePath;
            if (!string.IsNullOrEmpty(askPass))
            {
                startInfo.Environment["GIT_ASKPASS"] = askPass;
                startInfo.Environment["LEAF_CREDENTIAL_KEY"] = credentialKey;
            }
        }

        // Caller-supplied environment overrides take precedence so the
        // interactive-rebase plumbing can plant GIT_SEQUENCE_EDITOR /
        // GIT_EDITOR alongside the LEAF_REBASE_* contract that the helper
        // exe reads. Previously-set keys (GIT_TERMINAL_PROMPT, GIT_ASKPASS)
        // are intentionally overridable — caller is closer to the use case.
        if (extraEnvironment != null)
        {
            foreach (var kvp in extraEnvironment)
            {
                startInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        // Use ArgumentList - NO string escaping needed!
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Log.Info("Git", $"Running: git {string.Join(" ", arguments)}");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // CRITICAL: Register cancellation to KILL the process
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    Log.Warn("Git", "Killing git process due to cancellation");
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited - ignore
            }
        });

        // Read output asynchronously (must continue even after cancellation to drain)
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        if (input != null)
        {
            await process.StandardInput.WriteAsync(input);
        }
        // Closed unconditionally: with no input this hands git an already-
        // EOF'd stdin instead of the parent's handle (see startInfo note).
        process.StandardInput.Close();

        // Wait for exit with cancellation
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Process was killed - drain remaining output then rethrow
            try
            {
                // Use short timeout for draining after cancellation
                using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await Task.WhenAll(
                    outputTask.WaitAsync(drainCts.Token),
                    errorTask.WaitAsync(drainCts.Token));
            }
            catch (Exception drainEx) when (drainEx is OperationCanceledException
                                         or TimeoutException
                                         or InvalidOperationException
                                         or IOException
                                         or AggregateException)
            {
                // Drain hit the 1s ceiling or streams were torn down — cancellation
                // is still the outcome the caller needs to see.
                Log.Info("GitRunner", $"Post-cancel drain failed: {drainEx.GetType().Name}: {drainEx.Message}");
            }
            throw;
        }

        var stdout = await outputTask;
        var stderr = await errorTask;

        var result = new GitCommandResult(
            process.ExitCode,
            stdout,
            stderr,
            process.ExitCode == 0);

        // Only log failures with actual error content (not expected failures like missing config keys)
        if (!result.Success && !string.IsNullOrWhiteSpace(result.StandardError))
        {
            Log.Error("Git", $"Command failed (exit code {result.ExitCode}): {result.StandardError}");
        }

        // Notify observers (merge editor's command log, terminal pane) AFTER
        // the result is fully assembled so handlers see consistent state.
        // Cancellation paths threw above and never reach here, which is the
        // intended behaviour — cancelled commands have no result to report.
        // Subscribers run synchronously on this thread; UI-touching handlers
        // dispatcher-hop themselves (see IGitCommandRunner.CommandExecuted docs).
        try
        {
            CommandExecuted?.Invoke(this, new GitCommandEventArgs(
                workingDirectory,
                string.Join(" ", arguments),
                result.ExitCode,
                result.StandardOutput,
                result.StandardError));
        }
        catch (Exception ex)
        {
            // A faulty observer must not break the git call's return path.
            // The result is what the caller actually awaited; an event-side
            // exception is purely diagnostic.
            Log.Warn("Git", $"CommandExecuted observer threw: {ex.GetType().Name}: {ex.Message}");
        }

        return result;
    }
}
