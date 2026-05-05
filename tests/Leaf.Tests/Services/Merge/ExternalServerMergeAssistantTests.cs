#nullable enable
using System.IO;
using System.Text;
using FluentAssertions;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Tests for <see cref="ExternalServerMergeAssistant"/>. The transport layer
/// spawns a real child process + exchanges JSON over stdio, so the tests use a
/// real exe-style shim: a <c>cmd.exe</c> script that echoes canned JSON. This
/// verifies the gating logic + the JSON round-trip without depending on a
/// real provider account.
/// </summary>
public class ExternalServerMergeAssistantTests
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
        var assistant = new ExternalServerMergeAssistant(
            serverPathProvider: () => "C:/nonexistent.exe",
            enabledProvider: () => false,
            consentGivenProvider: () => true);
        var result = await assistant.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNull_WhenConsentMissing()
    {
        var assistant = new ExternalServerMergeAssistant(
            serverPathProvider: () => "C:/nonexistent.exe",
            enabledProvider: () => true,
            consentGivenProvider: () => false);
        var result = await assistant.RequestResolutionAsync(SampleRequest());
        result.Should().BeNull();
    }

    [Fact]
    public async Task Throws_WhenServerPathMissing()
    {
        var assistant = new ExternalServerMergeAssistant(
            serverPathProvider: () => null,
            enabledProvider: () => true,
            consentGivenProvider: () => true);
        var act = () => assistant.RequestResolutionAsync(SampleRequest());
        await act.Should().ThrowAsync<AiMergeAssistantException>()
            .Where(e => e.Message.Contains("External Server"));
    }

    [Fact]
    public async Task Throws_WhenServerPathDoesNotExist()
    {
        var assistant = new ExternalServerMergeAssistant(
            serverPathProvider: () => "C:/this-path-should-never-exist-xyz.exe",
            enabledProvider: () => true,
            consentGivenProvider: () => true);
        var act = () => assistant.RequestResolutionAsync(SampleRequest());
        await act.Should().ThrowAsync<AiMergeAssistantException>();
    }

    [Fact]
    public async Task WrapsWin32Exception_FromProcessStart()
    {
        // A file that exists but isn't a valid PE/executable triggers Win32Exception
        // from Process.Start. The assistant must wrap it in AiMergeAssistantException
        // so the VM's AiError event fires (not the generic AsyncErrorHandler path).
        var notAnExe = Path.Combine(Path.GetTempPath(), $"not-an-exe-{Guid.NewGuid():N}.txt");
        File.WriteAllText(notAnExe, "plain text");
        try
        {
            var assistant = new ExternalServerMergeAssistant(
                serverPathProvider: () => notAnExe,
                enabledProvider: () => true,
                consentGivenProvider: () => true);
            var act = () => assistant.RequestResolutionAsync(SampleRequest());
            var exAssertion = await act.Should().ThrowAsync<AiMergeAssistantException>();
            // We don't assert the inner-exception type — Windows may return
            // different framework exceptions depending on file contents — but
            // we do verify the public exception type is the wrapped one.
            exAssertion.Which.Message.Should().Contain("Could not start external server");
        }
        finally { File.Delete(notAnExe); }
    }

    [Fact]
    public async Task EarlyExitBrokenPipe_DoesNotPreventReadingStdout()
    {
        // This shim exits immediately without consuming stdin; WriteAsync(stdin)
        // can throw IOException/ObjectDisposedException depending on timing. The
        // assistant must still succeed in reading stdout + parsing JSON.
        var shim = CreateBatShim("{\"proposedText\":\"early-exit\",\"rationale\":\"\",\"confidence\":\"medium\"}");
        try
        {
            var assistant = new ExternalServerMergeAssistant(
                serverPathProvider: () => shim,
                enabledProvider: () => true,
                consentGivenProvider: () => true);
            // Run repeatedly to catch the race — any run that hits the broken-pipe
            // path must still return a parsed result, not surface an IOException.
            for (int i = 0; i < 5; i++)
            {
                var result = await assistant.RequestResolutionAsync(SampleRequest());
                result.Should().NotBeNull();
                result!.ProposedText.Should().Be("early-exit");
            }
        }
        finally { File.Delete(shim); }
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
            var assistant = new ExternalServerMergeAssistant(
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
            var assistant = new ExternalServerMergeAssistant(
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
            var assistant = new ExternalServerMergeAssistant(
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
        // IsEnabled now gates on the server path existing on disk too
        // (matches the connection-state pattern from the CLI providers
        // — the router relies on this to dispatch a clear "not connected"
        // error rather than silently falling through). Use a real temp
        // file so the property surface reflects a connected state.
        var serverPath = Path.Combine(Path.GetTempPath(), $"leaf-mock-server-{Guid.NewGuid():N}.bat");
        File.WriteAllText(serverPath, "@echo off\r\n");
        try
        {
            var assistant = new ExternalServerMergeAssistant(
                serverPathProvider: () => serverPath,
                enabledProvider: () => true,
                consentGivenProvider: () => false);
            assistant.IsEnabled.Should().BeTrue();
            assistant.IsConsentGiven.Should().BeFalse();
            assistant.ProviderKind.Should().Be(AiProviderKind.ExternalServer);
            assistant.ProviderDescription.Should().Contain(serverPath);
        }
        finally { File.Delete(serverPath); }
    }

    [Fact]
    public void IsEnabled_FalseWhenServerPathMissing()
    {
        // Connection-state contract: a configured-but-missing server
        // path counts as "not connected" so the router's selected-
        // provider-not-connected branch can fire with a named error,
        // matching the CLI providers' IsProviderConnected behaviour.
        var assistant = new ExternalServerMergeAssistant(
            serverPathProvider: () => "C:/does-not-exist-on-disk.exe",
            enabledProvider: () => true,
            consentGivenProvider: () => true);
        assistant.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ProviderDescription_FallsBackWhenNoPathConfigured()
    {
        var assistant = new ExternalServerMergeAssistant(
            serverPathProvider: () => null,
            enabledProvider: () => true,
            consentGivenProvider: () => true);
        assistant.ProviderDescription.Should().Contain("no path configured");
    }

    /// <summary>
    /// Writes a temporary .bat file that echoes the given JSON to stdout and exits 0.
    /// Uses .bat because Windows can't directly Process.Start an arbitrary script;
    /// a batch file is the minimum fixture that exercises the full transport path.
    /// </summary>
    private static string CreateBatShim(string jsonOutput)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leaf-merge-server-test-{Guid.NewGuid():N}.bat");
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
