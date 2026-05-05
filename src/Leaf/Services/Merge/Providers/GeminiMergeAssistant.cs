#nullable enable
using Leaf.Services.Ai;
using Leaf.Services.Ai.Adapters;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// Routes merge-conflict resolution requests through the user's
/// installed <c>gemini</c> CLI via <see cref="GeminiCliAdapter"/>.
/// Connection-state mirrors <c>AppSettings.IsGeminiConnected</c>.
/// </summary>
public sealed class GeminiMergeAssistant : CliMergeAssistantBase
{
    private readonly Func<bool> _isGeminiConnectedProvider;

    public GeminiMergeAssistant(
        IAiCliRunner runner,
        GeminiCliAdapter adapter,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<bool> isGeminiConnectedProvider,
        Func<int> timeoutSecondsProvider)
        : base(runner, adapter, enabledProvider, consentProvider, timeoutSecondsProvider)
    {
        _isGeminiConnectedProvider = isGeminiConnectedProvider
            ?? throw new ArgumentNullException(nameof(isGeminiConnectedProvider));
    }

    public override AiProviderKind ProviderKind => AiProviderKind.Gemini;

    public override string ProviderDescription => "Gemini CLI";

    protected override bool IsProviderConnected() => _isGeminiConnectedProvider();
}
