#nullable enable
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Leaf.Services.Ai.Http;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Ai.Http;

/// <summary>
/// Wire-level tests for the four <see cref="IAiApiClient.ListModelsAsync"/>
/// implementations. Each provider has a different envelope shape and a
/// different filter rule — these tests assert that we hit the correct
/// endpoint with the correct auth header, parse the envelope, and apply
/// the filter (when applicable).
/// </summary>
public class ListModelsAsyncTests
{
    // ── Claude ───────────────────────────────────────────────────────

    [Fact]
    public async Task Claude_ListModels_HitsEndpointWithApiKeyAndVersion()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"data\":[{\"id\":\"claude-sonnet-4-5\",\"type\":\"model\",\"display_name\":\"Sonnet 4.5\"},"
                  + "{\"id\":\"claude-opus-4-7\",\"type\":\"model\",\"display_name\":\"Opus 4.7\"}],"
                  + "\"has_more\":false}",
                    Encoding.UTF8, "application/json"),
            });
        var client = new ClaudeApiClient(
            new HttpClient(handler),
            keyReader: () => "sk-ant-test",
            modelProvider: () => "claude-sonnet-4-5",
            timeoutSecondsProvider: () => 30);

        var models = await client.ListModelsAsync(CancellationToken.None);

        models.Should().ContainInOrder("claude-sonnet-4-5", "claude-opus-4-7");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://api.anthropic.com/v1/models");
        handler.LastRequest.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("sk-ant-test");
        handler.LastRequest.Headers.GetValues("anthropic-version").Should().ContainSingle().Which.Should().Be("2023-06-01");
    }

    [Fact]
    public async Task Claude_ListModels_Throws_On401()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new ClaudeApiClient(
            new HttpClient(handler),
            keyReader: () => "sk-ant-bad",
            modelProvider: () => "claude-sonnet-4-5",
            timeoutSecondsProvider: () => 30);

        var act = async () => await client.ListModelsAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("API key rejected");
    }

    // ── Gemini ───────────────────────────────────────────────────────

    [Fact]
    public async Task Gemini_ListModels_FiltersToGenerateContent_AndStripsModelsPrefix()
    {
        // Mixed list: one chat model, one embedding-only, one
        // generateContent-capable name without the prefix (shouldn't happen
        // in practice but the parser must tolerate it).
        const string body = """
        {
          "models": [
            {"name":"models/gemini-2.5-pro","supportedGenerationMethods":["generateContent","countTokens"]},
            {"name":"models/embedding-001","supportedGenerationMethods":["embedContent"]},
            {"name":"models/gemini-2.5-flash","supportedGenerationMethods":["generateContent"]},
            {"name":"gemini-no-prefix","supportedGenerationMethods":["generateContent"]}
          ]
        }
        """;
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        var client = new GeminiApiClient(
            new HttpClient(handler),
            keyReader: () => "AIza-test",
            modelProvider: () => "gemini-2.5-pro",
            timeoutSecondsProvider: () => 30);

        var models = await client.ListModelsAsync(CancellationToken.None);

        models.Should().ContainInOrder("gemini-2.5-pro", "gemini-2.5-flash", "gemini-no-prefix");
        models.Should().NotContain("embedding-001");
        models.Should().NotContain("models/embedding-001");
        handler.LastRequest!.RequestUri!.ToString().Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models");
        handler.LastRequest.Headers.GetValues("x-goog-api-key").Should().ContainSingle().Which.Should().Be("AIza-test");
    }

    // ── OpenAI (Responses API surface, /v1/models is shared) ────────

    [Fact]
    public async Task OpenAi_ListModels_FiltersOutNonChatFamilies()
    {
        // Realistic mix from a live OpenAI account: chat, embedding,
        // audio, image, moderation, realtime, fine-tune. Only the
        // chat-capable ones should make it through.
        const string body = """
        {
          "data":[
            {"id":"gpt-5-codex","object":"model"},
            {"id":"gpt-5-mini","object":"model"},
            {"id":"o3","object":"model"},
            {"id":"o4-mini","object":"model"},
            {"id":"chatgpt-4o-latest","object":"model"},
            {"id":"text-embedding-3-large","object":"model"},
            {"id":"whisper-1","object":"model"},
            {"id":"tts-1","object":"model"},
            {"id":"dall-e-3","object":"model"},
            {"id":"omni-moderation-latest","object":"model"},
            {"id":"gpt-4o-realtime-preview","object":"model"},
            {"id":"gpt-4o-transcribe","object":"model"},
            {"id":"ft:gpt-4o:org::abc","object":"model"}
          ],
          "object":"list"
        }
        """;
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        var client = new OpenAiApiClient(
            new HttpClient(handler),
            keyReader: () => "sk-test",
            modelProvider: () => "gpt-5-codex",
            timeoutSecondsProvider: () => 30);

        var models = await client.ListModelsAsync(CancellationToken.None);

        models.Should().Contain(new[] { "gpt-5-codex", "gpt-5-mini", "o3", "o4-mini", "chatgpt-4o-latest" });
        models.Should().NotContain("text-embedding-3-large");
        models.Should().NotContain("whisper-1");
        models.Should().NotContain("tts-1");
        models.Should().NotContain("dall-e-3");
        models.Should().NotContain("omni-moderation-latest");
        models.Should().NotContain("gpt-4o-realtime-preview");
        models.Should().NotContain("gpt-4o-transcribe");
        models.Should().NotContain("ft:gpt-4o:org::abc");

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.openai.com/v1/models");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
    }

    [Theory]
    [InlineData("gpt-5-codex", true)]
    [InlineData("gpt-5", true)]
    [InlineData("gpt-5-mini", true)]
    [InlineData("gpt-4o", true)]
    [InlineData("o1", true)]
    [InlineData("o3", true)]
    [InlineData("o4-mini", true)]
    [InlineData("chatgpt-4o-latest", true)]
    [InlineData("text-embedding-3-large", false)]
    [InlineData("whisper-1", false)]
    [InlineData("tts-1-hd", false)]
    [InlineData("dall-e-3", false)]
    [InlineData("gpt-image-1", false)]
    [InlineData("omni-moderation-latest", false)]
    [InlineData("gpt-4o-realtime-preview", false)]
    [InlineData("gpt-4o-transcribe", false)]
    [InlineData("ft:gpt-4o:my-org::abc", false)]
    [InlineData("davinci-002", false)] // unrecognised family
    [InlineData("", false)]
    public void OpenAi_IsChatModelId_ClassifiesCorrectly(string id, bool expected)
    {
        OpenAiApiClient.IsChatModelId(id).Should().Be(expected);
    }

    // ── OpenAI-Compatible (passthrough) ─────────────────────────────

    [Fact]
    public async Task OpenAiCompatible_ListModels_ReturnsAllIdsVerbatim()
    {
        // No filter — compatible servers expose endpoint-specific
        // identifiers (anthropic/claude-sonnet-4-5, local-model,
        // llama3.1:8b, etc) that don't match OpenAI's chat-family
        // heuristics but are legitimate.
        const string body = """
        {
          "data":[
            {"id":"anthropic/claude-sonnet-4-5","object":"model"},
            {"id":"local-model","object":"model"},
            {"id":"llama3.1:8b","object":"model"},
            {"id":"text-embedding-3-large","object":"model"}
          ],
          "object":"list"
        }
        """;
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        var client = new OpenAiChatCompletionsClient(
            new HttpClient(handler),
            keyReader: () => "sk-local",
            baseUrlProvider: () => "http://localhost:1234/v1",
            modelProvider: () => "local-model",
            timeoutSecondsProvider: () => 30);

        var models = await client.ListModelsAsync(CancellationToken.None);

        // Verbatim — embeddings included, because the user explicitly
        // configured this endpoint and may have legitimate reasons.
        models.Should().ContainInOrder(
            "anthropic/claude-sonnet-4-5",
            "local-model",
            "llama3.1:8b",
            "text-embedding-3-large");
        handler.LastRequest!.RequestUri!.ToString().Should().Be("http://localhost:1234/v1/models");
    }

    // ── Cross-cutting ───────────────────────────────────────────────

    [Fact]
    public async Task ListModels_Throws_OnMalformedEnvelope()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"unexpected\":\"shape\"}", Encoding.UTF8, "application/json"),
            });
        var client = new ClaudeApiClient(
            new HttpClient(handler),
            keyReader: () => "sk-ant-test",
            modelProvider: () => "claude-sonnet-4-5",
            timeoutSecondsProvider: () => 30);

        var act = async () => await client.ListModelsAsync(CancellationToken.None);
        await act.Should().ThrowAsync<AiMergeAssistantException>();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }
}
