#nullable enable
using Leaf.Services.Ai;
using Leaf.Services.Ai.Adapters;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// Common shape for the three CLI-backed merge assistants (Claude /
/// Gemini / Codex). Owns the runner + adapter wiring + timeout config
/// so each concrete provider only has to declare its identity, its
/// connection-state check, and which adapter it uses.
/// </summary>
public abstract class CliMergeAssistantBase : AiMergeAssistantBase
{
    private readonly IAiCliRunner _runner;
    private readonly IAiCliAdapter _adapter;
    private readonly Func<int> _timeoutSecondsProvider;

    protected CliMergeAssistantBase(
        IAiCliRunner runner,
        IAiCliAdapter adapter,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<int> timeoutSecondsProvider)
        : base(enabledProvider, consentProvider)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _timeoutSecondsProvider = timeoutSecondsProvider ?? throw new ArgumentNullException(nameof(timeoutSecondsProvider));
    }

    protected sealed override async Task<string> ExecutePromptAsync(string prompt, CancellationToken cancellationToken)
    {
        var invocation = _adapter.BuildInvocation(
            prompt,
            AiMergePromptBuilder.ResolutionJsonSchema,
            repoPath: null);

        var timeoutSeconds = Math.Max(1, _timeoutSecondsProvider());
        var result = await _runner.RunAsync(invocation, timeoutSeconds, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            // Surface the runner's diagnostic verbatim — it already
            // distinguishes timeout / non-zero exit / missing executable.
            // Wrap it in the merge-assistant exception type so the VM's
            // AiError event fires (instead of the generic AsyncErrorHandler).
            throw new AiMergeAssistantException(
                $"{ProviderDescription}: {result.Detail}");
        }

        // Provider-specific envelope unwrap (Claude structured_output,
        // Codex JSONL agent_message, Gemini response field). The
        // resolution parser expects the inner JSON shape.
        return _adapter.ExtractStructuredOutput(result.Stdout);
    }
}
