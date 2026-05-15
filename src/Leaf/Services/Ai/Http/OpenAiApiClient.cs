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

    protected override HttpRequestMessage BuildListModelsRequest(string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    /// <summary>
    /// OpenAI's <c>/v1/models</c> returns every model the key can see —
    /// chat, embeddings, image, audio, fine-tunes, deprecated dated
    /// snapshots, internal previews. Filter to user-facing chat models
    /// only (prefix-match on <c>gpt-</c>, <c>o</c>, <c>chatgpt-</c>) and
    /// exclude obvious non-chat suffixes.
    /// </summary>
    protected override IReadOnlyList<string> ParseModels(string rawBody)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(rawBody); }
        catch (JsonException ex)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: malformed models response ({ex.Message}).", ex);
        }
        if (root is not JsonObject obj || obj["data"] is not JsonArray data)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: models response missing 'data' array.");
        }

        var ids = new List<string>();
        foreach (var item in data)
        {
            if (item is JsonObject m && m["id"]?.GetValue<string>() is string id && IsChatModelId(id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>
    /// Heuristic filter for OpenAI's mixed <c>/v1/models</c> list. Keep
    /// chat-capable identifiers, drop everything else (embeddings,
    /// audio, image, moderation, dated deprecation snapshots, realtime
    /// preview SKUs that don't accept Responses-API calls).
    /// </summary>
    internal static bool IsChatModelId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;

        // Exclude well-known non-chat families up front.
        if (id.Contains("embed", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("whisper", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("tts", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("dall-e", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("image", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("moderation", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("realtime", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("transcribe", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("audio", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.StartsWith("ft:", StringComparison.OrdinalIgnoreCase)) return false; // fine-tune

        // Include the chat families we recognise. New families OpenAI
        // ships in the future fall through to the default-true clause
        // for "gpt"/"o" prefixes.
        if (id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.StartsWith("chatgpt-", StringComparison.OrdinalIgnoreCase)) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(id, @"^o\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
        return false;
    }
}
