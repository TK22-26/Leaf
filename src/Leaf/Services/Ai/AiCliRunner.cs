#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Leaf.Services.Ai;

/// <summary>
/// Default <see cref="IAiCliRunner"/> implementation. Runs an executable
/// via <see cref="Process"/>, pipes the prompt to stdin, captures
/// stdout / stderr concurrently to avoid pipe-buffer deadlocks, kills on
/// timeout / cancellation. Provider-agnostic — see <see cref="IAiCliRunner"/>.
/// </summary>
public sealed class AiCliRunner : IAiCliRunner
{
    public async Task<AiCliProcessResult> RunAsync(
        AiCliInvocation invocation,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        var (resolvedPath, combinedPath) = ResolveCommandPath(invocation.Executable);
        var executablePath = resolvedPath ?? invocation.Executable;

        var psi = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // .cmd / .bat aren't directly Process.Start-able on Windows: the
        // executable handed to CreateProcess must be a real PE. Wrap them
        // through cmd.exe /c "<bat>" arg arg, with each argument re-quoted
        // for cmd's parsing rules (which differ from CreateProcess's).
        var isBatchFile = executablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || executablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        if (isBatchFile)
        {
            var cmdPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");
            psi.FileName = cmdPath;
            var escaped = invocation.Arguments.Select(a =>
                a.Contains(' ') || a.Contains('"')
                    ? $"\"{a.Replace("\"", "\\\"")}\""
                    : a);
            psi.Arguments = $"/c \"{executablePath}\" {string.Join(" ", escaped)}";
        }
        else
        {
            psi.FileName = executablePath;
            foreach (var arg in invocation.Arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        if (!string.IsNullOrWhiteSpace(invocation.WorkingDirectory))
        {
            psi.WorkingDirectory = invocation.WorkingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(combinedPath))
        {
            // Some CLIs are installed at user-level paths that the parent
            // process doesn't see — passing the merged user+machine PATH
            // ensures resolution works regardless of how Leaf was launched.
            psi.Environment["PATH"] = combinedPath;
        }

        // Non-interactive hints. Most CLIs check these and skip ANSI
        // colours / progress bars / tutorial prompts, which would otherwise
        // mangle the JSON responses we expect on stdout.
        psi.Environment["CI"] = "true";
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["TERM"] = "dumb";

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            return new AiCliProcessResult(
                Success: false,
                ExitCode: -1,
                Stdout: string.Empty,
                Stderr: string.Empty,
                Detail: $"command error: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new AiCliProcessResult(false, -1, string.Empty, string.Empty,
                $"command error: {ex.Message}");
        }
        catch (PlatformNotSupportedException ex)
        {
            return new AiCliProcessResult(false, -1, string.Empty, string.Empty,
                $"command error: {ex.Message}");
        }

        if (process is null)
        {
            return new AiCliProcessResult(false, -1, string.Empty, string.Empty,
                $"failed to start '{invocation.Executable}'");
        }

        using var _proc = process;

        // Start reading stdout + stderr BEFORE writing stdin. A chatty CLI
        // can fill the (~4-64 KB) pipe buffer before reading our prompt;
        // if we wait to drain until after the write completes, the CLI
        // blocks on stdout and we block on stdin → deadlock. Mirrors the
        // GitCommandRunner pattern.
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        ObserveFaults(outputTask);
        ObserveFaults(errorTask);

        // Pipe-broken-mid-write is recoverable: the CLI may have exited
        // early on a fast error path (bad args, missing schema file). We
        // still want whatever stdout / stderr / exit code it produced —
        // those carry the actual diagnostic, not "pipe broke".
        if (!string.IsNullOrEmpty(invocation.Stdin))
        {
            try
            {
                await process.StandardInput.WriteAsync(invocation.Stdin).ConfigureAwait(false);
            }
            catch (IOException) { /* CLI exited before consuming stdin */ }
            catch (ObjectDisposedException) { /* stream torn down mid-write */ }
        }
        try { process.StandardInput.Close(); }
        catch (IOException) { /* same: tolerated */ }
        catch (ObjectDisposedException) { /* same: tolerated */ }

        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);

        Task completed;
        try
        {
            completed = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillSafely(process, "cancelled");
            throw;
        }

        if (completed == timeoutTask && !cancellationToken.IsCancellationRequested)
        {
            // Timed out under our own clock (not external cancellation).
            KillSafely(process, "timed out");
            return new AiCliProcessResult(false, -1, string.Empty, string.Empty,
                $"timed out after {timeoutSeconds}s");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            KillSafely(process, "cancelled");
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Process exited; collect its output.
        var stdout = await outputTask.ConfigureAwait(false);
        // Drain stderr unconditionally so an unobserved-task exception
        // can't surface at GC time. We already attached ObserveFaults
        // above for the case where awaiting throws.
        string stderr;
        try { stderr = await errorTask.ConfigureAwait(false); }
        catch { stderr = string.Empty; }

        if (process.ExitCode != 0)
        {
            var combined = (stdout + (string.IsNullOrEmpty(stderr) ? string.Empty : "\n" + stderr)).Trim();
            var detail = string.IsNullOrWhiteSpace(combined)
                ? $"exit {process.ExitCode}"
                : $"exit {process.ExitCode}: {TrimDetail(combined)}";
            return new AiCliProcessResult(false, process.ExitCode, stdout, stderr, detail);
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return new AiCliProcessResult(false, 0, string.Empty, stderr, "no output");
        }

        return new AiCliProcessResult(true, 0, stdout, stderr, string.Empty);
    }

    /// <summary>
    /// Try to kill <paramref name="process"/> without crashing if it has
    /// already exited between the timeout/cancel decision and this call.
    /// All the swallowed exception types map to "process was racing with us";
    /// anything else genuinely is a runtime problem and propagates.
    /// </summary>
    private static void KillSafely(Process process, string reason)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or Win32Exception
                                       or NotSupportedException
                                       or AggregateException)
        {
            Log.Info("AiCliRunner", $"Kill on {reason} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Observe a task's eventual exception so the runtime doesn't raise
    /// <see cref="TaskScheduler.UnobservedTaskException"/> if our main
    /// flow aborts before awaiting. Cheap, sync continuation.
    /// </summary>
    private static void ObserveFaults(Task task)
    {
        _ = task.ContinueWith(
            t => { _ = t.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static string TrimDetail(string detail)
    {
        var compact = detail.Replace("\r", " ").Replace("\n", " ");
        return compact.Length <= 140 ? compact : compact[..140] + "...";
    }

    /// <summary>
    /// Locate <paramref name="command"/> on PATH (process / user / machine
    /// scopes merged), trying <c>.exe</c> / <c>.cmd</c> / <c>.bat</c> when
    /// no explicit extension is supplied. Returns the resolved absolute
    /// path plus the merged PATH (which the caller injects into the spawn
    /// environment so the child inherits the same view).
    /// </summary>
    /// <remarks>
    /// Returning the merged PATH alongside the resolved exe matters because
    /// process-scope PATH is a snapshot from when Leaf launched, but newly
    /// installed CLIs only land in user-scope PATH. Without re-reading user
    /// + machine, a freshly-installed Claude wouldn't be discoverable until
    /// Leaf restarts.
    /// </remarks>
    private static (string? FullPath, string? CombinedPath) ResolveCommandPath(string command)
    {
        // Already absolute or relative-with-extension: trust the caller and
        // skip PATH resolution entirely.
        if (Path.IsPathRooted(command) && File.Exists(command))
        {
            return (command, null);
        }

        var paths = new List<string>();
        var processPath = Environment.GetEnvironmentVariable("PATH");
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);

        if (!string.IsNullOrWhiteSpace(processPath)) paths.Add(processPath);
        if (!string.IsNullOrWhiteSpace(userPath)) paths.Add(userPath);
        if (!string.IsNullOrWhiteSpace(machinePath)) paths.Add(machinePath);

        var combinedPath = string.Join(";", paths);
        var searchPaths = combinedPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var extensions = Path.HasExtension(command)
            ? new[] { string.Empty }
            : new[] { ".exe", ".cmd", ".bat" };

        foreach (var dir in searchPaths)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate)) return (candidate, combinedPath);
            }
        }

        return (null, combinedPath);
    }
}
