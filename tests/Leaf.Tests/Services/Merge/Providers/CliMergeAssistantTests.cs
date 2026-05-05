#nullable enable
using FluentAssertions;
using Leaf.Services.Ai;
using Leaf.Services.Ai.Adapters;
using Leaf.Services.Merge;
using Leaf.Services.Merge.Providers;
using Xunit;

namespace Leaf.Tests.Services.Merge.Providers;

/// <summary>
/// Behaviour tests for the three CLI-backed merge assistants
/// (Claude / Gemini / Codex). They share <see cref="CliMergeAssistantBase"/>,
/// so the tests live in a single file but each provider is exercised
/// against a fake runner. We don't need the real CLIs installed —
/// the fake stands in for the transport.
/// </summary>
public class CliMergeAssistantTests
{
    private static AiResolutionRequest SampleRequest() => new(
        FilePath: "test.cs",
        Language: "csharp",
        BaseLines: new[] { "b" },
        OursLines: new[] { "o" },
        TheirsLines: new[] { "t" },
        ContextBefore: Array.Empty<string>(),
        ContextAfter: Array.Empty<string>());

    private const string CannedJsonResponse =
        """{"proposedText":"merged","rationale":"both ok","confidence":"medium"}""";

    [Fact]
    public async Task ReturnsNull_WhenDisabled()
    {
        var assistant = new ClaudeMergeAssistant(
            runner: new RecordingRunner(stdout: CannedJsonResponse),
            adapter: new ClaudeCliAdapter(),
            enabledProvider: () => false,
            consentProvider: () => true,
            isClaudeConnectedProvider: () => true,
            timeoutSecondsProvider: () => 60);

        var result = await assistant.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenConsentMissing()
    {
        var assistant = new ClaudeMergeAssistant(
            runner: new RecordingRunner(stdout: CannedJsonResponse),
            adapter: new ClaudeCliAdapter(),
            enabledProvider: () => true,
            consentProvider: () => false,
            isClaudeConnectedProvider: () => true,
            timeoutSecondsProvider: () => 60);

        var result = await assistant.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenProviderDisconnected()
    {
        // Even with the global feature on and consent given, a
        // disconnected provider should silently return null — the
        // user-facing failure path is the router's job in Phase 4.
        var assistant = new ClaudeMergeAssistant(
            runner: new RecordingRunner(stdout: CannedJsonResponse),
            adapter: new ClaudeCliAdapter(),
            enabledProvider: () => true,
            consentProvider: () => true,
            isClaudeConnectedProvider: () => false,
            timeoutSecondsProvider: () => 60);

        var result = await assistant.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task SuccessfulInvocation_ParsesResolution()
    {
        var assistant = new ClaudeMergeAssistant(
            runner: new RecordingRunner(stdout: CannedJsonResponse),
            adapter: new ClaudeCliAdapter(),
            enabledProvider: () => true,
            consentProvider: () => true,
            isClaudeConnectedProvider: () => true,
            timeoutSecondsProvider: () => 60);

        var result = await assistant.RequestResolutionAsync(SampleRequest());

        result.Should().NotBeNull();
        result!.ProposedText.Should().Be("merged");
        result.Rationale.Should().Be("both ok");
        result.Confidence.Should().Be(AiConfidence.Medium);
    }

    [Fact]
    public async Task RunnerFailure_ThrowsWithProviderName()
    {
        var assistant = new GeminiMergeAssistant(
            runner: new RecordingRunner(success: false, detail: "exit 1: gemini not found"),
            adapter: new GeminiCliAdapter(),
            enabledProvider: () => true,
            consentProvider: () => true,
            isGeminiConnectedProvider: () => true,
            timeoutSecondsProvider: () => 60);

        var act = () => assistant.RequestResolutionAsync(SampleRequest());

        await act.Should().ThrowAsync<AiMergeAssistantException>()
            .WithMessage("*Gemini CLI*gemini not found*");
    }

    [Fact]
    public async Task MalformedResponse_ThrowsParserException()
    {
        var assistant = new CodexMergeAssistant(
            runner: new RecordingRunner(stdout: "not json"),
            adapter: new CodexCliAdapter(),
            enabledProvider: () => true,
            consentProvider: () => true,
            isCodexConnectedProvider: () => true,
            timeoutSecondsProvider: () => 60);

        var act = () => assistant.RequestResolutionAsync(SampleRequest());

        await act.Should().ThrowAsync<AiMergeAssistantException>()
            .WithMessage("*did not contain a JSON object*");
    }

    [Fact]
    public void ProviderDescription_IsCliName()
    {
        new ClaudeMergeAssistant(
            new RecordingRunner(), new ClaudeCliAdapter(),
            () => true, () => true, () => true, () => 60)
            .ProviderDescription.Should().Be("Claude CLI");
        new GeminiMergeAssistant(
            new RecordingRunner(), new GeminiCliAdapter(),
            () => true, () => true, () => true, () => 60)
            .ProviderDescription.Should().Be("Gemini CLI");
        new CodexMergeAssistant(
            new RecordingRunner(), new CodexCliAdapter(),
            () => true, () => true, () => true, () => 60)
            .ProviderDescription.Should().Be("Codex CLI");
    }

    [Fact]
    public async Task RunnerReceivesPrompt_WithBoundedRequestContent()
    {
        // Privacy: the runner sees the exact prompt produced by
        // AiMergePromptBuilder. Spot-check that conflict content is in
        // the prompt and nothing extra leaks.
        var recorder = new RecordingRunner(stdout: CannedJsonResponse);
        var assistant = new ClaudeMergeAssistant(
            runner: recorder,
            adapter: new ClaudeCliAdapter(),
            enabledProvider: () => true,
            consentProvider: () => true,
            isClaudeConnectedProvider: () => true,
            timeoutSecondsProvider: () => 60);

        await assistant.RequestResolutionAsync(SampleRequest());

        recorder.LastInvocation.Should().NotBeNull();
        recorder.LastInvocation!.Stdin.Should().Contain("test.cs");
        recorder.LastInvocation.Stdin.Should().Contain("BASE");
        recorder.LastInvocation.Stdin.Should().Contain("OURS");
        recorder.LastInvocation.Stdin.Should().Contain("THEIRS");
    }

    /// <summary>Hand-rolled fake runner that records the last invocation.</summary>
    private sealed class RecordingRunner : IAiCliRunner
    {
        private readonly string _stdout;
        private readonly bool _success;
        private readonly string _detail;
        public AiCliInvocation? LastInvocation { get; private set; }

        public RecordingRunner(string stdout = "{}", bool success = true, string detail = "")
        {
            _stdout = stdout;
            _success = success;
            _detail = detail;
        }

        public Task<AiCliProcessResult> RunAsync(AiCliInvocation invocation, int timeoutSeconds, CancellationToken ct = default)
        {
            LastInvocation = invocation;
            return Task.FromResult(new AiCliProcessResult(
                Success: _success,
                ExitCode: _success ? 0 : 1,
                Stdout: _success ? _stdout : string.Empty,
                Stderr: string.Empty,
                Detail: _detail));
        }
    }
}
