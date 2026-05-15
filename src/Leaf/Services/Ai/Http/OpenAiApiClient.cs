#nullable enable
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// HTTP client for OpenAI's Responses API (<c>POST /v1/responses</c>).
/// Uses <c>text.format = json_schema</c> with <c>strict: true</c> for
/// structured output.
/// </summary>
/// <remarks>
/// <para>
/// This client is OpenAI-first-party only. The 2026-05-15 audit found
/// that virtually no third-party "OpenAI-compatible" server
/// (LM Studio, OpenRouter, vLLM, Together, Ollama-in-compat-mode)
/// implements <c>/v1/responses</c> — they all run Chat Completions
/// only. The compatible-endpoint provider therefore lives in
/// <see cref="OpenAiChatCompletionsClient"/>; this class is restricted
/// to the canonical <c>api.openai.com</c> surface.
/// </para>
/// <para>
/// Auth header: <c>Authorization: Bearer {key}</c>.
/// </para>
/// </remarks>
public sealed class OpenAiApiClient : AiApiClientBase
{
    private readonly Func<string> _modelProvider;

    public OpenAiApiClient(
        HttpClient httpClient,
        Func<string?> keyReader,
        Func<string> modelProvider,
        Func<int> timeoutSecondsProvider)
        : base(httpClient, keyReader, timeoutSecondsProvider)
    {
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
    }

    public override AiProviderKind Provider => AiProviderKind.OpenAi;

    protected override string ProviderLabel => "OpenAI (API)";

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

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
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
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
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
}
