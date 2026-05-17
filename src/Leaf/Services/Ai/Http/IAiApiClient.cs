#nullable enable
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// Direct-billing HTTP transport for an AI provider — the API-key
/// counterpart to <see cref="Leaf.Services.Ai.Adapters.IAiCliAdapter"/>.
/// Implementations talk to the provider's REST endpoint directly so the
/// user pays via their own API key instead of going through a locally
/// installed CLI's subscription / OAuth session.
/// </summary>
/// <remarks>
/// <para>
/// The contract is deliberately narrow: take a prompt and a JSON schema,
/// return the inner JSON string the model produced. Envelope unwrap is
/// the client's responsibility (Anthropic returns tool-use blocks,
/// Google wraps in candidates, OpenAI in choices) so the downstream
/// <see cref="AiResolutionParser"/> sees identical bytes regardless of
/// provider — same architectural promise as the CLI adapter layer.
/// </para>
/// <para>
/// Per <c>feedback_ai_via_mcp.md</c>: no provider SDK linkage. Raw
/// <see cref="System.Net.Http.HttpClient"/> only. The key is read from
/// <c>CredentialService</c> on each instance construction and cached
/// for the lifetime of the singleton; <see cref="RefreshKey"/> lets
/// the Settings UI invalidate the cache after the user updates or
/// disconnects without a DI rebuild.
/// </para>
/// </remarks>
public interface IAiApiClient
{
    /// <summary>Which provider this client targets — matches the merge assistant's <see cref="AiProviderKind"/>.</summary>
    AiProviderKind Provider { get; }

    /// <summary>
    /// Whether the client currently has a usable API key. Read on every
    /// merge request through the corresponding assistant's
    /// <c>IsProviderConnected()</c> check — a true return here does not
    /// mean the key is valid, just present.
    /// </summary>
    bool HasKey { get; }

    /// <summary>
    /// Send <paramref name="prompt"/> to the provider with the given
    /// <paramref name="jsonSchema"/> constraining the response shape.
    /// Returns the inner JSON object as a string, matching the contract
    /// of the CLI adapter's <c>ExtractStructuredOutput</c>.
    /// Throws <see cref="AiMergeAssistantException"/> on transport
    /// failures (non-2xx, network error, malformed wire response,
    /// timeout) so the router surfaces a uniform error to the UI.
    /// </summary>
    Task<string> SendAsync(
        string prompt,
        string jsonSchema,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invalidate the cached key. Called by the Settings UI after the
    /// user saves a new key or disconnects, so subsequent requests
    /// re-read from Credential Manager without restarting the app.
    /// </summary>
    void RefreshKey();

    /// <summary>
    /// Probe the provider with a minimal request to validate the key.
    /// Returns <c>null</c> on success, or a short human-readable error
    /// suitable for the Settings status line. Distinct from
    /// <see cref="SendAsync"/> because the merge prompt is too heavy
    /// for a connection test.
    /// </summary>
    Task<string?> TestConnectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fetch the list of model identifiers the current key has access
    /// to. Each provider has its own <c>/models</c>-style endpoint:
    /// <list type="bullet">
    ///   <item>Anthropic: <c>GET /v1/models</c></item>
    ///   <item>Google: <c>GET /v1beta/models</c></item>
    ///   <item>OpenAI: <c>GET /v1/models</c></item>
    ///   <item>OpenAI-Compatible: <c>GET {baseUrl}/models</c></item>
    /// </list>
    /// Implementations filter the response down to chat-capable models
    /// (Gemini's <c>supportedGenerationMethods</c>, OpenAI's chat-style
    /// id patterns) and strip any provider-specific prefix
    /// (<c>models/</c> on Gemini). Throws
    /// <see cref="AiMergeAssistantException"/> on transport failures so
    /// the Settings UI can fall back to its curated list.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
}
