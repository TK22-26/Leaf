#nullable enable
using Leaf.Services.Ai.Http;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// Direct-billing OpenAI merge assistant. Targets the canonical
/// <c>api.openai.com</c> Responses API. No CLI sibling — the existing
/// Codex CLI is a separate provider with its own auth flow; users who
/// prefer that path keep using <see cref="CodexMergeAssistant"/>.
/// </summary>
public sealed class OpenAiApiMergeAssistant : AiMergeAssistantBase
{
    private readonly IAiApiClient _client;
    private readonly Func<bool> _isConnectedProvider;

    public OpenAiApiMergeAssistant(
        IAiApiClient client,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<bool> isConnectedProvider)
        : base(enabledProvider, consentProvider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _isConnectedProvider = isConnectedProvider ?? throw new ArgumentNullException(nameof(isConnectedProvider));
    }

    public override AiProviderKind ProviderKind => AiProviderKind.OpenAi;

    public override string ProviderDescription => "OpenAI (API)";

    protected override bool IsProviderConnected()
        => _isConnectedProvider() && _client.HasKey;

    protected override Task<string> ExecutePromptAsync(string prompt, CancellationToken cancellationToken)
        => _client.SendAsync(prompt, AiMergePromptBuilder.ResolutionJsonSchema, cancellationToken);
}
