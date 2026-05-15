#nullable enable
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// HTTP client for Google's Gemini API
/// (<c>POST generativelanguage.googleapis.com/v1beta/models/{model}:generateContent</c>).
/// Uses <c>generationConfig.responseSchema</c> + <c>responseMimeType: "application/json"</c>
/// for structured output — Gemini's equivalent of Anthropic's tool-use
/// pattern.
/// </summary>
/// <remarks>
/// Auth is the <c>x-goog-api-key</c> header (the URL <c>?key=</c>
/// variant still works but Google is restricting unrestricted keys
/// across 2026, and the header form is now canonical). Schema must be
/// sent as an object (not a JSON-encoded string).
/// </remarks>
public sealed class GeminiApiClient : AiApiClientBase
{
    private const string EndpointFormat =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    private readonly Func<string> _modelProvider;

    public GeminiApiClient(
        HttpClient httpClient,
        Func<string?> keyReader,
        Func<string> modelProvider,
        Func<int> timeoutSecondsProvider)
        : base(httpClient, keyReader, timeoutSecondsProvider)
    {
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
    }

    public override AiProviderKind Provider => AiProviderKind.GeminiApi;

    protected override string ProviderLabel => "Gemini (API)";

    protected override HttpRequestMessage BuildRequest(string prompt, string jsonSchema, string apiKey)
    {
        var model = _modelProvider();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: no model configured. Set one in Settings → AI → Gemini.");
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

        // Gemini's responseSchema is an OpenAPI 3.0 subset. The
        // 2026-05-15 audit confirmed `additionalProperties` IS now
        // supported, so we leave it intact. We still strip `$schema`
        // and other JSON-Schema-only keywords that the validator
        // silently ignores — keeps the wire payload smaller and the
        // intent obvious. If a caller eventually needs richer
        // schemas ($ref, oneOf), use `responseJsonSchema` instead.
        StripUnsupportedSchemaKeywords(schemaNode);

        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = prompt },
                    },
                },
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = schemaNode,
            },
        };

        var url = string.Format(EndpointFormat, Uri.EscapeDataString(model));
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-goog-api-key", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    protected override HttpRequestMessage BuildTestRequest(string apiKey)
    {
        var model = _modelProvider();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: no model configured. Set one in Settings → AI → Gemini.");
        }

        // Tiny ping — no schema, just a single short output, low cost.
        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = "ping" } },
                },
            },
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = 1 },
        };
        var url = string.Format(EndpointFormat, Uri.EscapeDataString(model));
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-goog-api-key", apiKey);
        return req;
    }

    /// <summary>
    /// Walk <c>candidates[0].content.parts</c> and return the first
    /// text part — under <c>responseMimeType: "application/json"</c>
    /// that text IS the JSON object the downstream parser expects.
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

        // Blocks come back without candidates — surface the reason
        // verbatim so all six current BlockReason values (SAFETY,
        // OTHER, BLOCKLIST, PROHIBITED_CONTENT, IMAGE_SAFETY,
        // BLOCK_REASON_UNSPECIFIED) read sensibly. Field presence is
        // the signal; the specific value is for the user/log.
        if (rootObj["promptFeedback"]?["blockReason"] is JsonNode block)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: request blocked ({block.GetValue<string>()}).");
        }

        var candidates = rootObj["candidates"] as JsonArray;
        if (candidates is null || candidates.Count == 0)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: response had no candidates.");
        }

        var parts = candidates[0]?["content"]?["parts"] as JsonArray;
        if (parts is null || parts.Count == 0)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: candidate had no content parts.");
        }

        foreach (var part in parts)
        {
            if (part is JsonObject obj && obj["text"]?.GetValue<string>() is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        throw new AiMergeAssistantException(
            $"{ProviderLabel}: no text part found in response.");
    }

    protected override HttpRequestMessage BuildListModelsRequest(string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://generativelanguage.googleapis.com/v1beta/models");
        req.Headers.Add("x-goog-api-key", apiKey);
        return req;
    }

    /// <summary>
    /// Google's <c>/v1beta/models</c> returns <c>{models:[{name:"models/...", supportedGenerationMethods:[...]} ...]}</c>.
    /// We filter to entries that support <c>generateContent</c> (skipping
    /// embedding-only / fine-tune-only models) and strip the
    /// <c>models/</c> prefix so the user sees the bare identifier the
    /// generateContent URL expects.
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
        if (root is not JsonObject obj || obj["models"] is not JsonArray models)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: models response missing 'models' array.");
        }

        var ids = new List<string>();
        foreach (var item in models)
        {
            if (item is not JsonObject m) continue;
            if (m["supportedGenerationMethods"] is not JsonArray methods) continue;
            var supportsGenerate = methods.Any(n =>
                n is not null && string.Equals(n.GetValue<string>(), "generateContent", StringComparison.Ordinal));
            if (!supportsGenerate) continue;

            var name = m["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name)) continue;
            const string prefix = "models/";
            var id = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Strip JSON-Schema-only keywords that <c>responseSchema</c>
    /// silently ignores (it's an OpenAPI 3.0 subset). Not strictly
    /// required — Gemini accepts and discards them — but keeps the
    /// wire payload small and signals intent. <c>additionalProperties</c>
    /// is NOT stripped: the audit confirmed it's now supported.
    /// </summary>
    private static void StripUnsupportedSchemaKeywords(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("$schema");
            obj.Remove("$ref");
            obj.Remove("$defs");
            obj.Remove("definitions");
            obj.Remove("oneOf");
            obj.Remove("anyOf");
            obj.Remove("allOf");
            obj.Remove("not");
            foreach (var child in obj)
            {
                if (child.Value is not null) StripUnsupportedSchemaKeywords(child.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null) StripUnsupportedSchemaKeywords(item);
            }
        }
    }
}
