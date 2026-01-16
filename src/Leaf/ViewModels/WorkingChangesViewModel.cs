using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the working changes staging area view.
/// Handles staging, unstaging, discarding, and committing files.
/// </summary>
public partial class WorkingChangesViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly SettingsService _settingsService;
    private string? _repositoryPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(FileChangesSummary))]
    private WorkingChangesInfo? _workingChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingChars))]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private string _commitDescription = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Maximum characters for commit message.
    /// </summary>
    public const int MaxMessageLength = 72;
    private const int MaxSummaryChars = 400000;

    /// <summary>
    /// Remaining characters for commit message.
    /// </summary>
    public int RemainingChars => MaxMessageLength - CommitMessage.Length;

    /// <summary>
    /// True if there are any working changes.
    /// </summary>
    public bool HasChanges => WorkingChanges?.HasChanges ?? false;

    /// <summary>
    /// True if can commit (has staged files and non-empty message).
    /// </summary>
    public bool CanCommit =>
        WorkingChanges?.HasStagedChanges == true &&
        !string.IsNullOrWhiteSpace(CommitMessage) &&
        CommitMessage.Length <= MaxMessageLength;

    /// <summary>
    /// Summary of file changes for display.
    /// </summary>
    public string FileChangesSummary
    {
        get
        {
            if (WorkingChanges == null)
                return "No changes";

            var total = WorkingChanges.TotalChanges;
            var branch = WorkingChanges.BranchName;

            return total switch
            {
                0 => "No changes",
                1 => $"1 file change on {branch}",
                _ => $"{total} file changes on {branch}"
            };
        }
    }

    public WorkingChangesViewModel(IGitService gitService, SettingsService settingsService)
    {
        _gitService = gitService;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Set the repository path and refresh working changes.
    /// </summary>
    public async Task SetRepositoryAsync(string? repoPath)
    {
        _repositoryPath = repoPath;
        await RefreshAsync();
    }

    /// <summary>
    /// Set the working changes directly (synced from GitGraphViewModel).
    /// </summary>
    public void SetWorkingChanges(string repoPath, WorkingChangesInfo? workingChanges)
    {
        _repositoryPath = repoPath;
        WorkingChanges = workingChanges;

        // Debug: show what data we received
        if (workingChanges == null)
        {
            ErrorMessage = "SetWorkingChanges: null data received";
        }
        else
        {
            ErrorMessage = $"Synced: {workingChanges.UnstagedFiles.Count} unstaged, {workingChanges.StagedFiles.Count} staged";
        }

        // Force notification for dependent properties
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(FileChangesSummary));
    }

    /// <summary>
    /// Refresh working changes from the repository.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            WorkingChanges = null;
            ErrorMessage = "No repository path set";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            ErrorMessage = $"Loaded: {WorkingChanges?.TotalChanges ?? 0} changes";
        }
        catch (Exception ex)
        {
            WorkingChanges = null;
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Stage a single file.
    /// </summary>
    [RelayCommand]
    public async Task StageFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitService.StageFileAsync(_repositoryPath, file.Path);
            // Refresh and notify - don't use event to avoid triggering full graph reload
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage a single file.
    /// </summary>
    [RelayCommand]
    public async Task UnstageFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await _gitService.UnstageFileAsync(_repositoryPath, file.Path);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Stage all modified files.
    /// </summary>
    [RelayCommand]
    public async Task StageAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        try
        {
            await _gitService.StageAllAsync(_repositoryPath);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stage all failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Unstage all staged files.
    /// </summary>
    [RelayCommand]
    public async Task UnstageAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        try
        {
            await _gitService.UnstageAllAsync(_repositoryPath);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unstage all failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Discard changes for a single file.
    /// </summary>
    [RelayCommand]
    public async Task DiscardFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var result = MessageBox.Show(
            $"Are you sure you want to discard changes to '{file.FileName}'?\n\nThis cannot be undone.",
            "Discard Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _gitService.DiscardFileChangesAsync(_repositoryPath, file.Path);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add a specific file to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            await AddToGitignoreAsync(file.Path);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add all files with a specific extension to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreExtensionAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null || string.IsNullOrEmpty(file.Extension))
            return;

        try
        {
            await AddToGitignoreAsync($"*{file.Extension}");
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore extension failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Add all files in a specific directory to .gitignore.
    /// </summary>
    [RelayCommand]
    public async Task IgnoreDirectoryAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null || string.IsNullOrEmpty(file.Directory))
            return;

        try
        {
            // Add trailing slash for directory pattern
            await AddToGitignoreAsync($"{file.Directory}/");
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ignore directory failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Stash a single file.
    /// </summary>
    [RelayCommand]
    public async Task StashFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        try
        {
            // Stage the file first, then stash only staged changes
            await _gitService.StageFileAsync(_repositoryPath, file.Path);
            await _gitService.StashStagedAsync(_repositoryPath, $"Stash: {file.FileName}");
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Stash failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Open the file's containing folder in Windows Explorer and select the file.
    /// </summary>
    [RelayCommand]
    public void OpenInExplorer(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        // Normalize path separators (Git uses forward slashes)
        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.Combine(_repositoryPath, normalizedFilePath);

        if (File.Exists(fullPath))
        {
            // Open Explorer and select the file
            Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
        }
        else if (Directory.Exists(fullPath))
        {
            // Open the directory
            Process.Start("explorer.exe", $"\"{fullPath}\"");
        }
        else
        {
            // File doesn't exist (deleted), open the containing folder
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Process.Start("explorer.exe", $"\"{directory}\"");
            }
        }
    }

    /// <summary>
    /// Copy the file's full path to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyFilePath(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var normalizedFilePath = file.Path.Replace('/', '\\');
        var fullPath = Path.Combine(_repositoryPath, normalizedFilePath);
        Clipboard.SetText(fullPath);
    }

    /// <summary>
    /// Delete a file from the filesystem.
    /// </summary>
    [RelayCommand]
    public async Task DeleteFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete '{file.FileName}'?\n\nThis will permanently delete the file from disk and cannot be undone.",
            "Delete File",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var fullPath = Path.Combine(_repositoryPath, file.Path);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Delete failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Delete a Windows reserved filename (nul, con, prn, etc.) using admin privileges.
    /// These files cannot be deleted normally due to Windows restrictions.
    /// </summary>
    [RelayCommand]
    public async Task AdminDeleteReservedFileAsync(FileStatusInfo file)
    {
        if (string.IsNullOrEmpty(_repositoryPath) || file == null)
            return;

        var result = MessageBox.Show(
            $"Delete reserved file '{file.FileName}'?\n\nThis requires administrator privileges and will run a command to rename and delete the file.",
            "Admin Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var fullPath = Path.Combine(_repositoryPath, file.Path);
            var directory = Path.GetDirectoryName(fullPath) ?? _repositoryPath;
            var fileName = Path.GetFileName(fullPath);
            var tempName = $"_leaf_temp_{Guid.NewGuid():N}.tmp";

            // Build the batch script to rename and delete the reserved file
            // Uses \\?\ prefix to bypass Windows reserved name restrictions
            var script = $@"
@echo off
cd /d ""{directory}""
ren ""\\?\{fullPath}"" ""{tempName}""
del ""{Path.Combine(directory, tempName)}""
exit /b %errorlevel%
";

            var batchFile = Path.Combine(Path.GetTempPath(), $"leaf_admin_delete_{Guid.NewGuid():N}.bat");
            await File.WriteAllTextAsync(batchFile, script);

            // Run with admin privileges
            var startInfo = new ProcessStartInfo
            {
                FileName = batchFile,
                Verb = "runas", // Request admin elevation
                UseShellExecute = true,
                CreateNoWindow = false
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();

                // Clean up batch file
                try { File.Delete(batchFile); } catch { }

                if (process.ExitCode == 0)
                {
                    WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
                    OnPropertyChanged(nameof(HasChanges));
                    OnPropertyChanged(nameof(FileChangesSummary));
                }
                else
                {
                    ErrorMessage = $"Admin delete failed with exit code {process.ExitCode}";
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
            ErrorMessage = "Admin delete cancelled by user.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Admin delete failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Adds a pattern to the repository's .gitignore file.
    /// </summary>
    private async Task AddToGitignoreAsync(string pattern)
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        var gitignorePath = Path.Combine(_repositoryPath, ".gitignore");

        await Task.Run(() =>
        {
            var lines = File.Exists(gitignorePath)
                ? File.ReadAllLines(gitignorePath).ToList()
                : new List<string>();

            // Check if pattern already exists
            if (lines.Any(l => l.Trim().Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                return;

            // Add blank line if file doesn't end with one
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");

            lines.Add(pattern);
            File.WriteAllLines(gitignorePath, lines);
        });
    }

    /// <summary>
    /// Discard all working directory changes.
    /// </summary>
    [RelayCommand]
    public async Task DiscardAllAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
            return;

        // Show confirmation dialog
        var result = MessageBox.Show(
            "Are you sure you want to discard all changes? This cannot be undone.",
            "Discard All Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _gitService.DiscardAllChangesAsync(_repositoryPath);
            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discard failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Commit staged changes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCommit))]
    public async Task CommitAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath) || !CanCommit)
            return;

        try
        {
            IsLoading = true;

            var description = string.IsNullOrWhiteSpace(CommitDescription)
                ? null
                : CommitDescription.Trim();

            await _gitService.CommitAsync(_repositoryPath, CommitMessage.Trim(), description);

            // Clear form after successful commit
            CommitMessage = string.Empty;
            CommitDescription = string.Empty;

            WorkingChanges = await _gitService.GetWorkingChangesAsync(_repositoryPath);
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(FileChangesSummary));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Commit failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task AutoFillCommitMessageAsync()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            ErrorMessage = "No repository selected.";
            return;
        }

        var settings = _settingsService.LoadSettings();
        var preferredProvider = settings.DefaultAiProvider?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preferredProvider))
        {
            ErrorMessage = "Select a preferred AI in Settings before using Auto Fill.";
            Debug.WriteLine("[WorkingChanges] AutoFill blocked: no preferred AI.");
            return;
        }

        if (!IsProviderConnected(preferredProvider, settings))
        {
            ErrorMessage = $"Preferred AI ({preferredProvider}) is not connected.";
            Debug.WriteLine($"[WorkingChanges] AutoFill blocked: {preferredProvider} not connected.");
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            Debug.WriteLine($"[WorkingChanges] AutoFill start: repo={_repositoryPath}, provider={preferredProvider}");

            // Codex can access git directly, so we don't need to include the full diff
            var isCodex = preferredProvider.Equals("Codex", StringComparison.OrdinalIgnoreCase);
            var includeContext = !isCodex;

            string summary;
            if (includeContext)
            {
                summary = await _gitService.GetStagedSummaryAsync(_repositoryPath);
                if (summary.Length > MaxSummaryChars)
                {
                    ErrorMessage = $"Staged summary is too large to send ({summary.Length} chars).";
                    Debug.WriteLine($"[WorkingChanges] AutoFill blocked: summary length {summary.Length} exceeds limit {MaxSummaryChars}.");
                    return;
                }
            }
            else
            {
                // For Codex, just get basic info (no diff content)
                summary = string.Empty;
            }

            var prompt = BuildPrompt(_repositoryPath, summary, includeContext);
            var timeoutSeconds = Math.Max(1, settings.AiCliTimeoutSeconds);

            Debug.WriteLine($"[WorkingChanges] AutoFill prompt length: {prompt.Length}, summary={summary.Length}, timeout={timeoutSeconds}s, includeContext={includeContext}");
            Debug.WriteLine($"[WorkingChanges] AutoFill prompt:\n{prompt}");

            var (success, output, detail) = await RunAiPromptAsync(preferredProvider, prompt, timeoutSeconds, _repositoryPath);
            if (!success)
            {
                Debug.WriteLine($"[WorkingChanges] AutoFill failed: {detail}");
                ErrorMessage = $"AI request failed: {detail}";
                return;
            }

            Debug.WriteLine($"[WorkingChanges] AutoFill output length: {output.Length}");
            Debug.WriteLine($"[WorkingChanges] AutoFill output:\n{output}");

            if (!TryParseCommitResult(output, out var message, out var description, out var parseDetail))
            {
                Debug.WriteLine($"[WorkingChanges] AutoFill parse error: {parseDetail}");
                ErrorMessage = $"AI response invalid: {parseDetail}";
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                ErrorMessage = "AI returned an empty commit message.";
                return;
            }

            message = message.Trim();
            if (message.Length > MaxMessageLength)
            {
                message = message[..MaxMessageLength].TrimEnd();
                ErrorMessage = $"AI message trimmed to {MaxMessageLength} characters.";
            }

            CommitMessage = message;
            CommitDescription = description?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"AI commit failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnCommitMessageChanged(string value)
    {
        // Notify CanCommit changed when message changes
        CommitCommand.NotifyCanExecuteChanged();
    }

    private static string BuildPrompt(string repoPath, string summary, bool includeContext)
    {
        var contextInstruction = includeContext
            ? "Do not run any commands or tools. Use only the staged summary provided."
            : "Run 'git diff --cached' to see the staged changes, then generate the commit message.";

        var contextBlock = includeContext
            ? $"\n\nStaged summary:\n{summary}"
            : string.Empty;

        return
$@"You are creating a git commit message and description. You are in the repository '{repoPath}'.
{contextInstruction}
Do not include any tool output, analysis, or commentary.
Only consider staged changes when forming the commit message and description.

Return JSON with keys: commitMessage, description.
The commitMessage must be 72 characters or fewer.
The description should be a bullet-point list with each item on a new line, starting with '- '.
Example description format: ""- Added feature X\n- Fixed bug Y\n- Updated Z""
If there are no significant details, description may be an empty string.
Do not include any additional text or formatting outside the JSON.{contextBlock}";
    }

    private async Task<(bool success, string output, string detail)> RunAiPromptAsync(
        string provider, string prompt, int timeoutSeconds, string? repoPath = null)
    {
        var (command, args, useStdin, workingDirectory) = BuildAiCommand(provider, prompt, repoPath);
        if (string.IsNullOrWhiteSpace(command))
        {
            return (false, string.Empty, "Unknown AI provider");
        }

        var (resolvedPath, combinedPath) = ResolveCommandPath(command);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolvedPath ?? command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Set working directory if specified (allows AI to access git repo)
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            // Debug: log the command (truncate long prompts in log)
            var argsPreview = string.Join(" ", args.Select(a =>
                a.Length > 100 ? $"\"{a[..100]}...\"" : (a.Contains(' ') ? $"\"{a}\"" : a)));
            Debug.WriteLine($"[WorkingChanges] AI command: {psi.FileName} {argsPreview}");
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                Debug.WriteLine($"[WorkingChanges] Working directory: {workingDirectory}");
            }
            if (useStdin)
            {
                Debug.WriteLine($"[WorkingChanges] Sending {prompt.Length} chars via stdin");
            }

            if (!string.IsNullOrWhiteSpace(combinedPath))
            {
                psi.Environment["PATH"] = combinedPath;
            }

            // Set environment variables for non-interactive mode
            psi.Environment["CI"] = "true";
            psi.Environment["NO_COLOR"] = "1";
            psi.Environment["TERM"] = "dumb";

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, string.Empty, "Failed to start AI process");
            }

            // Send prompt via stdin if needed, then close stdin
            if (useStdin)
            {
                await process.StandardInput.WriteAsync(prompt);
            }
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var exitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(Math.Max(1, timeoutSeconds) * 1000);
            var completed = await Task.WhenAny(exitTask, timeoutTask);
            if (completed == timeoutTask)
            {
                try { process.Kill(); } catch { }
                return (false, string.Empty, $"timed out after {Math.Max(1, timeoutSeconds)}s");
            }

            var output = (await outputTask + await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(output)
                    ? $"exit {process.ExitCode}"
                    : $"exit {process.ExitCode}: {TrimDetail(output)}";
                return (false, string.Empty, detail);
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return (false, string.Empty, "no output");
            }

            // For Codex with --json flag, extract the agent_message from JSONL output
            if (provider.Equals("Codex", StringComparison.OrdinalIgnoreCase))
            {
                output = ExtractCodexJsonlMessage(output);
            }

            // For Claude with --output-format json, extract structured_output
            if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
            {
                output = ExtractClaudeStructuredOutput(output);
            }

            // For Gemini with --output-format json, extract response field
            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                output = ExtractGeminiResponse(output);
            }

            return (true, output, string.Empty);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, string.Empty, "command not found on PATH");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    private static string ExtractCodexJsonlMessage(string jsonlOutput)
    {
        // Parse JSONL output from Codex --json flag
        // Looking for: {"type":"item.completed","item":{"type":"agent_message","text":"..."}}
        foreach (var line in jsonlOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "item.completed" &&
                    root.TryGetProperty("item", out var itemEl) &&
                    itemEl.TryGetProperty("type", out var itemTypeEl) &&
                    itemTypeEl.GetString() == "agent_message" &&
                    itemEl.TryGetProperty("text", out var textEl))
                {
                    return textEl.GetString() ?? jsonlOutput;
                }
            }
            catch
            {
                // Skip invalid JSON lines
            }
        }

        // Fallback to original output if parsing fails
        return jsonlOutput;
    }

    private static string ExtractClaudeStructuredOutput(string jsonOutput)
    {
        // Parse JSON output from Claude --output-format json
        // Looking for: {"type":"result","structured_output":{"commitMessage":"...","description":"..."}}
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            if (root.TryGetProperty("structured_output", out var structuredEl))
            {
                // Return the structured_output as JSON string for our existing parser
                return structuredEl.GetRawText();
            }
        }
        catch
        {
            // Fall through to return original
        }

        // Fallback to original output if parsing fails
        return jsonOutput;
    }

    private static string ExtractGeminiResponse(string jsonOutput)
    {
        // Parse JSON output from Gemini --output-format json
        // Looking for: {"session_id":"...","response":"...","stats":{...}}
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            if (root.TryGetProperty("response", out var responseEl))
            {
                // Return the response text (may contain markdown code fences, existing parser handles that)
                return responseEl.GetString() ?? jsonOutput;
            }
        }
        catch
        {
            // Fall through to return original
        }

        // Fallback to original output if parsing fails
        return jsonOutput;
    }

    /// <summary>
    /// Builds the AI command with arguments.
    /// Returns: (command, args, useStdin, workingDirectory)
    /// - useStdin: if true, send prompt via stdin instead of command line
    /// - workingDirectory: if set, run the command in this directory (allows AI to access git)
    /// </summary>
    private static (string command, List<string> args, bool useStdin, string? workingDirectory) BuildAiCommand(
        string provider, string prompt, string? repoPath)
    {
        if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
        {
            // -p/--print is a FLAG for non-interactive mode, prompt is positional at end
            // --output-format json returns structured JSON
            // --json-schema takes the schema as a STRING (not a file path!)
            // Use stdin to avoid command line too long errors
            var schema = """{"type":"object","properties":{"commitMessage":{"type":"string"},"description":{"type":"string"}},"required":["commitMessage","description"],"additionalProperties":false}""";
            return ("claude", new List<string>
            {
                "-p",
                "--model", "sonnet",
                "--output-format", "json",
                "--json-schema", schema,
                "-"  // Read prompt from stdin
            }, useStdin: true, workingDirectory: null);
        }

        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            // Use stdin to avoid command line too long errors
            return ("gemini", new List<string> { "-p", "-", "--output-format", "json" },
                useStdin: true, workingDirectory: null);
        }

        if (provider.Equals("Codex", StringComparison.OrdinalIgnoreCase))
        {
            // Codex CLI uses: codex exec with --output-schema for guaranteed JSON format
            // Run in repo directory so Codex can access git directly
            // --full-auto enables non-interactive automatic execution
            // --color never disables terminal detection to prevent TTY issues
            // --json outputs structured JSONL for cleaner parsing
            var schemaPath = GetOrCreateCodexSchemaFile();
            return ("codex", new List<string>
            {
                "exec",
                "-m", "gpt-5.1-codex-mini",
                "--full-auto",
                "--color", "never",
                "--output-schema", schemaPath,
                "--json",
                prompt  // Codex prompt is short since it reads git directly
            }, useStdin: false, workingDirectory: repoPath);
        }

        return (string.Empty, [], false, null);
    }

    private static string? _codexSchemaPath;

    private static string GetOrCreateCodexSchemaFile()
    {
        if (_codexSchemaPath != null && File.Exists(_codexSchemaPath))
            return _codexSchemaPath;

        var schema = """
            {
                "type": "object",
                "properties": {
                    "commitMessage": { "type": "string" },
                    "description": { "type": "string" }
                },
                "required": ["commitMessage", "description"],
                "additionalProperties": false
            }
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "Leaf");
        Directory.CreateDirectory(tempDir);
        _codexSchemaPath = Path.Combine(tempDir, "commit-schema.json");
        File.WriteAllText(_codexSchemaPath, schema);
        return _codexSchemaPath;
    }

    private static bool IsProviderConnected(string provider, AppSettings settings)
    {
        if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
            return settings.IsClaudeConnected;
        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            return settings.IsGeminiConnected;
        if (provider.Equals("Codex", StringComparison.OrdinalIgnoreCase))
            return settings.IsCodexConnected;

        return false;
    }

    private static string TrimDetail(string detail)
    {
        var compact = detail.Replace("\r", " ").Replace("\n", " ");
        return compact.Length <= 140 ? compact : compact[..140] + "...";
    }

    private static bool TryParseCommitResult(string response, out string message, out string description, out string error)
    {
        if (TryParseCommitJson(response, out message, out description, out error))
        {
            return true;
        }

        if (TryParseLabeledOutput(response, out message, out description, out error))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseCommitJson(string response, out string message, out string description, out string error)
    {
        message = string.Empty;
        description = string.Empty;
        error = string.Empty;

        var json = ExtractJsonObject(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "no JSON object found";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "JSON root is not an object";
                return false;
            }

            if (doc.RootElement.TryGetProperty("commitMessage", out var commitMessageProp))
            {
                message = commitMessageProp.GetString() ?? string.Empty;
            }
            else if (doc.RootElement.TryGetProperty("message", out var messageProp))
            {
                message = messageProp.GetString() ?? string.Empty;
            }
            else if (doc.RootElement.TryGetProperty("commit", out var commitProp))
            {
                message = commitProp.GetString() ?? string.Empty;
            }

            if (doc.RootElement.TryGetProperty("description", out var descriptionProp))
            {
                description = descriptionProp.GetString() ?? string.Empty;
            }
            else if (doc.RootElement.TryGetProperty("body", out var bodyProp))
            {
                description = bodyProp.GetString() ?? string.Empty;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON parse error: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseLabeledOutput(string response, out string message, out string description, out string error)
    {
        message = string.Empty;
        description = string.Empty;
        error = string.Empty;

        var lines = response.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();

        int commitIndex = lines.FindIndex(line => line.TrimStart().StartsWith("Commit message:", StringComparison.OrdinalIgnoreCase));
        int descriptionIndex = lines.FindIndex(line => line.TrimStart().StartsWith("Description:", StringComparison.OrdinalIgnoreCase));

        if (commitIndex == -1 && descriptionIndex == -1)
        {
            error = "no JSON or labeled output found";
            return false;
        }

        if (commitIndex != -1)
        {
            var commitLine = lines[commitIndex];
            var commitValue = commitLine[(commitLine.IndexOf(':') + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(commitValue))
            {
                message = commitValue;
            }
            else
            {
                for (int i = commitIndex + 1; i < lines.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;
                    if (lines[i].TrimStart().StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                        break;

                    message = lines[i].Trim();
                    break;
                }
            }
        }

        if (descriptionIndex != -1)
        {
            var descriptionLine = lines[descriptionIndex];
            var descriptionValue = descriptionLine[(descriptionLine.IndexOf(':') + 1)..].Trim();

            var descriptionLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(descriptionValue))
            {
                descriptionLines.Add(descriptionValue);
            }

            for (int i = descriptionIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (IsMetadataLine(line))
                {
                    break;
                }
                descriptionLines.Add(line);
            }

            description = string.Join(Environment.NewLine, descriptionLines).TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            error = "commit message missing in labeled output";
            return false;
        }

        return true;
    }

    private static bool IsMetadataLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.TrimStart();
        return trimmed.StartsWith("OpenAI", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("codex", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("tokens", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("--------", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("workdir:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("model:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("provider:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("approval:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("sandbox:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("session", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("user", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("assistant", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("thinking", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("exec", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("[WorkingChanges]", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("Exception", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractJsonObject(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var cleaned = response.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = cleaned.Split('\n');
            var filtered = lines.Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal));
            cleaned = string.Join("\n", filtered);
        }

        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return cleaned.Substring(start, end - start + 1);
    }

    private static (string? fullPath, string? combinedPath) ResolveCommandPath(string command)
    {
        var paths = new List<string>();
        var processPath = Environment.GetEnvironmentVariable("PATH");
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);

        if (!string.IsNullOrWhiteSpace(processPath))
            paths.Add(processPath);
        if (!string.IsNullOrWhiteSpace(userPath))
            paths.Add(userPath);
        if (!string.IsNullOrWhiteSpace(machinePath))
            paths.Add(machinePath);

        var combinedPath = string.Join(";", paths);
        var searchPaths = combinedPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var extensions = Path.HasExtension(command) ? new[] { string.Empty } : new[] { ".exe", ".cmd", ".bat" };
        foreach (var dir in searchPaths)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                {
                    return (candidate, combinedPath);
                }
            }
        }

        return (null, combinedPath);
    }
}
