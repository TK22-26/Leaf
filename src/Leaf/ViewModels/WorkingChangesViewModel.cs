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

            var summary = await _gitService.GetStagedSummaryAsync(_repositoryPath);
            if (summary.Length > MaxSummaryChars)
            {
                ErrorMessage = $"Staged summary is too large to send ({summary.Length} chars).";
                Debug.WriteLine($"[WorkingChanges] AutoFill blocked: summary length {summary.Length} exceeds limit {MaxSummaryChars}.");
                return;
            }

            var prompt = BuildPrompt(_repositoryPath, summary);
            var timeoutSeconds = Math.Max(1, settings.AiCliTimeoutSeconds);

            Debug.WriteLine($"[WorkingChanges] AutoFill prompt length: {prompt.Length}, summary={summary.Length}, timeout={timeoutSeconds}s");
            Debug.WriteLine($"[WorkingChanges] AutoFill prompt:\n{prompt}");

            var (success, output, detail) = await RunAiPromptAsync(preferredProvider, prompt, timeoutSeconds);
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

    private static string BuildPrompt(string repoPath, string summary)
    {
        return
$@"You are creating a git commit message and description. You are in the repository '{repoPath}'.
Do not run any commands or tools. Use only the staged summary provided.
Do not include any tool output, analysis, or commentary.
Only consider staged changes when forming the commit message and description.

Return JSON with keys: commitMessage, description.
The commitMessage must be 72 characters or fewer. The description may be an empty string.
Do not include any additional text or formatting.

Staged summary:
{summary}";
    }

    private async Task<(bool success, string output, string detail)> RunAiPromptAsync(string provider, string prompt, int timeoutSeconds)
    {
        var (command, args) = BuildAiCommand(provider, prompt);
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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrWhiteSpace(combinedPath))
            {
                psi.Environment["PATH"] = combinedPath;
            }

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, string.Empty, "Failed to start AI process");
            }

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

    private static (string command, List<string> args) BuildAiCommand(string provider, string prompt)
    {
        if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
        {
            return ("claude", new List<string> { "-p", prompt });
        }

        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return ("gemini", new List<string> { "-p", prompt });
        }

        if (provider.Equals("Codex", StringComparison.OrdinalIgnoreCase))
        {
            return ("codex", new List<string> { "e", prompt });
        }

        return (string.Empty, []);
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
