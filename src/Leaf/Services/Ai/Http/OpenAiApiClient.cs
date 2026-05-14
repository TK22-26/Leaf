#nullable enable
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// HTTP client for OpenAI's Responses API and any
/// OpenAI-API-compatible endpoint (LM Studio, OpenRouter, vLLM,
/// Together, an Azure OpenAI gateway). Base URL is parameterised so a
/// single implementation serves both OpenAI proper (<see cref="AiProviderKind.OpenAi"/>)
/// and the user-supplied custom endpoint (<see cref="AiProviderKind.OpenAiCompatible"/>).
/// </summary>
/// <remarks>
/// <para>
/// Uses the modern Responses API endpoint (<c>POST /v1/responses</c>)
/// with <c>text.format = json_schema</c> for structured output. Chat
/// Completions is legacy on OpenAI; most compatible servers also
/// implement Responses now. If a server doesn't, the user can keep
/// using its own CLI through the Codex / External Server paths.
/// </para>
/// <para>
/// Auth header: <c>Authorization: Bearer {key}</c>.
/// </para>
/// </remarks>
public sealed class OpenAiApiClient : AiApiClientBase
{
    private readonly AiProviderKind _kind;
    private readonly string _label;
    private readonly Func<string> _baseUrlProvider;
    private readonly Func<string> _modelProvider;

    public OpenAiApiClient(
        AiProviderKind kind,
        string label,
        HttpClient httpClient,
        Func<string?> keyReader,
        Func<string> baseUrlProvider,
        Func<string> modelProvider,
        Func<int> timeoutSecondsProvider)
        : base(httpClient, keyReader, timeoutSecondsProvider)
    {
        if (kind != AiProviderKind.OpenAi && kind != AiProviderKind.OpenAiCompatible)
            throw new ArgumentException($"Unsupported kind for OpenAI client: {kind}", nameof(kind));
        _kind = kind;
        _label = label ?? throw new ArgumentNullException(nameof(label));
        _baseUrlProvider = baseUrlProvider ?? throw new ArgumentNullException(nameof(baseUrlProvider));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
    }

    public override AiProviderKind Provider => _kind;

    protected override string ProviderLabel => _label;

    protected override HttpRequestMessage BuildRequest(string prompt, string jsonSchema, string apiKey)
    {
        var model = _modelProvider();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: no model configured. Set one in Settings → AI.");
        }

        JsonNode schemaNode;
        try
        {
            schemaNode = JsonNode.Parse(jsonSchema)
                ?? throw new AiMergeAssistantException($"{ProviderLabel}: schema is null.");
        }
        catch (JsonException ex)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: invalid JSON schema ({ex.Message}).", ex);
        }

        // OpenAI's strict mode requires additionalProperties:false and
        // every property listed in required. Our shared schema already
        // satisfies both — leave it alone, just enforce strict.
        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = prompt,
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "merge_resolution",
                    ["strict"] = true,
                    ["schema"] = schemaNode,
                },
            },
        };

        var url = BuildEndpointUrl("responses");
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    protected override HttpRequestMessage BuildTestRequest(string apiKey)
    {
        var model = _modelProvider();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: no model configured. Set one in Settings → AI.");
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = "ping",
            ["max_output_tokens"] = 16,
        };
        var url = BuildEndpointUrl("responses");
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    /// <summary>
    /// Walk the Responses-API output array, find the first message
    /// item, and return its first <c>output_text</c> content block —
    /// which under <c>text.format = json_schema</c> is the validated
    /// resolution JSON exactly as <see cref="AiResolutionParser"/>
    /// wants it.
    /// </summary>
    protected override string ExtractStructuredOutput(string rawBody)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(rawBody); }
        catch (JsonException ex)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: malformed response JSON ({ex.Message}).", ex);
        }
        if (root is not JsonObject rootObj)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: response root is not an object.");
        }

        // Convenience field that some Responses-API responses surface
        // when the output is a single text block. Use it when present.
        if (rootObj["output_text"]?.GetValue<string>() is string outputText && !string.IsNullOrEmpty(outputText))
        {
            return outputText;
        }

        var output = rootObj["output"] as JsonArray
            ?? throw new AiMergeAssistantException(
                $"{ProviderLabel}: response missing 'output' array.");

        foreach (var item in output)
        {
            if (item is not JsonObject itemObj) continue;
            if (itemObj["type"]?.GetValue<string>() != "message") continue;
            if (itemObj["content"] is not JsonArray contents) continue;
            foreach (var c in contents)
            {
                if (c is not JsonObject cObj) continue;
                var type = cObj["type"]?.GetValue<string>();
                if (type == "output_text" && cObj["text"]?.GetValue<string>() is string text)
                {
                    return text;
                }
            }
        }

        throw new AiMergeAssistantException(
            $"{ProviderLabel}: response had no output_text content.");
    }

    /// <summary>
    /// Resolve the endpoint URL by combining the configured base URL
    /// with the API path. OpenAI proper uses <c>https://api.openai.com/v1</c>;
    /// compatible servers vary. We accept either a base URL with or
    /// without the <c>/v1</c> suffix and either with or without a
    /// trailing slash.
    /// </summary>
    private string BuildEndpointUrl(string path)
    {
        var baseUrl = _baseUrlProvider()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: no base URL configured. Set one in Settings → AI.");
        }
        baseUrl = baseUrl.TrimEnd('/');
        // If the user pasted just the host (https://api.openai.com or
        // http://localhost:1234), append /v1. If they included /v1
        // already (or any other path), respect it verbatim — some
        // gateways mount the API at /openai/v1 or similar.
        if (!baseUrl.Contains("/v", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl += "/v1";
        }
        return $"{baseUrl}/{path}";
    }
}
