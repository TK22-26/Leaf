#nullable enable
using System.Diagnostics;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// Shared scaffolding for every <see cref="IAiMergeAssistant"/> that
/// drives a single provider (CLI or HTTP). Handles the gating, prompt
/// construction, response parsing, and outcome logging — derived
/// classes only supply the provider identity, connection-state check,
/// and the actual call into their backend.
/// </summary>
/// <remarks>
/// Centralising the common path here means error messages, telemetry,
/// privacy guarantees, and the "null = disabled / consent missing"
/// contract are identical across providers — fixing the architectural
/// promise that providers are interchangeable behind the interface.
/// </remarks>
public abstract class AiMergeAssistantBase : IAiMergeAssistant
{
    private readonly Func<bool> _enabledProvider;
    private readonly Func<bool> _consentProvider;

    protected AiMergeAssistantBase(Func<bool> enabledProvider, Func<bool> consentProvider)
    {
        _enabledProvider = enabledProvider ?? throw new ArgumentNullException(nameof(enabledProvider));
        _consentProvider = consentProvider ?? throw new ArgumentNullException(nameof(consentProvider));
    }

    public abstract AiProviderKind ProviderKind { get; }

    public abstract string ProviderDescription { get; }

    /// <summary>
    /// True when the user has both opted in globally AND this specific
    /// provider reports as connected (e.g. Claude CLI is logged in).
    /// Two-layer gate: a connected-but-globally-off provider stays off,
    /// and a globally-on but disconnected provider stays off.
    /// </summary>
    public bool IsEnabled => _enabledProvider() && IsProviderConnected();

    public bool IsConsentGiven => _consentProvider();

    /// <summary>
    /// Provider-specific connection check. Implementations consult
    /// <c>SettingsService</c> (e.g. <c>IsClaudeConnected</c>) or run a
    /// liveness probe against the backend.
    /// </summary>
    protected abstract bool IsProviderConnected();

    /// <summary>
    /// Send <paramref name="prompt"/> to the backend, return the raw
    /// response text. Implementations should NOT parse it; that's
    /// <see cref="AiResolutionParser"/>'s job. Throw
    /// <see cref="AiMergeAssistantException"/> on transport / connection
    /// failures so the caller sees a uniform exception type.
    /// </summary>
    protected abstract Task<string> ExecutePromptAsync(string prompt, CancellationToken cancellationToken);

    public async Task<AiResolution?> RequestResolutionAsync(
        AiResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Silent feature gate. The router treats null as "feature
        // unavailable" and routes the user to settings / consent UI as
        // appropriate. We deliberately don't throw here — disabled is
        // not an error.
        if (!IsEnabled) return null;
        if (!IsConsentGiven) return null;

        var sw = Stopwatch.StartNew();
        var outcome = "error";
        try
        {
            var prompt = AiMergePromptBuilder.BuildPrompt(request);
            var rawResponse = await ExecutePromptAsync(prompt, cancellationToken).ConfigureAwait(false);
            var resolution = AiResolutionParser.Parse(rawResponse);
            outcome = "success";
            return resolution;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            sw.Stop();
            // Privacy: log timing + outcome + provider only — never
            // request content, branch names, or response text. Matches
            // the existing AiCommit / AiMerge telemetry pattern.
            Log.Info("AiMerge",
                $"RequestResolution provider={ProviderKind} outcome={outcome} duration_ms={sw.ElapsedMilliseconds}");
        }
    }
}
