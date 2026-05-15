#nullable enable
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Leaf.Services.Ai.Http;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Ai.Http;

/// <summary>
/// Wire-level behaviour tests for <see cref="OpenAiChatCompletionsClient"/> —
/// OpenAI-API-compatible custom endpoints (LM Studio, OpenRouter, etc.).
/// Audit (2026-05-15) confirmed Chat Completions is the universal
/// surface for these servers, not Responses API.
/// </summary>
public class OpenAiChatCompletionsClientTests
{
    private const string TestSchema = """
        {"type":"object","properties":{"proposedText":{"type":"string"}},"required":["proposedText"],"additionalProperties":false}
        """;

    [Theory]
    [InlineData("http://localhost:1234", "http://localhost:1234/v1/chat/completions")]
    [InlineData("http://localhost:1234/", "http://localhost:1234/v1/chat/completions")]
    [InlineData("http://localhost:1234/v1", "http://localhost:1234/v1/chat/completions")]
    [InlineData("http://localhost:1234/v1/", "http://localhost:1234/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://gateway.example.com/openai/v1", "https://gateway.example.com/openai/v1/chat/completions")]
    public async Task SendAsync_ResolvesBaseUrlCorrectly(string baseUrl, string expectedFullUrl)
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildSuccessResponse("x"), Encoding.UTF8, "application/json"),
            });
        var client = New(handler, baseUrl: baseUrl);

        await client.SendAsync("p", TestSchema, CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Be(expectedFullUrl);
    }

    [Fact]
    public async Task SendAsync_BuildsChatCompletionsShape()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildSuccessResponse("merged"), Encoding.UTF8, "application/json"),
            });
        var client = New(handler, key: "sk-local", model: "local-model");

        var result = await client.SendAsync("the prompt", TestSchema, CancellationToken.None);
        var parsed = JsonNode.Parse(result)!.AsObject();
        parsed["proposedText"]!.GetValue<string>().Should().Be("merged");

        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("sk-local");

        var bodyJson = JsonNode.Parse(handler.LastBody!)!.AsObject();
        bodyJson["model"]!.GetValue<string>().Should().Be("local-model");
        // Chat Completions wire shape — messages array (not Responses-API
        // `input` string), response_format at TOP level (not nested under
        // text.format), strict + name + schema inside json_schema wrapper.
        bodyJson["messages"]!.AsArray()[0]!["role"]!.GetValue<string>().Should().Be("user");
        bodyJson["messages"]!.AsArray()[0]!["content"]!.GetValue<string>().Should().Be("the prompt");
        bodyJson["response_format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        bodyJson["response_format"]!["json_schema"]!["name"]!.GetValue<string>().Should().Be("merge_resolution");
        bodyJson["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ThrowsAuthError_On401()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = New(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("API key rejected");
    }

    [Fact]
    public async Task SendAsync_Throws_OnRefusal()
    {
        const string refusal = """
            {"choices":[{"message":{"role":"assistant","refusal":"I cannot help with that."}}]}
            """;
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(refusal, Encoding.UTF8, "application/json"),
            });
        var client = New(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("refused");
        ex.Which.Message.Should().Contain("I cannot help");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenBaseUrlEmpty()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = New(handler, baseUrl: "");

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("base URL");
    }

    [Fact]
    public async Task SendAsync_ThrowsRateLimited_On429()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage((HttpStatusCode)429));
        var client = New(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("rate limited");
    }

    [Fact]
    public async Task SendAsync_Throws_OnMalformedResponse()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not even json", Encoding.UTF8, "application/json"),
            });
        var client = New(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        await act.Should().ThrowAsync<AiMergeAssistantException>();
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoModelConfigured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = New(handler, model: "");

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no model configured");
    }

    [Theory]
    [InlineData("   ")]      // whitespace-only treated as empty after trim
    [InlineData("not-a-url")] // unparseable
    public async Task SendAsync_Throws_OnUnusableBaseUrl(string baseUrl)
    {
        // Unparseable URL falls through to HttpClient.SendAsync which
        // throws an HttpRequestException — we wrap it as
        // AiMergeAssistantException with a "network error" prefix.
        // Whitespace-only is caught by the explicit base-URL guard.
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = New(handler, baseUrl: baseUrl);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        await act.Should().ThrowAsync<AiMergeAssistantException>();
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoKeyConfigured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = New(handler, key: null);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no API key configured");
    }

    [Fact]
    public void Provider_IsOpenAiCompatible()
    {
        var client = New(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        client.Provider.Should().Be(AiProviderKind.OpenAiCompatible);
    }

    private static OpenAiChatCompletionsClient New(
        HttpMessageHandler handler,
        string baseUrl = "http://localhost:1234/v1",
        string? key = "sk-local",
        string model = "local-model",
        int timeoutSeconds = 30)
        => new(
            new HttpClient(handler),
            keyReader: () => key,
            baseUrlProvider: () => baseUrl,
            modelProvider: () => model,
            timeoutSecondsProvider: () => timeoutSeconds);

    private static string BuildSuccessResponse(string proposedText)
        => $$"""
        {
          "id": "chatcmpl-1",
          "object": "chat.completion",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": "{\"proposedText\":\"{{proposedText}}\",\"rationale\":\"r\",\"confidence\":\"medium\"}"
              },
              "finish_reason": "stop"
            }
          ]
        }
        """;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return _responder(request);
        }
    }
}
