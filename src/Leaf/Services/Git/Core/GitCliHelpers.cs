using System.Diagnostics;
using System.IO;
using Leaf.Services;

namespace Leaf.Services.Git.Core;

/// <summary>
/// Helper utilities for common git CLI operations.
/// Instance-based for testability; only pure functions are static.
/// </summary>
internal class GitCliHelpers
{
    /// <summary>
    /// Constant used to identify temporary stashes created during smart pop operations.
    /// </summary>
    public const string TempStashMessage = "TEMP_LEAF_AUTOPOP";

    public GitCliHelpers(IGitCommandRunner runner)
    {
    }

    /// <summary>
    /// Result of a git command execution (internal record for static methods).
    /// </summary>
    public record GitResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// Run a synchronous git command with individually-escaped arguments.
    /// Uses ProcessStartInfo.ArgumentList so the OS handles quoting, which
    /// prevents shell-injection bugs when any argument carries user-supplied
    /// data (branch names, stash refs, paths, etc.).
    /// </summary>
    public static GitResult RunGitArgs(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        startInfo.EnvironmentVariables["LC_ALL"] = "C";

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return new GitResult(-1, "", "Failed to start git process");
        }

        // Read stderr on a separate thread to avoid deadlock when pipe buffers fill.
        // (ReadToEnd on stdout blocks until the process closes its stdout handle, but the
        // process may block writing to stderr if its pipe buffer is full and nobody is reading it.)
        string error = "";
        var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
        string output = process.StandardOutput.ReadToEnd();
        error = stderrTask.Result;
        process.WaitForExit();

        return new GitResult(process.ExitCode, output, error);
    }

    /// <summary>
    /// Run a git command with stdin input and individually-escaped arguments.
    /// </summary>
    public static GitResult RunGitWithInputArgs(string workingDirectory, string input, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        startInfo.EnvironmentVariables["LC_ALL"] = "C";

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return new GitResult(-1, "", "Failed to start git process");
        }

        process.StandardInput.Write(input);
        process.StandardInput.Close();

        // Read stderr on a separate thread to avoid deadlock when pipe buffers fill.
        // (ReadToEnd on stdout blocks until the process closes its stdout handle, but the
        // process may block writing to stderr if its pipe buffer is full and nobody is reading it.)
        string error = "";
        var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
        string output = process.StandardOutput.ReadToEnd();
        error = stderrTask.Result;
        process.WaitForExit();

        return new GitResult(process.ExitCode, output, error);
    }

    /// <summary>
    /// Run patch command with the given patch content.
    /// </summary>
    public static GitResult RunPatchWithInput(string workingDirectory, string patchContent)
    {
        string? patchPath = FindPatchExecutable();
        if (patchPath == null)
        {
            return new GitResult(-1, "",
                "Could not find patch.exe. Smart stash pop requires Git for Windows to be installed. " +
                "Download from https://git-scm.com/download/win");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = patchPath,
            Arguments = "-p1 --fuzz=3 --no-backup",
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return new GitResult(-1, "", "Failed to start patch process");
        }

        process.StandardInput.Write(patchContent);
        process.StandardInput.Close();

        // Read stderr on a separate thread to avoid deadlock when pipe buffers fill.
        // (ReadToEnd on stdout blocks until the process closes its stdout handle, but the
        // process may block writing to stderr if its pipe buffer is full and nobody is reading it.)
        string error = "";
        var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
        string output = process.StandardOutput.ReadToEnd();
        error = stderrTask.Result;
        process.WaitForExit();

        return new GitResult(process.ExitCode, output, error);
    }

    /// <summary>
    /// Find patch.exe from Git installation.
    /// </summary>
    public static string? FindPatchExecutable()
    {
        string[] possiblePaths =
        [
            @"C:\Program Files\Git\usr\bin\patch.exe",
            @"C:\Program Files (x86)\Git\usr\bin\patch.exe",
        ];

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Try to find git.exe and derive patch.exe location from it
        var gitResult = RunGitArgs(".", "--exec-path");
        if (gitResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(gitResult.Output))
        {
            var execPath = gitResult.Output.Trim().Replace('/', '\\');
            var gitRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(execPath)));
            if (gitRoot != null)
            {
                var patchPath = Path.Combine(gitRoot, "usr", "bin", "patch.exe");
                if (File.Exists(patchPath))
                    return patchPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Check if there are uncommitted changes.
    /// Uses git diff-index which exits immediately on first difference,
    /// much faster than 'git status --porcelain' on repos with many changes.
    /// </summary>
    public static bool HasUncommittedChanges(string repoPath)
    {
        // Fast: diff-index --quiet HEAD checks staged + unstaged tracked files.
        // Exits immediately on first difference (exit code 1 = dirty).
        var result = RunGitArgs(repoPath, "diff-index", "--quiet", "HEAD", "--");
        if (result.ExitCode == 1)
            return true;

        if (result.ExitCode != 0)
        {
            // No HEAD (empty repo) or other error — fall back to status
            var fallback = RunGitArgs(repoPath, "status", "--porcelain");
            return !string.IsNullOrWhiteSpace(fallback.Output);
        }

        // Tracked files are clean; check for untracked files
        var untracked = RunGitArgs(repoPath, "ls-files", "--others", "--exclude-standard");
        return !string.IsNullOrWhiteSpace(untracked.Output);
    }

    /// <summary>
    /// Clean up .rej files created by patch.
    /// </summary>
    public static void CleanupRejectFiles(string repoPath)
    {
        try
        {
            foreach (var rejFile in Directory.GetFiles(repoPath, "*.rej", SearchOption.AllDirectories))
            {
                File.Delete(rejFile);
                Log.Info("Git", $"CleanupRejectFiles: Deleted {rejFile}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Git", $"CleanupRejectFiles: Error cleaning up .rej files: {ex.Message}");
        }
    }

    /// <summary>
    /// Get list of conflicting files from git.
    /// </summary>
    public static List<string> GetConflictFiles(string repoPath)
    {
        var result = RunGitArgs(repoPath, "diff", "--name-only", "--diff-filter=U");
        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();
    }

    /// <summary>
    /// Get the count of conflicted files using git command line.
    /// </summary>
    public static int GetConflictCount(string repoPath)
    {
        try
        {
            // First try git diff --name-only --diff-filter=U
            var result = RunGitArgs(repoPath, "diff", "--name-only", "--diff-filter=U");
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
            {
                return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            }

            // Fallback: check git status --porcelain for 'U' markers
            result = RunGitArgs(repoPath, "status", "--porcelain");
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
            {
                int count = 0;
                foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length >= 2 && (line[0] == 'U' || line[1] == 'U'))
                    {
                        count++;
                    }
                }
                return count;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                or IOException
                                or UnauthorizedAccessException)
        {
            // Treat unreadable status as zero conflicts; caller will surface
            // any real merge-state error through normal channels.
            Log.Info("GitCli", $"GetConflictCount failed: {ex.GetType().Name}: {ex.Message}");
        }

        return 0;
    }

    /// <summary>
    /// Read content from a specific conflict stage.
    /// </summary>
    public static string ReadConflictStage(string repoPath, string filePath, int stage)
    {
        var result = RunGitArgs(repoPath, "show", $":{stage}:{filePath}");
        return result.ExitCode == 0 ? result.Output : string.Empty;
    }

    /// <summary>
    /// Get file content from a specific git ref.
    /// </summary>
    public static string GetRefFileContent(string repoPath, string refName, string filePath)
    {
        var result = RunGitArgs(repoPath, "show", $"{refName}:{filePath}");
        return result.ExitCode == 0 ? result.Output : string.Empty;
    }

    /// <summary>
    /// Get merge side contents for a file (base, ours, theirs).
    /// </summary>
    public static (string baseContent, string oursContent, string theirsContent) GetMergeSideContents(
        string repoPath, string filePath)
    {
        var oursContent = GetRefFileContent(repoPath, "HEAD", filePath);
        var theirsContent = GetRefFileContent(repoPath, "MERGE_HEAD", filePath);

        var baseShaResult = RunGitArgs(repoPath, "merge-base", "HEAD", "MERGE_HEAD");
        var baseSha = baseShaResult.ExitCode == 0 ? baseShaResult.Output.Trim() : string.Empty;
        var baseContent = string.IsNullOrEmpty(baseSha)
            ? string.Empty
            : GetRefFileContent(repoPath, baseSha, filePath);

        return (baseContent, oursContent, theirsContent);
    }
}
