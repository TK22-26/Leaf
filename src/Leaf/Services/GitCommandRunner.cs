using System.Diagnostics;
using System.IO;

namespace Leaf.Services;

/// <summary>
/// Safe git CLI execution using ProcessStartInfo.ArgumentList.
/// No string escaping needed - .NET handles platform-specific argument passing.
/// </summary>
public class GitCommandRunner : IGitCommandRunner
{
    private const string AskPassExecutable = "Leaf.AskPass.exe";

    /// <summary>
    /// Resolved path to Leaf.AskPass.exe, or null if the helper is missing.
    /// Evaluated once; a missing helper means credential-requiring commands
    /// will fall back to git's default credential flow (GCM).
    /// </summary>
    private static readonly Lazy<string?> _askPassPath = new(() =>
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, AskPassExecutable);
        if (File.Exists(candidate)) return candidate;
        Log.Warn("Git", $"{AskPassExecutable} not found at {candidate}; credential-requiring commands will fall back to Git Credential Manager.");
        return null;
    });

    /// <inheritdoc />
    public Task<GitCommandResult> RunAsync(
        string workingDirectory,
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(workingDirectory, command.ToArguments(), null, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? input = null,
        string? credentialKey = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input != null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // CRITICAL: Prevent git from hanging on credential prompts in background
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";  // Git Credential Manager

        // When a credential key is supplied, route git through Leaf.AskPass.exe
        // instead of embedding the PAT in the URL or relying on GCM. Only the
        // key (not the PAT) is exposed via environment variables.
        if (!string.IsNullOrEmpty(credentialKey))
        {
            var askPass = _askPassPath.Value;
            if (!string.IsNullOrEmpty(askPass))
            {
                startInfo.Environment["GIT_ASKPASS"] = askPass;
                startInfo.Environment["LEAF_CREDENTIAL_KEY"] = credentialKey;
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
            process.StandardInput.Close();
        }

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

        return result;
    }
}
