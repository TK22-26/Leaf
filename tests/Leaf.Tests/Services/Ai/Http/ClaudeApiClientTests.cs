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
/// Wire-level behaviour tests for <see cref="ClaudeApiClient"/>. Uses a
/// fake <see cref="HttpMessageHandler"/> so we never touch the real
/// Anthropic API. Each test exercises one envelope shape end-to-end —
/// request construction, status-code handling, response unwrap.
/// </summary>
public class ClaudeApiClientTests
{
    private const string TestSchema = """
        {"type":"object","properties":{"proposedText":{"type":"string"}},"required":["proposedText"]}
        """;

    [Fact]
    public async Task SendAsync_BuildsCorrectRequest()
    {
        // Captures the outbound request for assertions; returns a canned
        // tool_use response so the call completes successfully.
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildToolUseResponse("merged text"), Encoding.UTF8, "application/json"),
            });
        var client = NewClient(handler, key: "sk-ant-test123", model: "claude-sonnet-4-5");

        var result = await client.SendAsync("the prompt", TestSchema, CancellationToken.None);

        // Inner JSON unwrapped to the tool's input object.
        var parsed = JsonNode.Parse(result)!.AsObject();
        parsed["proposedText"]!.GetValue<string>().Should().Be("merged text");

        // Request shape assertions.
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/messages");
        handler.LastRequest.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("sk-ant-test123");
        handler.LastRequest.Headers.GetValues("anthropic-version").Should().ContainSingle().Which.Should().Be("2023-06-01");

        var bodyJson = JsonNode.Parse(handler.LastBody!)!.AsObject();
        bodyJson["model"]!.GetValue<string>().Should().Be("claude-sonnet-4-5");
        bodyJson["tool_choice"]!["type"]!.GetValue<string>().Should().Be("tool");
        bodyJson["tool_choice"]!["name"]!.GetValue<string>().Should().Be("submit_resolution");
        bodyJson["tools"]!.AsArray()[0]!["input_schema"]!["type"]!.GetValue<string>().Should().Be("object");
    }

    [Fact]
    public async Task SendAsync_ThrowsAuthError_On401()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"invalid x-api-key"}""", Encoding.UTF8, "application/json"),
            });
        var client = NewClient(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("API key rejected");
    }

    [Fact]
    public async Task SendAsync_ThrowsRateLimited_On429()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("rate limited", Encoding.UTF8, "text/plain"),
            });
        var client = NewClient(handler);

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
        var client = NewClient(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        await act.Should().ThrowAsync<AiMergeAssistantException>();
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoToolUseInResponse()
    {
        // Model returned text instead of calling the tool — should surface
        // a helpful error that includes the text fallback.
        const string textOnly = """
            {"content":[{"type":"text","text":"I can't merge that."}]}
            """;
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(textOnly, Encoding.UTF8, "application/json"),
            });
        var client = NewClient(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("I can't merge that.");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoKeyConfigured()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildToolUseResponse("x"), Encoding.UTF8, "application/json"),
            });
        var client = NewClient(handler, key: null);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no API key configured");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoModelConfigured()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildToolUseResponse("x"), Encoding.UTF8, "application/json"),
            });
        var client = NewClient(handler, model: "");

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no model configured");
    }

    [Fact]
    public void RefreshKey_ClearsCache()
    {
        var calls = 0;
        string? KeyReader()
        {
            calls++;
            return calls == 1 ? "first" : "second";
        }
        var client = new ClaudeApiClient(
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            keyReader: KeyReader,
            modelProvider: () => "m",
            timeoutSecondsProvider: () => 5);

        client.HasKey.Should().BeTrue();   // 1st read, caches "first"
        client.HasKey.Should().BeTrue();   // cache hit, no re-read
        calls.Should().Be(1);

        client.RefreshKey();
        client.HasKey.Should().BeTrue();   // re-reads, gets "second"
        calls.Should().Be(2);
    }

    private static ClaudeApiClient NewClient(
        HttpMessageHandler handler,
        string? key = "sk-ant-test",
        string model = "claude-sonnet-4-5",
        int timeoutSeconds = 30)
        => new(
            new HttpClient(handler),
            keyReader: () => key,
            modelProvider: () => model,
            timeoutSecondsProvider: () => timeoutSeconds);

    private static string BuildToolUseResponse(string proposedText)
        => $$"""
        {
          "id": "msg_abc",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-4-5",
          "content": [
            {
              "type": "tool_use",
              "id": "toolu_1",
              "name": "submit_resolution",
              "input": { "proposedText": "{{proposedText}}", "rationale": "test", "confidence": "high" }
            }
          ],
          "stop_reason": "tool_use"
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
