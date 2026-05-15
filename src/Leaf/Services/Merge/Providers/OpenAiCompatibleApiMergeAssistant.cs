#nullable enable
using Leaf.Services.Ai.Http;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// OpenAI-API-compatible custom-endpoint merge assistant. Same wire
/// format as <see cref="OpenAiApiMergeAssistant"/> but the base URL is
/// user-supplied — meant for LM Studio, OpenRouter, vLLM, Together, a
/// corporate Azure OpenAI gateway, etc. The underlying
/// <see cref="OpenAiApiClient"/> is shared between both assistants;
/// only the <see cref="AiProviderKind"/> and the configured base URL
/// differ.
/// </summary>
public sealed class OpenAiCompatibleApiMergeAssistant : AiMergeAssistantBase
{
    private readonly IAiApiClient _client;
    private readonly Func<bool> _isConnectedProvider;
    private readonly Func<string> _baseUrlProvider;

    public OpenAiCompatibleApiMergeAssistant(
        IAiApiClient client,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<bool> isConnectedProvider,
        Func<string> baseUrlProvider)
        : base(enabledProvider, consentProvider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _isConnectedProvider = isConnectedProvider ?? throw new ArgumentNullException(nameof(isConnectedProvider));
        _baseUrlProvider = baseUrlProvider ?? throw new ArgumentNullException(nameof(baseUrlProvider));
    }

    public override AiProviderKind ProviderKind => AiProviderKind.OpenAiCompatible;

    public override string ProviderDescription
    {
        get
        {
            var url = _baseUrlProvider();
            return string.IsNullOrEmpty(url)
                ? "OpenAI-Compatible (no endpoint configured)"
                : $"OpenAI-Compatible ({url})";
        }
    }

    // Connection requires the user to have set a base URL — without
    // one the underlying client throws on every request. Treat that
    // as a not-connected condition so the dropdown shows the gate
    // honestly.
    protected override bool IsProviderConnected()
        => _isConnectedProvider() && _client.HasKey && !string.IsNullOrWhiteSpace(_baseUrlProvider());

    protected override Task<string> ExecutePromptAsync(string prompt, CancellationToken cancellationToken)
        => _client.SendAsync(prompt, AiMergePromptBuilder.ResolutionJsonSchema, cancellationToken);
}
