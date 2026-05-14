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
/// Wire-level behaviour tests for <see cref="OpenAiApiClient"/>.
/// Exercises both the OpenAI-proper instance (fixed base URL) and the
/// compatible-endpoint instance (user-supplied base URL).
/// </summary>
public class OpenAiApiClientTests
{
    private const string TestSchema = """
        {"type":"object","properties":{"proposedText":{"type":"string"}},"required":["proposedText"],"additionalProperties":false}
        """;

    [Fact]
    public async Task SendAsync_OpenAi_BuildsCorrectRequest()
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

    [Theory]
    [InlineData("http://localhost:1234", "http://localhost:1234/v1/responses")]
    [InlineData("http://localhost:1234/", "http://localhost:1234/v1/responses")]
    [InlineData("http://localhost:1234/v1", "http://localhost:1234/v1/responses")]
    [InlineData("http://localhost:1234/v1/", "http://localhost:1234/v1/responses")]
    [InlineData("https://gateway.example.com/openai/v1", "https://gateway.example.com/openai/v1/responses")]
    public async Task SendAsync_OpenAiCompatible_ResolvesBaseUrlCorrectly(string baseUrl, string expectedFullUrl)
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildSuccessResponse("x"), Encoding.UTF8, "application/json"),
            });
        var client = NewCompatible(handler, baseUrl: baseUrl);

        await client.SendAsync("p", TestSchema, CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Be(expectedFullUrl);
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
    public async Task SendAsync_Throws_WhenNoKeyConfigured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewOpenAi(handler, key: null);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no API key configured");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenCompatibleEndpointHasEmptyBaseUrl()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewCompatible(handler, baseUrl: "");

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("base URL");
    }

    [Fact]
    public void Constructor_RejectsNonOpenAiKind()
    {
        var act = () => new OpenAiApiClient(
            AiProviderKind.Claude, "x",
            new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            keyReader: () => "k",
            baseUrlProvider: () => "https://api.openai.com/v1",
            modelProvider: () => "m",
            timeoutSecondsProvider: () => 30);

        act.Should().Throw<ArgumentException>();
    }

    private static OpenAiApiClient NewOpenAi(
        HttpMessageHandler handler,
        string? key = "sk-test",
        string model = "gpt-5-codex",
        int timeoutSeconds = 30)
        => new(
            AiProviderKind.OpenAi,
            "OpenAI (API)",
            new HttpClient(handler),
            keyReader: () => key,
            baseUrlProvider: () => "https://api.openai.com/v1",
            modelProvider: () => model,
            timeoutSecondsProvider: () => timeoutSeconds);

    private static OpenAiApiClient NewCompatible(
        HttpMessageHandler handler,
        string baseUrl = "http://localhost:1234/v1",
        string? key = "sk-local",
        string model = "local-model",
        int timeoutSeconds = 30)
        => new(
            AiProviderKind.OpenAiCompatible,
            "OpenAI-Compatible",
            new HttpClient(handler),
            keyReader: () => key,
            baseUrlProvider: () => baseUrl,
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
