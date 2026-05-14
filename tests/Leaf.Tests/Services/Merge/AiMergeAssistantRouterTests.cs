#nullable enable
using FluentAssertions;
using Leaf.Services;
using Leaf.Services.Ai;
using Leaf.Services.Ai.Adapters;
using Leaf.Services.Ai.Http;
using Leaf.Services.Merge;
using Leaf.Services.Merge.Providers;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Tests for <see cref="AiMergeAssistantRouter"/>: provider dispatch,
/// no-silent-fallback, settings re-read on every call.
/// </summary>
public class AiMergeAssistantRouterTests
{
    private static AiResolutionRequest SampleRequest() => new(
        FilePath: "x.cs",
        Language: "csharp",
        BaseLines: new[] { "b" },
        OursLines: new[] { "o" },
        TheirsLines: new[] { "t" },
        ContextBefore: Array.Empty<string>(),
        ContextAfter: Array.Empty<string>());

    private const string CannedJson =
        """{"proposedText":"x","rationale":"r","confidence":"medium"}""";

    private static (AiMergeAssistantRouter Router,
                    RecordingRunner Runner,
                    Func<string> ReadSelectedProvider,
                    Action<string> SetProvider,
                    Action<bool> SetEnabled,
                    Action<bool> SetConsent,
                    Func<bool> AnyClaudeCall) Build(
        bool claudeConnected = true,
        bool geminiConnected = true,
        bool codexConnected = true,
        bool ollamaConnected = true,
        bool externalServerExists = true,
        bool claudeApiConnected = false,
        bool geminiApiConnected = false)
    {
        var runner = new RecordingRunner(stdout: CannedJson);
        var providerSetting = "Claude";
        var enabled = true;
        var consent = true;

        var claude = new ClaudeMergeAssistant(
            runner, new ClaudeCliAdapter(),
            () => enabled, () => consent,
            () => claudeConnected, () => 60);
        var gemini = new GeminiMergeAssistant(
            runner, new GeminiCliAdapter(),
            () => enabled, () => consent,
            () => geminiConnected, () => 60);
        var codex = new CodexMergeAssistant(
            runner, new CodexCliAdapter(),
            () => enabled, () => consent,
            () => codexConnected, () => 60);
        var ollama = new OllamaMergeAssistant(
            new OllamaService(),
            () => enabled, () => consent,
            () => "http://localhost:11434",
            () => ollamaConnected ? "llama3.1" : string.Empty,
            () => 60);
        var external = new ExternalServerMergeAssistant(
            // We don't actually invoke the external server in these
            // tests — its IsEnabled gate is path-existence + the global
            // flag. Point it at a real file when we want it "connected"
            // in dispatch-routing tests, otherwise null.
            serverPathProvider: () => externalServerExists ? typeof(string).Assembly.Location : null,
            enabledProvider: () => enabled,
            consentGivenProvider: () => consent);

        var claudeApi = new ClaudeApiMergeAssistant(
            new StubAiApiClient(AiProviderKind.ClaudeApi, hasKey: claudeApiConnected),
            () => enabled, () => consent,
            () => claudeApiConnected);
        var geminiApi = new GeminiApiMergeAssistant(
            new StubAiApiClient(AiProviderKind.GeminiApi, hasKey: geminiApiConnected),
            () => enabled, () => consent,
            () => geminiApiConnected);

        var router = new AiMergeAssistantRouter(
            selectedProviderProvider: () => providerSetting,
            enabledProvider: () => enabled,
            consentProvider: () => consent,
            claude, gemini, codex, ollama, external, claudeApi, geminiApi);

        return (
            router,
            runner,
            ReadSelectedProvider: () => providerSetting,
            SetProvider: v => providerSetting = v,
            SetEnabled: v => enabled = v,
            SetConsent: v => consent = v,
            AnyClaudeCall: () => runner.LastInvocation?.Executable == "claude"
        );
    }

    [Fact]
    public async Task Dispatches_ToSelectedProvider()
    {
        var ctx = Build();

        ctx.SetProvider("Claude");
        await ctx.Router.RequestResolutionAsync(SampleRequest());
        ctx.Runner.LastInvocation!.Executable.Should().Be("claude");

        ctx.SetProvider("Gemini");
        await ctx.Router.RequestResolutionAsync(SampleRequest());
        ctx.Runner.LastInvocation!.Executable.Should().Be("gemini");

        ctx.SetProvider("Codex");
        await ctx.Router.RequestResolutionAsync(SampleRequest());
        ctx.Runner.LastInvocation!.Executable.Should().Be("codex");
    }

    [Fact]
    public async Task ReturnsNull_WhenGloballyDisabled()
    {
        var ctx = Build();
        ctx.SetEnabled(false);
        var result = await ctx.Router.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenConsentMissing()
    {
        var ctx = Build();
        ctx.SetConsent(false);
        var result = await ctx.Router.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task SelectedProviderDisconnected_ThrowsWithProviderName_NoSilentFallback()
    {
        // Critical contract: even though Gemini is connected, the
        // router must NOT silently use it when the user picked Claude.
        var ctx = Build(claudeConnected: false, geminiConnected: true);
        ctx.SetProvider("Claude");

        var act = () => ctx.Router.RequestResolutionAsync(SampleRequest());

        await act.Should().ThrowAsync<AiMergeAssistantException>()
            .WithMessage("*Claude CLI*not connected*pick a different provider*");

        // And the runner must NOT have been invoked — no fallback.
        ctx.Runner.LastInvocation.Should().BeNull();
    }

    [Fact]
    public async Task SettingsRereadEachCall_ProviderSwapTakesEffectImmediately()
    {
        var ctx = Build();

        ctx.SetProvider("Claude");
        await ctx.Router.RequestResolutionAsync(SampleRequest());
        ctx.Runner.LastInvocation!.Executable.Should().Be("claude");

        // Hot-swap: change the setting, next call must use the new
        // provider without rebuilding the router or restarting the app.
        ctx.SetProvider("Gemini");
        await ctx.Router.RequestResolutionAsync(SampleRequest());
        ctx.Runner.LastInvocation!.Executable.Should().Be("gemini");
    }

    [Fact]
    public void ProviderDescription_ReflectsCurrentSelection()
    {
        var ctx = Build();
        ctx.SetProvider("Claude");
        ctx.Router.ProviderDescription.Should().Be("Claude CLI");
        ctx.SetProvider("Gemini");
        ctx.Router.ProviderDescription.Should().Be("Gemini CLI");
        ctx.SetProvider("Codex");
        ctx.Router.ProviderDescription.Should().Be("Codex CLI");
        ctx.SetProvider("Ollama");
        ctx.Router.ProviderDescription.Should().Contain("Ollama");
        ctx.SetProvider("ExternalServer");
        ctx.Router.ProviderDescription.Should().Contain("External server");
    }

    [Fact]
    public void EmptyProvider_FallsBackToExternalServer()
    {
        // An unset / empty AiMergeProvider should route to External
        // Server (the only always-present provider) — not throw at
        // resolve time. The dispatch error surfaces on actual use if
        // External Server isn't configured.
        var ctx = Build();
        ctx.SetProvider("");
        ctx.Router.ProviderDescription.Should().Contain("External server");
    }

    [Fact]
    public void UnknownProvider_FallsBackToExternalServer()
    {
        // A hand-edited settings.json with a typo'd provider name
        // shouldn't crash the app; it should resolve to External Server
        // and the user can pick something else from the dropdown.
        var ctx = Build();
        ctx.SetProvider("not-a-real-provider");
        ctx.Router.ProviderDescription.Should().Contain("External server");
    }

    /// <summary>
    /// Recording fake runner — same shape as the one in
    /// CliMergeAssistantTests. Local copy keeps each test file
    /// self-contained.
    /// </summary>
    private sealed class RecordingRunner : IAiCliRunner
    {
        private readonly string _stdout;
        public AiCliInvocation? LastInvocation { get; private set; }

        public RecordingRunner(string stdout = "{}")
        {
            _stdout = stdout;
        }

        public Task<AiCliProcessResult> RunAsync(AiCliInvocation invocation, int timeoutSeconds, CancellationToken ct = default)
        {
            LastInvocation = invocation;
            return Task.FromResult(new AiCliProcessResult(true, 0, _stdout, string.Empty, string.Empty));
        }
    }

    /// <summary>
    /// Stub HTTP client used to construct an API-transport assistant
    /// without touching the network. <see cref="SendAsync"/> returns a
    /// canned resolution JSON shaped like Anthropic's tool_use input.
    /// </summary>
    private sealed class StubAiApiClient : IAiApiClient
    {
        public AiProviderKind Provider { get; }
        public bool HasKey { get; }
        public StubAiApiClient(AiProviderKind kind, bool hasKey)
        {
            Provider = kind;
            HasKey = hasKey;
        }
        public Task<string> SendAsync(string prompt, string jsonSchema, CancellationToken cancellationToken)
            => Task.FromResult(CannedJson);
        public void RefreshKey() { }
        public Task<string?> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
