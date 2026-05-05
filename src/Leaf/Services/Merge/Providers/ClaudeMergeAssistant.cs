#nullable enable
using Leaf.Services.Ai;
using Leaf.Services.Ai.Adapters;

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// Routes merge-conflict resolution requests through the user's
/// installed <c>claude</c> CLI via <see cref="ClaudeCliAdapter"/>.
/// Connection-state mirrors <c>AppSettings.IsClaudeConnected</c>, the
/// same flag the commit-message service consults — so a user with
/// Claude commit messages working gets Claude merge resolution for free.
/// </summary>
public sealed class ClaudeMergeAssistant : CliMergeAssistantBase
{
    private readonly Func<bool> _isClaudeConnectedProvider;

    public ClaudeMergeAssistant(
        IAiCliRunner runner,
        ClaudeCliAdapter adapter,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<bool> isClaudeConnectedProvider,
        Func<int> timeoutSecondsProvider)
        : base(runner, adapter, enabledProvider, consentProvider, timeoutSecondsProvider)
    {
        _isClaudeConnectedProvider = isClaudeConnectedProvider
            ?? throw new ArgumentNullException(nameof(isClaudeConnectedProvider));
    }

    public override AiProviderKind ProviderKind => AiProviderKind.Claude;

    public override string ProviderDescription => "Claude CLI";

    protected override bool IsProviderConnected() => _isClaudeConnectedProvider();
}
