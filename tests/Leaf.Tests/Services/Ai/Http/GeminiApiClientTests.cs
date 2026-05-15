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
/// Wire-level behaviour tests for <see cref="GeminiApiClient"/>. Uses a
/// fake <see cref="HttpMessageHandler"/> so we never hit the real
/// Gemini API.
/// </summary>
public class GeminiApiClientTests
{
    // Schema includes additionalProperties (Gemini supports it as of 2026)
    // AND a $schema header (Gemini ignores it; we strip to keep payload tidy).
    private const string TestSchema = """
        {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"proposedText":{"type":"string"}},"required":["proposedText"],"additionalProperties":false}
        """;

    [Fact]
    public async Task SendAsync_BuildsCorrectRequest_PreservesAdditionalProperties_StripsJsonSchemaKeywords()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildSuccessResponse("merged"), Encoding.UTF8, "application/json"),
            });
        var client = NewClient(handler, key: "AIza-test", model: "gemini-2.5-pro");

        var result = await client.SendAsync("the prompt", TestSchema, CancellationToken.None);
        var parsed = JsonNode.Parse(result)!.AsObject();
        parsed["proposedText"]!.GetValue<string>().Should().Be("merged");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-pro:generateContent");
        handler.LastRequest.Headers.GetValues("x-goog-api-key").Should().ContainSingle().Which.Should().Be("AIza-test");

        var bodyJson = JsonNode.Parse(handler.LastBody!)!.AsObject();
        bodyJson["generationConfig"]!["responseMimeType"]!.GetValue<string>().Should().Be("application/json");

        var schemaSent = bodyJson["generationConfig"]!["responseSchema"]!.AsObject();
        // additionalProperties IS preserved — Gemini supports it as of 2026.
        schemaSent.ContainsKey("additionalProperties").Should().BeTrue();
        schemaSent["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        // $schema is stripped (silently ignored by Gemini; tidier on the wire).
        schemaSent.ContainsKey("$schema").Should().BeFalse();
        schemaSent["type"]!.GetValue<string>().Should().Be("object");
    }

    [Fact]
    public async Task SendAsync_ThrowsAuthError_On401()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":{"message":"API key not valid"}}""", Encoding.UTF8, "application/json"),
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
            new HttpResponseMessage((HttpStatusCode)429));
        var client = NewClient(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("rate limited");
    }

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("OTHER")]
    [InlineData("BLOCKLIST")]
    [InlineData("PROHIBITED_CONTENT")]
    [InlineData("IMAGE_SAFETY")]
    [InlineData("BLOCK_REASON_UNSPECIFIED")]
    public async Task SendAsync_Throws_OnAnyBlockReason(string reason)
    {
        var blocked = "{\"promptFeedback\":{\"blockReason\":\"" + reason + "\"}}";
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(blocked, Encoding.UTF8, "application/json"),
            });
        var client = NewClient(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("blocked");
        ex.Which.Message.Should().Contain(reason);
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoCandidates()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"candidates":[]}""", Encoding.UTF8, "application/json"),
            });
        var client = NewClient(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no candidates");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoKeyConfigured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler, key: null);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no API key configured");
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoModelConfigured()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = NewClient(handler, model: "");

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("no model configured");
    }

    private static GeminiApiClient NewClient(
        HttpMessageHandler handler,
        string? key = "AIza-test",
        string model = "gemini-2.5-pro",
        int timeoutSeconds = 30)
        => new(
            new HttpClient(handler),
            keyReader: () => key,
            modelProvider: () => model,
            timeoutSecondsProvider: () => timeoutSeconds);

    private static string BuildSuccessResponse(string proposedText)
        // Gemini returns the JSON object as a string inside the text part —
        // the responseMimeType: application/json header tells us that
        // string IS valid JSON for the schema. The client returns it
        // verbatim for the downstream AiResolutionParser.
        => $$"""
        {
          "candidates": [
            {
              "content": {
                "role": "model",
                "parts": [
                  { "text": "{\"proposedText\":\"{{proposedText}}\",\"rationale\":\"r\",\"confidence\":\"medium\"}" }
                ]
              },
              "finishReason": "STOP"
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
