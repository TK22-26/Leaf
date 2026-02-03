using System.Diagnostics;
using System.IO;

namespace Leaf.Services.Git.Core;

/// <summary>
/// Helper utilities for common git CLI operations.
/// Instance-based for testability; only pure functions are static.
/// </summary>
internal class GitCliHelpers
{
    private readonly IGitCommandRunner _runner;

    /// <summary>
    /// Constant used to identify temporary stashes created during smart pop operations.
    /// </summary>
    public const string TempStashMessage = "TEMP_LEAF_AUTOPOP";

    public GitCliHelpers(IGitCommandRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Result of a git command execution (internal record for static methods).
    /// </summary>
    public record GitResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// Run a synchronous git command.
    /// </summary>
    public static GitResult RunGit(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Force English output for consistent error message parsing
        startInfo.EnvironmentVariables["LC_ALL"] = "C";

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return new GitResult(-1, "", "Failed to start git process");
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitResult(process.ExitCode, output, error);
    }

    /// <summary>
    /// Run a git command with stdin input.
    /// </summary>
    public static GitResult RunGitWithInput(string workingDirectory, string arguments, string input)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Force English output for consistent error message parsing
        startInfo.EnvironmentVariables["LC_ALL"] = "C";

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return new GitResult(-1, "", "Failed to start git process");
        }

        // Write the input to stdin
        process.StandardInput.Write(input);
        process.StandardInput.Close();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
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

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
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
        var gitResult = RunGit(".", "--exec-path");
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
    /// </summary>
    public static bool HasUncommittedChanges(string repoPath)
    {
        var result = RunGit(repoPath, "status --porcelain");
        return !string.IsNullOrWhiteSpace(result.Output);
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
                Debug.WriteLine($"[CleanupRejectFiles] Deleted {rejFile}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CleanupRejectFiles] Error cleaning up .rej files: {ex.Message}");
        }
    }

    /// <summary>
    /// Get list of conflicting files from git.
    /// </summary>
    public static List<string> GetConflictFiles(string repoPath)
    {
        var result = RunGit(repoPath, "diff --name-only --diff-filter=U");
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
            var result = RunGit(repoPath, "diff --name-only --diff-filter=U");
            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
            {
                return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            }

            // Fallback: check git status --porcelain for 'U' markers
            result = RunGit(repoPath, "status --porcelain");
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
        catch
        {
            // Ignore errors
        }

        return 0;
    }

    /// <summary>
    /// Read content from a specific conflict stage.
    /// </summary>
    public static string ReadConflictStage(string repoPath, string filePath, int stage)
    {
        var result = RunGit(repoPath, $"show :{stage}:\"{filePath}\"");
        return result.ExitCode == 0 ? result.Output : string.Empty;
    }

    /// <summary>
    /// Get file content from a specific git ref.
    /// </summary>
    public static string GetRefFileContent(string repoPath, string refName, string filePath)
    {
        var result = RunGit(repoPath, $"show {refName}:\"{filePath}\"");
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

        var baseShaResult = RunGit(repoPath, "merge-base HEAD MERGE_HEAD");
        var baseSha = baseShaResult.ExitCode == 0 ? baseShaResult.Output.Trim() : string.Empty;
        var baseContent = string.IsNullOrEmpty(baseSha)
            ? string.Empty
            : GetRefFileContent(repoPath, baseSha, filePath);

        return (baseContent, oursContent, theirsContent);
    }
}
