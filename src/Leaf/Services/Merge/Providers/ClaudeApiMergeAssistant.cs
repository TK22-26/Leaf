#nullable enable
using Leaf.Services.Ai.Http;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// API-key (direct-billing) Claude merge assistant. Sibling of
/// <see cref="ClaudeMergeAssistant"/> — same prompt, same parser, same
/// schema — only the transport differs: HTTPS against
/// <c>api.anthropic.com</c> instead of shelling out to the
/// <c>claude</c> CLI.
/// </summary>
/// <remarks>
/// Connection state mirrors <c>AppSettings.IsClaudeApiConnected</c>,
/// which the Settings UI sets after a successful Test Connection.
/// We deliberately do NOT also gate on the CLI's
/// <c>IsClaudeConnected</c> — a user who only ever uses the API key
/// shouldn't be forced to install the CLI.
/// </remarks>
public sealed class ClaudeApiMergeAssistant : AiMergeAssistantBase
{
    private readonly IAiApiClient _client;
    private readonly Func<bool> _isConnectedProvider;

    public ClaudeApiMergeAssistant(
        IAiApiClient client,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<bool> isConnectedProvider)
        : base(enabledProvider, consentProvider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _isConnectedProvider = isConnectedProvider ?? throw new ArgumentNullException(nameof(isConnectedProvider));
    }

    public override AiProviderKind ProviderKind => AiProviderKind.ClaudeApi;

    public override string ProviderDescription => "Claude (API)";

    protected override bool IsProviderConnected()
        => _isConnectedProvider() && _client.HasKey;

    protected override Task<string> ExecutePromptAsync(string prompt, CancellationToken cancellationToken)
        => _client.SendAsync(prompt, AiMergePromptBuilder.ResolutionJsonSchema, cancellationToken);
}
