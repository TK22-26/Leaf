#nullable enable
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// HTTP client for Anthropic's Messages API
/// (<c>POST https://api.anthropic.com/v1/messages</c>). Uses the
/// forced-tool-use pattern for structured output: a single tool is
/// declared with the supplied JSON schema as its <c>input_schema</c>
/// and Claude is required to call it, so the response always carries
/// the resolution payload in a <c>tool_use</c> content block.
/// </summary>
/// <remarks>
/// The <c>tools</c> + <c>tool_choice</c> shape is Anthropic's canonical
/// way to enforce JSON output on the Messages API — there is no
/// <c>response_format</c> field like OpenAI's. We never let the model
/// emit free-form text for the resolution; the only valid stop reason
/// is <c>tool_use</c>.
/// </remarks>
public sealed class ClaudeApiClient : AiApiClientBase
{
    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string ResolutionToolName = "submit_resolution";

    private readonly Func<string> _modelProvider;

    public ClaudeApiClient(
        HttpClient httpClient,
        Func<string?> keyReader,
        Func<string> modelProvider,
        Func<int> timeoutSecondsProvider)
        : base(httpClient, keyReader, timeoutSecondsProvider)
    {
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
    }

    public override AiProviderKind Provider => AiProviderKind.ClaudeApi;

    protected override string ProviderLabel => "Claude (API)";

    protected override HttpRequestMessage BuildRequest(string prompt, string jsonSchema, string apiKey)
    {
        var model = _modelProvider();
        if (string.IsNullOrWhiteSpace(model))
        {
            // Fail loud per engineering-software policy — never silently
            // substitute a default model behind the user's back.
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: no model configured. Set one in Settings → AI → Claude.");
        }

        // Parse the schema once so we can embed it as a JSON object (not
        // a string) under the tool's input_schema.
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
            ["max_tokens"] = 4096,
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = ResolutionToolName,
                    ["description"] = "Submit the proposed merge conflict resolution.",
                    ["input_schema"] = schemaNode,
                },
            },
            ["tool_choice"] = new JsonObject
            {
                ["type"] = "tool",
                ["name"] = ResolutionToolName,
            },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt,
                },
            },
        };

        var req = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    protected override HttpRequestMessage BuildTestRequest(string apiKey)
    {
        // 1-token ping — keeps the connection test cheap. We don't force
        // a tool here; just confirm the key authenticates and the model
        // is reachable. A 4xx response from this trips the same error
        // surfacing as a real merge call.
        var model = _modelProvider();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: no model configured. Set one in Settings → AI → Claude.");
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 1,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "ping" },
            },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);
        return req;
    }

    /// <summary>
    /// Walk the response's <c>content</c> array, find the
    /// <c>tool_use</c> block named <c>submit_resolution</c>, and emit
    /// its <c>input</c> as a serialized JSON object string —
    /// exactly the shape <see cref="AiResolutionParser"/> consumes.
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
        if (root is not JsonObject rootObj || rootObj["content"] is not JsonArray content)
        {
            throw new AiMergeAssistantException(
                $"{ProviderLabel}: response missing 'content' array.");
        }

        foreach (var item in content)
        {
            if (item is not JsonObject obj) continue;
            var type = obj["type"]?.GetValue<string>();
            if (type != "tool_use") continue;
            var name = obj["name"]?.GetValue<string>();
            if (name != ResolutionToolName) continue;
            var input = obj["input"];
            if (input is null)
            {
                throw new AiMergeAssistantException(
                    $"{ProviderLabel}: tool_use block has no 'input' payload.");
            }
            return input.ToJsonString();
        }

        // No tool_use block — Claude refused the tool or the response
        // shape changed. Surface the first text block (if any) so the
        // user can see what the model said instead.
        var textFallback = content
            .OfType<JsonObject>()
            .Where(o => o["type"]?.GetValue<string>() == "text")
            .Select(o => o["text"]?.GetValue<string>())
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));
        throw new AiMergeAssistantException(textFallback is null
            ? $"{ProviderLabel}: response contained no '{ResolutionToolName}' tool_use block."
            : $"{ProviderLabel}: model returned text instead of the resolution tool: {Truncate(textFallback)}");
    }

    private static string Truncate(string s) => s.Length <= 240 ? s : s[..240] + "…";
}
