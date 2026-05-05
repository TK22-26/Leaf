#nullable enable
using Leaf.Services.Ai;
using Leaf.Services.Ai.Adapters;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// Routes merge-conflict resolution requests through the user's
/// installed <c>codex</c> CLI via <see cref="CodexCliAdapter"/>.
/// Connection-state mirrors <c>AppSettings.IsCodexConnected</c>.
/// </summary>
public sealed class CodexMergeAssistant : CliMergeAssistantBase
{
    private readonly Func<bool> _isCodexConnectedProvider;

    public CodexMergeAssistant(
        IAiCliRunner runner,
        CodexCliAdapter adapter,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<bool> isCodexConnectedProvider,
        Func<int> timeoutSecondsProvider)
        : base(runner, adapter, enabledProvider, consentProvider, timeoutSecondsProvider)
    {
        _isCodexConnectedProvider = isCodexConnectedProvider
            ?? throw new ArgumentNullException(nameof(isCodexConnectedProvider));
    }

    public override AiProviderKind ProviderKind => AiProviderKind.Codex;

    public override string ProviderDescription => "Codex CLI";

    protected override bool IsProviderConnected() => _isCodexConnectedProvider();
}
