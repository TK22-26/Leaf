#nullable enable

namespace Leaf.Services.Merge.Providers;

/// <summary>
/// Routes merge-conflict resolution requests through a local Ollama
/// instance via HTTP. Uses the existing <see cref="OllamaService"/>
/// transport so the model selection / base-URL config flows through the
/// same path as the commit-message feature.
/// </summary>
/// <remarks>
/// Connection-state mirrors the commit-message rule: connected ⇔
/// <c>OllamaSelectedModel</c> is non-empty. A blank model selection
/// counts as not-connected and surfaces a clear error in the router
/// rather than failing partway through a request.
/// </remarks>
public sealed class OllamaMergeAssistant : AiMergeAssistantBase
{
    private readonly OllamaService _ollama;
    private readonly Func<string> _baseUrlProvider;
    private readonly Func<string> _selectedModelProvider;
    private readonly Func<int> _timeoutSecondsProvider;

    public OllamaMergeAssistant(
        OllamaService ollama,
        Func<bool> enabledProvider,
        Func<bool> consentProvider,
        Func<string> baseUrlProvider,
        Func<string> selectedModelProvider,
        Func<int> timeoutSecondsProvider)
        : base(enabledProvider, consentProvider)
    {
        _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
        _baseUrlProvider = baseUrlProvider ?? throw new ArgumentNullException(nameof(baseUrlProvider));
        _selectedModelProvider = selectedModelProvider ?? throw new ArgumentNullException(nameof(selectedModelProvider));
        _timeoutSecondsProvider = timeoutSecondsProvider ?? throw new ArgumentNullException(nameof(timeoutSecondsProvider));
    }

    public override AiProviderKind ProviderKind => AiProviderKind.Ollama;

    public override string ProviderDescription
    {
        get
        {
            var model = _selectedModelProvider();
            var baseUrl = _baseUrlProvider();
            return string.IsNullOrEmpty(model)
                ? $"Ollama (no model selected, {baseUrl})"
                : $"Ollama ({model}, {baseUrl})";
        }
    }

    protected override bool IsProviderConnected()
        => !string.IsNullOrEmpty(_selectedModelProvider());

    protected override async Task<string> ExecutePromptAsync(string prompt, CancellationToken cancellationToken)
    {
        var baseUrl = _baseUrlProvider();
        var model = _selectedModelProvider();
        var timeoutSeconds = Math.Max(1, _timeoutSecondsProvider());

        var (success, output, error) = await _ollama
            .GenerateAsync(baseUrl, model, prompt, timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);

        if (!success)
        {
            throw new AiMergeAssistantException(
                $"Ollama: {error ?? "request failed"}");
        }

        return output;
    }
}
