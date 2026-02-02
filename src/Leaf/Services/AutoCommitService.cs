using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for headless auto-commit functionality via command-line.
/// Stages all changes, generates AI commit message, and commits.
/// </summary>
public class AutoCommitService
{
    private readonly GitService _gitService;
    private readonly SettingsService _settingsService;
    private readonly RepositoryManagementService _repositoryService;

    private const int MaxSummaryChars = 16000;
    private const int MaxMessageLength = 72;

    public AutoCommitService(
        GitService gitService,
        SettingsService settingsService,
        RepositoryManagementService repositoryService)
    {
        _gitService = gitService;
        _settingsService = settingsService;
        _repositoryService = repositoryService;
    }

    /// <summary>
    /// Performs an auto-commit on the specified repository.
    /// </summary>
    /// <param name="repoNameOrPath">Repository name or path to find</param>
    /// <returns>Tuple of success and message</returns>
    public async Task<(bool Success, string Message)> AutoCommitAsync(string repoNameOrPath)
    {
        // Load repositories
        await _repositoryService.LoadRepositoriesAsync();

        // Find repository by name or path
        var repo = FindRepository(repoNameOrPath);
        if (repo == null)
        {
            return (false, $"Repository '{repoNameOrPath}' not found. Add it to Leaf first.");
        }

        var repoPath = repo.Path;
        Console.WriteLine($"Found repository: {repo.Name} at {repoPath}");

        // Check for changes
        var workingChanges = await _gitService.GetWorkingChangesAsync(repoPath);
        if (!workingChanges.HasChanges)
        {
            return (false, "No changes to commit.");
        }

        Console.WriteLine($"Found {workingChanges.TotalChanges} changed files.");

        // Stage all changes
        Console.WriteLine("Staging all changes...");
        await _gitService.StageAllAsync(repoPath);

        // Verify staged changes
        var stagedChanges = await _gitService.GetWorkingChangesAsync(repoPath);
        if (!stagedChanges.HasStagedChanges)
        {
            return (false, "No files staged after staging all.");
        }

        Console.WriteLine($"Staged {stagedChanges.StagedFiles.Count} files.");

        // Check AI settings
        var settings = _settingsService.LoadSettings();
        var preferredProvider = settings.DefaultAiProvider?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preferredProvider))
        {
            return (false, "No AI provider configured. Set a preferred AI in Leaf Settings.");
        }

        if (!IsProviderConnected(preferredProvider, settings))
        {
            return (false, $"AI provider '{preferredProvider}' is not connected. Configure it in Leaf Settings.");
        }

        Console.WriteLine($"Using AI provider: {preferredProvider}");

        // Generate commit message
        Console.WriteLine("Generating commit message...");
        var (aiSuccess, commitMessage, description, aiError) = await GenerateCommitMessageAsync(repoPath, preferredProvider, settings);
        if (!aiSuccess)
        {
            return (false, $"AI commit message generation failed: {aiError}");
        }

        Console.WriteLine($"Generated message: {commitMessage}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            Console.WriteLine($"Generated description: {description}");
        }

        // Commit
        Console.WriteLine("Committing...");
        try
        {
            await _gitService.CommitAsync(repoPath, commitMessage, description);
            return (true, $"Successfully committed: {commitMessage}");
        }
        catch (Exception ex)
        {
            return (false, $"Commit failed: {ex.Message}");
        }
    }

    private RepositoryInfo? FindRepository(string nameOrPath)
    {
        // Try exact path match first
        var byPath = _repositoryService.FindRepository(nameOrPath);
        if (byPath != null)
            return byPath;

        // Try by name (case-insensitive)
        var allRepos = _repositoryService.RepositoryGroups
            .SelectMany(g => g.Repositories)
            .ToList();

        return allRepos.FirstOrDefault(r =>
            r.Name.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProviderConnected(string provider, AppSettings settings)
    {
        return provider.ToLowerInvariant() switch
        {
            "ollama" => !string.IsNullOrEmpty(settings.OllamaSelectedModel),
            "codex" => settings.IsCodexConnected,
            "claude" => settings.IsClaudeConnected,
            "gemini" => settings.IsGeminiConnected,
            _ => false
        };
    }

    private async Task<(bool Success, string Message, string Description, string Error)> GenerateCommitMessageAsync(
        string repoPath, string provider, AppSettings settings)
    {
        try
        {
            var isOllama = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
            var isCodex = provider.Equals("Codex", StringComparison.OrdinalIgnoreCase);
            var includeContext = !isCodex;

            string summary;
            if (includeContext)
            {
                summary = await _gitService.GetStagedSummaryAsync(repoPath);
                if (summary.Length > MaxSummaryChars)
                {
                    return (false, "", "", $"Staged changes too large ({summary.Length} chars). Max: {MaxSummaryChars}");
                }
            }
            else
            {
                summary = string.Empty;
            }

            var prompt = isOllama
                ? BuildOllamaPrompt(summary)
                : BuildPrompt(repoPath, summary, includeContext);

            var timeoutSeconds = Math.Max(1, settings.AiCliTimeoutSeconds);

            var (success, output, detail) = await RunAiPromptAsync(provider, prompt, timeoutSeconds, repoPath, settings);
            if (!success)
            {
                return (false, "", "", detail);
            }

            if (!TryParseCommitResult(output, out var message, out var description, out var parseError))
            {
                return (false, "", "", $"Failed to parse AI response: {parseError}");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return (false, "", "", "AI returned empty commit message.");
            }

            message = message.Trim();
            if (message.Length > MaxMessageLength)
            {
                message = message[..MaxMessageLength].TrimEnd();
            }

            return (true, message, description?.Trim() ?? "", "");
        }
        catch (Exception ex)
        {
            return (false, "", "", ex.Message);
        }
    }

    private static string BuildPrompt(string repoPath, string summary, bool includeContext)
    {
        var contextInstruction = includeContext
            ? "Do not run any commands or tools. Use only the staged summary provided."
            : "Run 'git diff --cached' to see the staged changes, then generate the commit message.";

        var contextBlock = includeContext
            ? $"\n\n<staged_summary>\n{summary}\n</staged_summary>"
            : string.Empty;

        return
$@"You are creating a git commit message and description. You are in the repository '{repoPath}'.
{contextInstruction}
Only consider staged changes when forming the commit message and description.

Return JSON with keys: commitMessage, description.
The commitMessage must be 72 characters or fewer.
The description should be 2-4 sentences explaining WHAT changed and WHY.{contextBlock}";
    }

    private static string BuildOllamaPrompt(string summary)
    {
        return
$@"Write a git commit message for these changes. Be concise and specific.

Staged changes:
{summary}

Rules:
- Keep the commit message under 72 characters
- Use imperative mood (""Add"", ""Fix"", ""Update"", not ""Added"", ""Fixed"")
- Focus on WHAT changed, not HOW
- Be specific about which components changed
- Add a brief description (2-4 sentences) explaining the changes

Examples of good commit messages:
- ""Add Ollama integration for local AI commit messages""
- ""Fix null reference in repository status refresh""
- ""Update GitFlow settings to use standard git config format""

Respond in this exact format:
Commit message: [your message here]
Description: [your description here]";
    }

    private async Task<(bool Success, string Output, string Detail)> RunAiPromptAsync(
        string provider, string prompt, int timeoutSeconds, string workingDir, AppSettings settings)
    {
        return provider.ToLowerInvariant() switch
        {
            "ollama" => await RunOllamaAsync(prompt, timeoutSeconds, settings),
            "codex" => await RunCodexAsync(prompt, timeoutSeconds, workingDir),
            "claude" => await RunClaudeAsync(prompt, timeoutSeconds, workingDir),
            _ => (false, "", $"Unknown AI provider: {provider}")
        };
    }

    private static async Task<(bool Success, string Output, string Detail)> RunOllamaAsync(
        string prompt, int timeoutSeconds, AppSettings settings)
    {
        var model = !string.IsNullOrEmpty(settings.OllamaSelectedModel) ? settings.OllamaSelectedModel : "llama3.2";
        var baseUrl = !string.IsNullOrEmpty(settings.OllamaBaseUrl) ? settings.OllamaBaseUrl : "http://localhost:11434";

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        var requestBody = new
        {
            model,
            prompt,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync($"{baseUrl}/api/generate", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (false, "", $"Ollama returned {response.StatusCode}: {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            if (doc.RootElement.TryGetProperty("response", out var responseProp))
            {
                return (true, responseProp.GetString() ?? "", "");
            }

            return (false, "", "Ollama response missing 'response' field.");
        }
        catch (TaskCanceledException)
        {
            return (false, "", $"Ollama request timed out after {timeoutSeconds}s.");
        }
        catch (Exception ex)
        {
            return (false, "", $"Ollama error: {ex.Message}");
        }
    }

    private static async Task<(bool Success, string Output, string Detail)> RunCodexAsync(
        string prompt, int timeoutSeconds, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "codex",
            Arguments = $"--approval-mode full-auto -q \"{EscapeArg(prompt)}\"",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return await RunProcessAsync(psi, timeoutSeconds);
    }

    private static async Task<(bool Success, string Output, string Detail)> RunClaudeAsync(
        string prompt, int timeoutSeconds, string workingDir)
    {
        var schema = """{"type":"object","properties":{"commitMessage":{"type":"string"},"description":{"type":"string"}},"required":["commitMessage","description"],"additionalProperties":false}""";

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = $"-p \"{EscapeArg(prompt)}\" --output-format json --output-schema '{schema}'",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return await RunProcessAsync(psi, timeoutSeconds);
    }

    private static async Task<(bool Success, string Output, string Detail)> RunProcessAsync(
        ProcessStartInfo psi, int timeoutSeconds)
    {
        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "", "Failed to start process.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var completed = await Task.WhenAny(
                process.WaitForExitAsync(),
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

            if (!process.HasExited)
            {
                process.Kill();
                return (false, "", $"Process timed out after {timeoutSeconds}s.");
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return (false, "", $"Process exited with code {process.ExitCode}: {error}");
            }

            return (true, output, "");
        }
        catch (Exception ex)
        {
            return (false, "", $"Process error: {ex.Message}");
        }
    }

    private static string EscapeArg(string arg)
    {
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static bool TryParseCommitResult(string response, out string message, out string description, out string error)
    {
        message = "";
        description = "";
        error = "";

        // Try JSON first
        if (TryParseJson(response, out message, out description))
        {
            error = "";
            return true;
        }

        // Try labeled format
        if (TryParseLabeledFormat(response, out message, out description))
        {
            error = "";
            return true;
        }

        error = "Could not parse AI response as JSON or labeled format.";
        return false;
    }

    private static bool TryParseJson(string response, out string message, out string description)
    {
        message = "";
        description = "";

        try
        {
            // Find JSON in response
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end < start)
                return false;

            var json = response.Substring(start, end - start + 1);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("commitMessage", out var msgProp))
            {
                message = msgProp.GetString() ?? "";
            }
            else if (doc.RootElement.TryGetProperty("commit_message", out var msg2Prop))
            {
                message = msg2Prop.GetString() ?? "";
            }

            if (doc.RootElement.TryGetProperty("description", out var descProp))
            {
                description = descProp.GetString() ?? "";
            }

            return !string.IsNullOrWhiteSpace(message);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseLabeledFormat(string response, out string message, out string description)
    {
        message = "";
        description = "";

        var lines = response.Split('\n').Select(l => l.Trim()).ToList();

        var commitIndex = lines.FindIndex(line =>
            line.StartsWith("Commit message:", StringComparison.OrdinalIgnoreCase));

        if (commitIndex >= 0 && commitIndex < lines.Count - 1)
        {
            var msgLine = lines[commitIndex];
            var colonIndex = msgLine.IndexOf(':');
            if (colonIndex >= 0 && colonIndex < msgLine.Length - 1)
            {
                message = msgLine[(colonIndex + 1)..].Trim();
            }
            else if (commitIndex + 1 < lines.Count)
            {
                message = lines[commitIndex + 1].Trim();
            }
        }

        var descIndex = lines.FindIndex(line =>
            line.StartsWith("Description:", StringComparison.OrdinalIgnoreCase));

        if (descIndex >= 0)
        {
            var descLine = lines[descIndex];
            var colonIndex = descLine.IndexOf(':');
            if (colonIndex >= 0 && colonIndex < descLine.Length - 1)
            {
                description = descLine[(colonIndex + 1)..].Trim();
            }
            else if (descIndex + 1 < lines.Count)
            {
                var descLines = new List<string>();
                for (int i = descIndex + 1; i < lines.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        break;
                    descLines.Add(lines[i]);
                }
                description = string.Join(" ", descLines);
            }
        }

        return !string.IsNullOrWhiteSpace(message);
    }
}
