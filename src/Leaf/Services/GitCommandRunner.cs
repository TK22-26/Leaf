using System.Diagnostics;

namespace Leaf.Services;

/// <summary>
/// Safe git CLI execution using ProcessStartInfo.ArgumentList.
/// No string escaping needed - .NET handles platform-specific argument passing.
/// </summary>
public class GitCommandRunner : IGitCommandRunner
{
    /// <inheritdoc />
    public Task<GitCommandResult> RunAsync(
        string workingDirectory,
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(workingDirectory, command.ToArguments(), null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? input = null,
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

        // Use ArgumentList - NO string escaping needed!
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Debug.WriteLine($"Running git command: git {string.Join(" ", arguments)}");

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // CRITICAL: Register cancellation to KILL the process
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    Debug.WriteLine("Killing git process due to cancellation");
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
            catch
            {
                // Ignore drain failures during cancellation
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
            Debug.WriteLine($"Git command failed (exit code {result.ExitCode}): {result.StandardError}");
        }

        return result;
    }
}
