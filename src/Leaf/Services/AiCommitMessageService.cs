using System.Diagnostics;
using System.IO;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for generating commit messages using AI providers (Claude, Gemini, Codex, Ollama).
/// </summary>
public class AiCommitMessageService : IAiCommitMessageService
{
    private readonly SettingsService _settingsService;
    private readonly OllamaService _ollamaService;
    private readonly ICommitMessageParser _parser;

    private static string? _codexSchemaPath;

    public AiCommitMessageService(
        SettingsService settingsService,
        OllamaService ollamaService,
        ICommitMessageParser parser)
    {
        _settingsService = settingsService;
        _ollamaService = ollamaService;
        _parser = parser;
    }

    /// <inheritdoc/>
    public async Task<(string? message, string? description, string? error)> GenerateCommitMessageAsync(
        string diffText,
        string? repoPath = null,
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.LoadSettings();
        var provider = settings.DefaultAiProvider?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(provider))
        {
            return (null, null, "Select a preferred AI in Settings before using Auto Fill.");
        }

        if (!IsProviderConnected(provider, settings))
        {
            return (null, null, $"Preferred AI ({provider}) is not connected.");
        }

        var timeoutSeconds = Math.Max(1, settings.AiCliTimeoutSeconds);
        var isOllama = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
        var prompt = isOllama
            ? BuildOllamaPrompt(diffText)
            : BuildPrompt(repoPath ?? ".", diffText, includeContext: true);

        Debug.WriteLine($"[AiCommitService] Generating with provider={provider}, timeout={timeoutSeconds}s, promptLen={prompt.Length}");

        try
        {
            var (success, output, detail) = await RunAiPromptAsync(provider, prompt, timeoutSeconds, repoPath, cancellationToken);
            if (!success)
            {
                Debug.WriteLine($"[AiCommitService] Provider failed: {detail}");
                return (null, null, $"AI request failed: {detail}");
            }

            Debug.WriteLine($"[AiCommitService] Output length: {output.Length}");

            var (message, description, parseError) = _parser.Parse(output);
            if (parseError != null)
            {
                Debug.WriteLine($"[AiCommitService] Parse error: {parseError}");
                return (null, null, $"AI response invalid: {parseError}");
            }

            return (message, description, null);
        }
        catch (OperationCanceledException)
        {
            return (null, null, "AI generation cancelled.");
        }
        catch (Exception ex)
        {
            return (null, null, $"AI commit failed: {ex.Message}");
        }
    }

    private async Task<(bool success, string output, string detail)> RunAiPromptAsync(
        string provider, string prompt, int timeoutSeconds, string? repoPath, CancellationToken cancellationToken)
    {
        // Handle Ollama separately via HTTP API (not CLI)
        if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            var settings = _settingsService.LoadSettings();
            var (success, output, error) = await _ollamaService.GenerateAsync(
                settings.OllamaBaseUrl, settings.OllamaSelectedModel, prompt, timeoutSeconds);
            return (success, output, error ?? string.Empty);
        }

        var (command, args, useStdin, workingDirectory) = BuildAiCommand(provider, prompt, repoPath);
        if (string.IsNullOrWhiteSpace(command))
        {
            return (false, string.Empty, "Unknown AI provider");
        }

        var (resolvedPath, combinedPath) = ResolveCommandPath(command);
        var executablePath = resolvedPath ?? command;
        Debug.WriteLine($"[AiCommitService] Command '{command}' resolved to: {executablePath}");

        try
        {
            var psi = new ProcessStartInfo
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // On Windows, .cmd and .bat files must be run through cmd.exe /c with args as a single string
            var isBatchFile = executablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                              executablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            if (isBatchFile)
            {
                var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                psi.FileName = cmdPath;
                var escapedArgs = args.Select(a => a.Contains(' ') || a.Contains('"') ? $"\"{a.Replace("\"", "\\\"")}\"" : a);
                psi.Arguments = $"/c \"{executablePath}\" {string.Join(" ", escapedArgs)}";
                Debug.WriteLine($"[AiCommitService] Batch file detected, using: {cmdPath}");
            }
            else
            {
                psi.FileName = executablePath;
                foreach (var arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
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
            var timeoutTask = Task.Delay(timeoutSeconds * 1000, cancellationToken);

            var completed = await Task.WhenAny(exitTask, timeoutTask);
            if (completed == timeoutTask)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(); } catch { }
                    throw new OperationCanceledException(cancellationToken);
                }
                try { process.Kill(); } catch { }
                return (false, string.Empty, $"timed out after {timeoutSeconds}s");
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

            // Apply provider-specific output extraction
            if (provider.Equals("Codex", StringComparison.OrdinalIgnoreCase))
            {
                output = CommitMessageParser.ExtractCodexJsonlMessage(output);
            }

            if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
            {
                output = CommitMessageParser.ExtractClaudeStructuredOutput(output);
            }

            if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                output = CommitMessageParser.ExtractGeminiResponse(output);
            }

            return (true, output, string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Debug.WriteLine($"[AiCommitService] Win32Exception: {ex.Message} (NativeErrorCode: {ex.NativeErrorCode})");
            return (false, string.Empty, $"command error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
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

    private static string BuildOllamaPrompt(string summary)
    {
        return
$@"Write a git commit message for these changes. Be concise and specific.

RULES:
- Describe WHAT changed and WHY, not which files changed
- Do NOT list filenames
- Use imperative mood (Fix, Add, Update, Remove, Refactor)
- Keep the commit message under 72 characters

BAD examples (do not do this):
- ""Updated file1.cs, file2.cs, file3.cs""
- ""Modified SettingsDialog.xaml""
- ""Changes to multiple files""

GOOD examples:
- ""Fix tooltip not closing when mouse moves away""
- ""Add Ollama integration for local AI commit messages""
- ""Refactor service layer to use dependency injection""

Changes:
{summary}

Respond with ONLY this format:
Commit message: [your message here]
Description:
- [bullet point 1]
- [bullet point 2]";
    }

    /// <summary>
    /// Builds the AI command with arguments.
    /// Returns: (command, args, useStdin, workingDirectory)
    /// </summary>
    private static (string command, List<string> args, bool useStdin, string? workingDirectory) BuildAiCommand(
        string provider, string prompt, string? repoPath)
    {
        if (provider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
        {
            var schema = """{"type":"object","properties":{"commitMessage":{"type":"string"},"description":{"type":"string"}},"required":["commitMessage","description"],"additionalProperties":false}""";
            return ("claude", new List<string>
            {
                "-p",
                "--model", "sonnet",
                "--output-format", "json",
                "--json-schema", schema,
                "-"
            }, useStdin: true, workingDirectory: null);
        }

        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return ("gemini", new List<string> { "-p", "-", "--output-format", "json" },
                useStdin: true, workingDirectory: null);
        }

        if (provider.Equals("Codex", StringComparison.OrdinalIgnoreCase))
        {
            var schemaPath = GetOrCreateCodexSchemaFile();
            return ("codex", new List<string>
            {
                "exec",
                "-m", "gpt-5.1-codex-mini",
                "--full-auto",
                "--color", "never",
                "--output-schema", schemaPath,
                "--json",
                "-"
            }, useStdin: true, workingDirectory: repoPath);
        }

        return (string.Empty, [], false, null);
    }

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
        if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrEmpty(settings.OllamaSelectedModel);

        return false;
    }

    private static string TrimDetail(string detail)
    {
        var compact = detail.Replace("\r", " ").Replace("\n", " ");
        return compact.Length <= 140 ? compact : compact[..140] + "...";
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
