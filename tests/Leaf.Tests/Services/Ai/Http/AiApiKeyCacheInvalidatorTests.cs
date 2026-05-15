#nullable enable
using FluentAssertions;
using Leaf.Services.Ai.Http;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Ai.Http;

/// <summary>
/// The DI service that fans <c>Invalidate(credentialProvider)</c> out
/// to the matching <see cref="IAiApiClient"/>. Replaces the
/// per-callsite invalidator callback that the ICR flagged for being
/// wired only on one Settings-entry path.
/// </summary>
public class AiApiKeyCacheInvalidatorTests
{
    [Theory]
    [InlineData("Claude", AiProviderKind.ClaudeApi)]
    [InlineData("Gemini", AiProviderKind.GeminiApi)]
    [InlineData("OpenAI", AiProviderKind.OpenAi)]
    [InlineData("OpenAiCompatible", AiProviderKind.OpenAiCompatible)]
    public void Invalidate_RefreshesMatchingClient(string credentialProvider, AiProviderKind expectedKind)
    {
        var claude = new RecordingClient(AiProviderKind.ClaudeApi);
        var gemini = new RecordingClient(AiProviderKind.GeminiApi);
        var openai = new RecordingClient(AiProviderKind.OpenAi);
        var compat = new RecordingClient(AiProviderKind.OpenAiCompatible);

        var subject = new AiApiKeyCacheInvalidator(new IAiApiClient[] { claude, gemini, openai, compat });
        subject.Invalidate(credentialProvider);

        var clients = new[] { claude, gemini, openai, compat };
        foreach (var c in clients)
        {
            if (c.Provider == expectedKind)
                c.RefreshCount.Should().Be(1, $"the {expectedKind} client should have been refreshed");
            else
                c.RefreshCount.Should().Be(0, $"the {c.Provider} client should NOT have been refreshed");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("claude")]   // case-sensitive — explicit decision
    public void Invalidate_NoOps_OnUnknownProvider(string credentialProvider)
    {
        var c = new RecordingClient(AiProviderKind.ClaudeApi);
        new AiApiKeyCacheInvalidator(new[] { c }).Invalidate(credentialProvider);
        c.RefreshCount.Should().Be(0);
    }

    private sealed class RecordingClient : IAiApiClient
    {
        public RecordingClient(AiProviderKind provider) { Provider = provider; }
        public AiProviderKind Provider { get; }
        public bool HasKey => true;
        public int RefreshCount { get; private set; }
        public void RefreshKey() => RefreshCount++;
        public Task<string> SendAsync(string prompt, string jsonSchema, CancellationToken cancellationToken)
            => Task.FromResult("{}");
        public Task<string?> TestConnectionAsync(CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);
    }
}
