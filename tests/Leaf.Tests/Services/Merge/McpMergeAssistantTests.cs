#nullable enable
using System.IO;
using System.Text;
using FluentAssertions;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Tests for <see cref="McpMergeAssistant"/>. The transport layer spawns a real
/// child process + exchanges JSON over stdio, so the tests use a real exe-style
/// shim: a <c>cmd.exe</c> script that echoes canned JSON. This verifies the
/// gating logic + the JSON round-trip without depending on an Anthropic account.
/// </summary>
public class McpMergeAssistantTests
{
    private static AiResolutionRequest SampleRequest() => new(
        FilePath: "test.cs",
        Language: "csharp",
        BaseLines: new[] { "b1" },
        OursLines: new[] { "o1" },
        TheirsLines: new[] { "t1" },
        ContextBefore: Array.Empty<string>(),
        ContextAfter: Array.Empty<string>());

    [Fact]
    public async Task ReturnsNull_WhenFeatureDisabled()
    {
        var assistant = new McpMergeAssistant(
            serverPathProvider: () => "C:/nonexistent.exe",
            enabledProvider: () => false,
            consentGivenProvider: () => true);
        var result = await assistant.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenConsentMissing()
    {
        var assistant = new McpMergeAssistant(
            serverPathProvider: () => "C:/nonexistent.exe",
            enabledProvider: () => true,
            consentGivenProvider: () => false);
        var result = await assistant.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task Throws_WhenServerPathMissing()
    {
        var assistant = new McpMergeAssistant(
            serverPathProvider: () => null,
            enabledProvider: () => true,
            consentGivenProvider: () => true);
        var act = () => assistant.RequestResolutionAsync(SampleRequest());
        await act.Should().ThrowAsync<AiMergeAssistantException>()
            .Where(e => e.Message.Contains("MCP server"));
    }

    [Fact]
    public async Task Throws_WhenServerPathDoesNotExist()
    {
        var assistant = new McpMergeAssistant(
            serverPathProvider: () => "C:/this-path-should-never-exist-xyz.exe",
            enabledProvider: () => true,
            consentGivenProvider: () => true);
        var act = () => assistant.RequestResolutionAsync(SampleRequest());
        await act.Should().ThrowAsync<AiMergeAssistantException>();
    }

    [Fact]
    public async Task SuccessfulInvocation_ParsesResponse()
    {
        // Shim is a batch file that emits a canned JSON response and exits 0.
        // Using a real process keeps the transport machinery under test (not
        // mocked) — catches regressions in cancellation wiring, encoding, etc.
        var shim = CreateBatShim("{\"proposedText\":\"fixed line\",\"rationale\":\"ok\",\"confidence\":\"high\"}");
        try
        {
            var assistant = new McpMergeAssistant(
                serverPathProvider: () => shim,
                enabledProvider: () => true,
                consentGivenProvider: () => true);
            var result = await assistant.RequestResolutionAsync(SampleRequest());
            result.Should().NotBeNull();
            result!.ProposedText.Should().Be("fixed line");
            result.Rationale.Should().Be("ok");
            result.Confidence.Should().Be(AiConfidence.High);
        }
        finally
        {
            File.Delete(shim);
        }
    }

    [Fact]
    public async Task MalformedJson_ThrowsAssistantException()
    {
        var shim = CreateBatShim("not-json");
        try
        {
            var assistant = new McpMergeAssistant(
                serverPathProvider: () => shim,
                enabledProvider: () => true,
                consentGivenProvider: () => true);
            var act = () => assistant.RequestResolutionAsync(SampleRequest());
            await act.Should().ThrowAsync<AiMergeAssistantException>()
                .Where(e => e.Message.Contains("malformed JSON"));
        }
        finally { File.Delete(shim); }
    }

    [Fact]
    public async Task EmptyProposedText_ThrowsAssistantException()
    {
        var shim = CreateBatShim("{\"proposedText\":\"\",\"rationale\":\"\",\"confidence\":\"low\"}");
        try
        {
            var assistant = new McpMergeAssistant(
                serverPathProvider: () => shim,
                enabledProvider: () => true,
                consentGivenProvider: () => true);
            var act = () => assistant.RequestResolutionAsync(SampleRequest());
            await act.Should().ThrowAsync<AiMergeAssistantException>()
                .Where(e => e.Message.Contains("empty resolution"));
        }
        finally { File.Delete(shim); }
    }

    [Fact]
    public void ExposesSettings_ForViewQueries()
    {
        var assistant = new McpMergeAssistant(
            serverPathProvider: () => "C:/mcp.exe",
            enabledProvider: () => true,
            consentGivenProvider: () => false);
        assistant.IsEnabled.Should().BeTrue();
        assistant.IsConsentGiven.Should().BeFalse();
        assistant.McpServerPath.Should().Be("C:/mcp.exe");
    }

    /// <summary>
    /// Writes a temporary .bat file that echoes the given JSON to stdout and exits 0.
    /// Uses .bat because Windows can't directly Process.Start an arbitrary script;
    /// a batch file is the minimum fixture that exercises the full transport path.
    /// </summary>
    private static string CreateBatShim(string jsonOutput)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leaf-mcp-test-{Guid.NewGuid():N}.bat");
        // @echo off suppresses the "C:\>echo ..." prefix; redirect stdout to the
        // given JSON and exit cleanly. We intentionally don't read stdin — the
        // assistant closes it before reading stdout, which is sufficient.
        var content = "@echo off\r\n"
            + $"echo {jsonOutput}\r\n"
            + "exit /b 0\r\n";
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }
}
