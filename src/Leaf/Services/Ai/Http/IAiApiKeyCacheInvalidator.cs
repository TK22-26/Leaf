#nullable enable
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// DI-owned service that invalidates the cached API key on every
/// matching <see cref="IAiApiClient"/> singleton whenever the user
/// changes a key through Settings. The previous design passed an
/// <c>Action&lt;string&gt;</c> callback through the SettingsDialog
/// constructor — that worked from <c>MainViewModel.OpenSettingsAsync</c>
/// but the secondary <c>CloneDialog → Settings</c> path didn't wire
/// it, leaving a stale-key bug behind. Moving the responsibility into
/// a service that <see cref="AiSettingsControl"/> resolves directly
/// from the root provider fixes that universally.
/// </summary>
public interface IAiApiKeyCacheInvalidator
{
    /// <summary>
    /// Invalidate the cached key on the client matching the given
    /// credential-provider name ("Claude" | "Gemini" | "OpenAI" |
    /// "OpenAiCompatible"). Silently no-ops for unknown names so a
    /// caller passing a typo can't crash the Settings UI.
    /// </summary>
    void Invalidate(string credentialProvider);
}

/// <summary>
/// Default implementation. Owns the <c>IEnumerable&lt;IAiApiClient&gt;</c>
/// registered in <c>ServiceRegistry</c> and dispatches by matching
/// <see cref="IAiApiClient.Provider"/>.
/// </summary>
public sealed class AiApiKeyCacheInvalidator : IAiApiKeyCacheInvalidator
{
    private readonly IReadOnlyList<IAiApiClient> _clients;

    public AiApiKeyCacheInvalidator(IEnumerable<IAiApiClient> clients)
    {
        _clients = (clients ?? throw new ArgumentNullException(nameof(clients))).ToList();
    }

    public void Invalidate(string credentialProvider)
    {
        var kind = credentialProvider switch
        {
            "Claude" => AiProviderKind.ClaudeApi,
            "Gemini" => AiProviderKind.GeminiApi,
            "OpenAI" => AiProviderKind.OpenAi,
            "OpenAiCompatible" => AiProviderKind.OpenAiCompatible,
            _ => (AiProviderKind?)null,
        };
        if (kind is null) return;
        foreach (var client in _clients)
        {
            if (client.Provider == kind)
            {
                client.RefreshKey();
                break;
            }
        }
    }
}
