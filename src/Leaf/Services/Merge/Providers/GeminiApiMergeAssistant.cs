#nullable enable
using Leaf.Services.Ai.Http;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// API-key (direct-billing) Gemini merge assistant. Sibling of
/// <see cref="GeminiMergeAssistant"/> — same prompt, same parser, same
/// schema — only the transport differs: HTTPS against
/// <c>generativelanguage.googleapis.com</c> instead of shelling out to
/// the <c>gemini</c> CLI.
/// </summary>
public sealed class GeminiApiMergeAssistant : AiMergeAssistantBase
{
    private readonly IAiApiClient _client;
    private readonly Func<bool> _isConnectedProvider;

    public GeminiApiMergeAssistant(
        IAiApiClient client,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<bool> isConnectedProvider)
        : base(enabledProvider, consentProvider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _isConnectedProvider = isConnectedProvider ?? throw new ArgumentNullException(nameof(isConnectedProvider));
    }

    public override AiProviderKind ProviderKind => AiProviderKind.GeminiApi;

    public override string ProviderDescription => "Gemini (API)";

    protected override bool IsProviderConnected()
        => _isConnectedProvider() && _client.HasKey;

    protected override Task<string> ExecutePromptAsync(string prompt, CancellationToken cancellationToken)
        => _client.SendAsync(prompt, AiMergePromptBuilder.ResolutionJsonSchema, cancellationToken);
}
