using Leaf.Models;
using Leaf.Services.Ai;
using Leaf.Services.Ai.Adapters;
using Leaf.Services.Merge;

namespace Leaf.Services;

/// <summary>
/// Generates commit messages by routing a structured-output prompt
/// through whichever AI provider the user has connected (Claude / Gemini /
/// Codex CLIs, or Ollama HTTP). Provider-specific CLI argument
/// construction and output unwrapping live in <see cref="IAiCliAdapter"/>
/// implementations under <see cref="Services.Ai.Adapters"/>; the
/// transport (process spawning, PATH, batch wrapping, timeout) lives in
/// <see cref="IAiCliRunner"/>. This class owns prompt construction +
/// commit-message JSON parsing.
/// </summary>
public class AiCommitMessageService : IAiCommitMessageService
{
    private readonly SettingsService _settingsService;
    private readonly OllamaService _ollamaService;
    private readonly ICommitMessageParser _parser;
    private readonly IAiCliRunner _runner;
    private readonly IReadOnlyDictionary<AiProviderKind, IAiCliAdapter> _adapters;

    public AiCommitMessageService(
        SettingsService settingsService,
        OllamaService ollamaService,
        ICommitMessageParser parser,
        IAiCliRunner runner,
        IEnumerable<IAiCliAdapter> adapters)
    {
        _settingsService = settingsService;
        _ollamaService = ollamaService;
        _parser = parser;
        _runner = runner;
        _adapters = adapters.ToDictionary(a => a.Provider);
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

        Log.Info("AiCommit", $"Generating with provider={provider}, timeout={timeoutSeconds}s, promptLen={prompt.Length}");

        try
        {
            var (success, output, detail) = await RunAiPromptAsync(provider, prompt, timeoutSeconds, repoPath, cancellationToken);
            if (!success)
            {
                Log.Error("AiCommit", $"Provider failed: {detail}");
                return (null, null, $"AI request failed: {detail}");
            }

            Log.Info("AiCommit", $"Output length: {output.Length}");

            var (message, description, parseError) = _parser.Parse(output);
            if (parseError != null)
            {
                Log.Error("AiCommit", $"Parse error: {parseError}");
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
        // Ollama is HTTP, not CLI — handled directly via OllamaService.
        if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            var settings = _settingsService.LoadSettings();
            var (success, output, error) = await _ollamaService.GenerateAsync(
                settings.OllamaBaseUrl, settings.OllamaSelectedModel, prompt, timeoutSeconds);
            return (success, output, error ?? string.Empty);
        }

        if (!TryResolveAdapter(provider, out var adapter))
        {
            return (false, string.Empty, "Unknown AI provider");
        }

        var invocation = adapter.BuildInvocation(prompt, CommitMessageJsonSchema, repoPath);
        var result = await _runner.RunAsync(invocation, timeoutSeconds, cancellationToken);
        if (!result.Success)
        {
            return (false, string.Empty, result.Detail);
        }

        // Provider-specific envelope unwrap (Claude structured_output,
        // Codex JSONL agent_message, Gemini response field). The
        // commit-message JSON parser expects the inner shape.
        var unwrapped = adapter.ExtractStructuredOutput(result.Stdout);
        return (true, unwrapped, string.Empty);
    }

    /// <summary>
    /// JSON schema constraining the response shape for commit-message
    /// generation. Adapters that support schema-output (Claude, Codex)
    /// pass it to the CLI; Gemini ignores it (the prompt enforces the
    /// shape via instructions).
    /// </summary>
    private const string CommitMessageJsonSchema =
        """{"type":"object","properties":{"commitMessage":{"type":"string"},"description":{"type":"string"}},"required":["commitMessage","description"],"additionalProperties":false}""";

    private bool TryResolveAdapter(string providerName, out IAiCliAdapter adapter)
    {
        var kind = providerName.Trim() switch
        {
            string s when s.Equals("Claude", StringComparison.OrdinalIgnoreCase) => AiProviderKind.Claude,
            string s when s.Equals("Gemini", StringComparison.OrdinalIgnoreCase) => AiProviderKind.Gemini,
            string s when s.Equals("Codex", StringComparison.OrdinalIgnoreCase) => AiProviderKind.Codex,
            _ => (AiProviderKind?)null,
        } ?? AiProviderKind.ExternalServer;

        return _adapters.TryGetValue(kind, out adapter!);
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
}
