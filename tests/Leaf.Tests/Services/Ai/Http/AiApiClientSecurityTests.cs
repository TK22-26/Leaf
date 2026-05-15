#nullable enable
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Leaf.Services;
using Leaf.Services.Ai.Http;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Ai.Http;

/// <summary>
/// Cross-cutting security/robustness tests for <see cref="AiApiClientBase"/>:
/// response size cap, log redaction of API-key shapes in error bodies,
/// cache invalidation via <see cref="IAiApiClient.RefreshKey"/>.
/// </summary>
public class AiApiClientSecurityTests
{
    private const string TestSchema = """
        {"type":"object","properties":{"proposedText":{"type":"string"}},"required":["proposedText"],"additionalProperties":false}
        """;

    [Fact]
    public async Task SendAsync_RejectsResponse_WhenContentLengthExceedsCap()
    {
        // Declared Content-Length larger than the 4 MB cap — rejected
        // up front without reading the body, so a hostile endpoint
        // can't drain bandwidth.
        var handler = new RecordingHandler(_ =>
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            content.Headers.ContentLength = 8 * 1024 * 1024;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var client = NewClaude(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("too large");
    }

    [Fact]
    public async Task SendAsync_RejectsResponse_WhenStreamExceedsCap()
    {
        // Streaming body without a declared Content-Length but larger
        // than the cap — must be rejected mid-stream.
        var bigPayload = new string('x', 5 * 1024 * 1024);
        var handler = new RecordingHandler(_ =>
        {
            var content = new StreamContent(new System.IO.MemoryStream(Encoding.UTF8.GetBytes(bigPayload)));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var client = NewClaude(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().Contain("cap");
    }

    [Fact]
    public async Task SendAsync_ErrorBody_RedactsApiKeyShapes()
    {
        // Hostile / misconfigured server echoes back the API key in
        // its error body. We must scrub before embedding the body in
        // the exception message — otherwise the key lands in logs and
        // UI toasts via AsyncErrorHandler.
        const string keyedBody = """
            {"error":"unauthorized for key sk-ant-real_key_abc123_more_chars and Google AIza1234567890abcdefghij_extra"}
            """;
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(keyedBody, Encoding.UTF8, "application/json"),
            });
        var client = NewClaude(handler);

        var act = async () => await client.SendAsync("p", TestSchema, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<AiMergeAssistantException>();
        ex.Which.Message.Should().NotContain("real_key_abc123");
        ex.Which.Message.Should().NotContain("AIza1234567890");
        ex.Which.Message.Should().Contain("REDACTED");
    }

    [Fact]
    public void Log_Redact_ScrubsAllKnownApiKeyShapes()
    {
        // Belt-and-suspenders: the Log.Redact function itself catches
        // these patterns regardless of caller. Anthropic / OpenAI /
        // Google shapes — confirm each matches and is replaced.
        var anth = Log.Redact("oops sk-ant-AbCdEf1234567890 leaked");
        anth.Should().NotContain("AbCdEf1234567890");
        anth.Should().Contain("sk-ant-***REDACTED***");

        var oa = Log.Redact("oops sk-proj_AbCdEf1234567890abcdefghij leaked");
        oa.Should().NotContain("AbCdEf1234567890abcdefghij");
        oa.Should().Contain("sk-***REDACTED***");

        var goog = Log.Redact("oops AIzaSyAbCdEf1234567890abcdefghij leaked");
        goog.Should().NotContain("AIzaSyAbCdEf1234567890");
        goog.Should().Contain("AIza***REDACTED***");
    }

    [Fact]
    public void Log_Redact_DoesNotMatchInnocuousStrings()
    {
        // Don't over-match — short "sk-" prefixes that aren't really
        // keys must pass through. Also test the credential-URL pattern
        // (already covered elsewhere) isn't disturbed.
        Log.Redact("sk-").Should().Be("sk-");
        Log.Redact("sk-abc").Should().Be("sk-abc"); // too short
        Log.Redact("the AIza company").Should().Be("the AIza company"); // too short
    }

    [Fact]
    public async Task RefreshKey_ForcesReReadFromCredentialReader()
    {
        var reads = 0;
        string? KeyReader()
        {
            reads++;
            return reads == 1 ? "first-key" : "rotated-key";
        }
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildToolUseResponse("ok"), Encoding.UTF8, "application/json"),
            });
        var client = new ClaudeApiClient(
            new HttpClient(handler),
            keyReader: KeyReader,
            modelProvider: () => "claude-sonnet-4-5",
            timeoutSecondsProvider: () => 30);

        await client.SendAsync("p1", TestSchema, CancellationToken.None);
        handler.LastRequest!.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("first-key");

        // Second call uses cached key — even though KeyReader would
        // return a different value if invoked, we shouldn't ask.
        await client.SendAsync("p2", TestSchema, CancellationToken.None);
        handler.LastRequest!.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("first-key");
        reads.Should().Be(1);

        // After RefreshKey the next call must re-read.
        client.RefreshKey();
        await client.SendAsync("p3", TestSchema, CancellationToken.None);
        handler.LastRequest!.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("rotated-key");
        reads.Should().Be(2);
    }

    private static ClaudeApiClient NewClaude(HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            keyReader: () => "sk-ant-test",
            modelProvider: () => "claude-sonnet-4-5",
            timeoutSecondsProvider: () => 30);

    private static string BuildToolUseResponse(string proposedText)
        => "{\"content\":[{\"type\":\"tool_use\",\"id\":\"t\",\"name\":\"submit_resolution\","
         + "\"input\":{\"proposedText\":\"" + proposedText + "\",\"rationale\":\"r\",\"confidence\":\"high\"}}]}";

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
