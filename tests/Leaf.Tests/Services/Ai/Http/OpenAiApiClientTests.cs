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
/// Wire-level behaviour tests for <see cref="OpenAiApiClient"/> —
/// OpenAI-first-party Responses API.
/// </summary>
public class OpenAiApiClientTests
{
    private const string TestSchema = """
        {"type":"object","properties":{"proposedText":{"type":"string"}},"required":["proposedText"],"additionalProperties":false}
        """;

    [Fact]
    public async Task SendAsync_BuildsCorrectRequest()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildSuccessResponse("merged"), Encoding.UTF8, "application/json"),
            });
        var client = NewOpenAi(handler, key: "sk-test", model: "gpt-5-codex");

        var result = await client.SendAsync("the prompt", TestSchema, CancellationToken.None);
        var parsed = JsonNode.Parse(result)!.AsObject();
        parsed["proposedText"]!.GetValue<string>().Should().Be("merged");

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.openai.com/v1/responses");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("sk-test");

        var bodyJson = JsonNode.Parse(handler.LastBody!)!.AsObject();
        bodyJson["model"]!.GetValue<string>().Should().Be("gpt-5-codex");
        bodyJson["text"]!["format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        bodyJson["text"]!["format"]!["strict"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ThrowsAuthError_On401()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":{"message":"Invalid API key"}}""", Encoding.UTF8, "application/json"),
            });
        var client = NewOpenAi(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("API key rejected");
    }

    [Fact]
    public async Task SendAsync_ThrowsRateLimited_On429()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage((HttpStatusCode)429));
        var client = NewOpenAi(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("rate limited");
    }

    [Fact]
    public async Task SendAsync_UsesOutputTextShortcut_WhenPresent()
    {
        const string shortcutBody = """
            {"output_text":"{\"proposedText\":\"shortcut\"}","output":[]}
            """;
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(shortcutBody, Encoding.UTF8, "application/json"),
            });
        var client = NewOpenAi(handler);

        var result = await client.SendAsync("p", TestSchema, CancellationToken.None);
        var parsed = JsonNode.Parse(result)!.AsObject();
        parsed["proposedText"]!.GetValue<string>().Should().Be("shortcut");
    }

    [Fact]
    public async Task SendAsync_Throws_OnMissingOutputArray()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"resp_1"}""", Encoding.UTF8, "application/json"),
            });
        var client = NewOpenAi(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("output");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoModelConfigured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewOpenAi(handler, model: "");

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no model configured");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoKeyConfigured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewOpenAi(handler, key: null);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no API key configured");
    }

    private static OpenAiApiClient NewOpenAi(
        HttpMessageHandler handler,
        string? key = "sk-test",
        string model = "gpt-5-codex",
        int timeoutSeconds = 30)
        => new(
            new HttpClient(handler),
            keyReader: () => key,
            modelProvider: () => model,
            timeoutSecondsProvider: () => timeoutSeconds);

    private static string BuildSuccessResponse(string proposedText)
        => $$"""
        {
          "id": "resp_1",
          "object": "response",
          "output": [
            {
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "output_text", "text": "{\"proposedText\":\"{{proposedText}}\",\"rationale\":\"r\",\"confidence\":\"medium\"}" }
              ]
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
