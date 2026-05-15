#nullable enable
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// HTTP client for <c>/v1/chat/completions</c> against any
/// OpenAI-API-compatible endpoint (LM Studio, OpenRouter, vLLM,
/// Together, Ollama in OpenAI-compat mode, Azure OpenAI gateway).
/// </summary>
/// <remarks>
/// <para>
/// Audit finding (2026-05-15): the Responses API (<c>/v1/responses</c>)
/// is OpenAI-only — virtually no third-party "OpenAI-compatible"
/// server implements it. Compatible endpoints universally implement
/// Chat Completions, which uses a different request body and
/// structured-output shape:
/// <list type="bullet">
///   <item><c>response_format: {type:"json_schema", json_schema:{name, strict, schema}}</c>
///   (top-level, not nested under <c>text.format</c>)</item>
///   <item><c>messages: [{role, content}]</c> array (not the
///   Responses-style <c>input</c> string)</item>
///   <item><c>max_tokens</c> (not <c>max_output_tokens</c>)</item>
///   <item>Response: <c>choices[0].message.content</c> (not
///   <c>output[].content[].text</c>)</item>
/// </list>
/// We keep the first-party OpenAI provider on the Responses API since
/// that's the recommended modern surface. This class only serves the
/// custom-endpoint variant.
/// </para>
/// </remarks>
public sealed class OpenAiChatCompletionsClient : AiApiClientBase
{
    private readonly Func<string> _baseUrlProvider;
    private readonly Func<string> _modelProvider;

    public OpenAiChatCompletionsClient(
        HttpClient httpClient,
        Func<string?> keyReader,
        Func<string> baseUrlProvider,
        Func<string> modelProvider,
        Func<int> timeoutSecondsProvider)
        : base(httpClient, keyReader, timeoutSecondsProvider)
    {
        _baseUrlProvider = baseUrlProvider ?? throw new ArgumentNullException(nameof(baseUrlProvider));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
    }

    public override AiProviderKind Provider => AiProviderKind.OpenAiCompatible;

    protected override string ProviderLabel => "OpenAI-Compatible";

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

        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = prompt },
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "merge_resolution",
                    ["strict"] = true,
                    ["schema"] = schemaNode,
                },
            },
        };

        var url = BuildEndpointUrl("chat/completions");
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
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "ping" },
            },
            ["max_tokens"] = 16,
        };
        var url = BuildEndpointUrl("chat/completions");
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return req;
    }

    /// <summary>
    /// Walk <c>choices[0].message.content</c> and return it directly —
    /// under <c>response_format = json_schema</c> that string IS the
    /// validated JSON object the downstream parser expects.
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

        var choices = rootObj["choices"] as JsonArray;
        if (choices is null || choices.Count == 0)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: response had no choices.");
        }

        var content = choices[0]?["message"]?["content"];
        if (content?.GetValue<string>() is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Some endpoints (Anthropic-compatible bridges, certain local
        // servers) emit a refusal field when they decline a request.
        // Surface that so the user sees a meaningful error instead of a
        // generic "no content".
        var refusal = choices[0]?["message"]?["refusal"]?.GetValue<string>();
        throw new AiMergeAssistantException(refusal is null
            ? $"{ProviderLabel}: response had no message content."
            : $"{ProviderLabel}: model refused: {Truncate(refusal)}");
    }

    /// <summary>
    /// Resolve the endpoint URL from the user-supplied base URL. Accepts
    /// either host-only (<c>http://localhost:1234</c>) or
    /// already-pathed (<c>https://openrouter.ai/api/v1</c>) forms.
    /// </summary>
    private string BuildEndpointUrl(string path)
    {
        var baseUrl = _baseUrlProvider()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: no base URL configured. Set one in Settings → AI.");
        }
        // Reject garbage up front with a clear error rather than
        // letting HttpRequestMessage accept it as a relative URI and
        // surface InvalidOperationException from HttpClient.SendAsync.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: base URL must be an absolute http:// or https:// URL (got '{baseUrl}').");
        }
        baseUrl = baseUrl.TrimEnd('/');
        if (!baseUrl.Contains("/v", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl += "/v1";
        }
        return $"{baseUrl}/{path}";
    }

    private static string Truncate(string s) => s.Length <= 240 ? s : s[..240] + "…";
}
