#nullable enable
using Leaf.Services.Merge.Providers;

namespace Leaf.Services.Merge;

/// <summary>
/// The DI-registered <see cref="IAiMergeAssistant"/>. Holds one instance
/// of every provider implementation and dispatches to the one currently
/// selected in <c>AppSettings.AiMergeProvider</c>. Re-reads the setting
/// on every call so a settings change takes effect on the next "Ask AI"
/// click without a DI rebuild or app restart.
/// </summary>
/// <remarks>
/// <para>
/// Inner providers are stateless wrappers around <c>Func&lt;T&gt;</c>
/// settings accessors, so cheap to keep alive together. The router
/// itself is also stateless (it owns no per-request state), so it can
/// safely be a singleton.
/// </para>
/// <para>
/// No silent fallback. If the selected provider is disconnected at
/// request time, <see cref="RequestResolutionAsync"/> throws an
/// <see cref="AiMergeAssistantException"/> that names the provider —
/// per the engineering-software policy in CLAUDE.md, surfacing a
/// clear error beats silently using a different model than the user
/// asked for.
/// </para>
/// </remarks>
public sealed class AiMergeAssistantRouter : IAiMergeAssistant
{
    private readonly Func<string> _selectedProviderProvider;
    private readonly IReadOnlyDictionary<AiProviderKind, IAiMergeAssistant> _providers;
    private readonly Func<bool> _enabledProvider;
    private readonly Func<bool> _consentProvider;

    public AiMergeAssistantRouter(
        Func<string> selectedProviderProvider,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        ClaudeMergeAssistant claude,
        GeminiMergeAssistant gemini,
        CodexMergeAssistant codex,
        OllamaMergeAssistant ollama,
        ExternalServerMergeAssistant externalServer)
    {
        _selectedProviderProvider = selectedProviderProvider ?? throw new ArgumentNullException(nameof(selectedProviderProvider));
        _enabledProvider = enabledProvider ?? throw new ArgumentNullException(nameof(enabledProvider));
        _consentProvider = consentProvider ?? throw new ArgumentNullException(nameof(consentProvider));

        _providers = new Dictionary<AiProviderKind, IAiMergeAssistant>
        {
            [AiProviderKind.Claude] = claude,
            [AiProviderKind.Gemini] = gemini,
            [AiProviderKind.Codex] = codex,
            [AiProviderKind.Ollama] = ollama,
            [AiProviderKind.ExternalServer] = externalServer,
        };
    }

    public AiProviderKind ProviderKind => ResolveSelectedSafe().ProviderKind;

    public string ProviderDescription => ResolveSelectedSafe().ProviderDescription;

    /// <summary>
    /// Globally enabled AND the currently selected provider is
    /// connected. The router does NOT defer to the inner provider's
    /// IsEnabled here — we want the UI to reflect "feature is on, but
    /// the chosen backend isn't ready" as a distinct state from
    /// "feature is off entirely". The inner provider's IsEnabled is
    /// still the source of truth at execution time.
    /// </summary>
    public bool IsEnabled => _enabledProvider() && ResolveSelectedSafe().IsEnabled;

    public bool IsConsentGiven => _consentProvider();

    public Task<AiResolution?> RequestResolutionAsync(
        AiResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var selected = ResolveSelected();

        // Surface a clear error when the selected provider can't run —
        // distinct from "feature is disabled / consent missing", which
        // returns null per the existing contract.
        if (!_enabledProvider())
        {
            // Globally disabled: silent null, same as before.
            return Task.FromResult<AiResolution?>(null);
        }
        if (!_consentProvider())
        {
            return Task.FromResult<AiResolution?>(null);
        }
        if (!selected.IsEnabled)
        {
            // Globally on, consent given, but the selected provider
            // isn't connected. Tell the user which one and how to fix it.
            throw new AiMergeAssistantException(
                $"{selected.ProviderDescription} is not connected. " +
                $"Reconnect it under Settings → AI, or pick a different provider " +
                $"under Settings → AI → Merge Assistant.");
        }

        return selected.RequestResolutionAsync(request, cancellationToken);
    }

    /// <summary>
    /// Resolve the currently configured provider, throwing on an
    /// unrecognised setting so the user sees the real cause instead of
    /// silently dispatching to a different model than they asked for.
    /// An empty / null setting maps to <see cref="AiProviderKind.ExternalServer"/>
    /// — that's the migration default for installs upgraded from before
    /// the router landed and matches the historical behaviour.
    /// </summary>
    private IAiMergeAssistant ResolveSelected()
    {
        var raw = _selectedProviderProvider()?.Trim();
        var kind = raw switch
        {
            null or "" => AiProviderKind.ExternalServer,
            string s when s.Equals("Claude", StringComparison.OrdinalIgnoreCase) => AiProviderKind.Claude,
            string s when s.Equals("Gemini", StringComparison.OrdinalIgnoreCase) => AiProviderKind.Gemini,
            string s when s.Equals("Codex", StringComparison.OrdinalIgnoreCase) => AiProviderKind.Codex,
            string s when s.Equals("Ollama", StringComparison.OrdinalIgnoreCase) => AiProviderKind.Ollama,
            string s when s.Equals("ExternalServer", StringComparison.OrdinalIgnoreCase)
                       || s.Equals("External", StringComparison.OrdinalIgnoreCase)
                => AiProviderKind.ExternalServer,
            _ => throw new AiMergeAssistantException(
                $"Unknown AI merge provider '{raw}' in settings. Expected one of: " +
                "Claude, Gemini, Codex, Ollama, ExternalServer."),
        };

        return _providers[kind];
    }

    /// <summary>
    /// Property-accessor variant of <see cref="ResolveSelected"/> that
    /// degrades to ExternalServer when the setting is corrupt instead of
    /// throwing. Property reads bind into Settings UI and consent dialogs
    /// — letting them throw would crash the surrounding view; instead the
    /// fail-loud behaviour is reserved for <see cref="RequestResolutionAsync"/>
    /// where the user actively asks for a resolution.
    /// </summary>
    private IAiMergeAssistant ResolveSelectedSafe()
    {
        try { return ResolveSelected(); }
        catch (AiMergeAssistantException) { return _providers[AiProviderKind.ExternalServer]; }
    }
}
