#nullable enable
using FluentAssertions;
using Leaf.Models;
using Leaf.Models.Merge;
using Leaf.Services.Merge;
using Leaf.Tests.Fakes;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Tests for Phase 5's AI-assisted resolution flow on <see cref="MergeEditorViewModel"/>.
/// The focus is the VM's orchestration — consent gate, event firing, resolution
/// acceptance, and error surfacing — not the MCP transport (covered separately).
/// </summary>
public class MergeEditorViewModelAiTests
{
    private static MergeDocument DocWithOneConflict()
    {
        var range = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 3),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 9),
            BaseLines: new[] { "baseline" },
            OursLines: new[] { "ours-value" },
            TheirsLines: new[] { "theirs-value" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        return new MergeDocument(
            filePath: "test.cs",
            baseText: string.Empty,
            oursText: string.Empty,
            theirsText: string.Empty,
            initialMergedText: string.Empty,
            baseLines: new[] { "ctx-before", "baseline", "ctx-after" },
            oursLines: new[] { "ctx-before", "ours-value", "ctx-after" },
            theirsLines: new[] { "ctx-before", "theirs-value", "ctx-after" },
            initialMergedLines: new[] { "ctx-before", "ctx-after" },
            ranges: new[] { range },
            lineEnding: "\n",
            hasTrailingNewline: true);
    }

    private static MergeEditorViewModel CreateVm(MergeDocument doc, IAiMergeAssistant? ai)
    {
        var vm = new MergeEditorViewModel(
            new FakeGitService(),
            new FakeClipboardService(),
            new FakeMergeEngine(doc),
            new WordDiffService(),
            ai,
            imageService: null,
            repoPath: "C:/test");
        typeof(MergeEditorViewModel).GetProperty(nameof(vm.Document))!.SetValue(vm, doc);
        return vm;
    }

    [Fact]
    public void RequestAiResolution_WhenAssistantIsNull_IsNoOp()
    {
        var vm = CreateVm(DocWithOneConflict(), ai: null);
        vm.CanRequestAiResolution.Should().BeFalse();
        vm.Invoking(v => v.RequestAiResolutionCommand.Execute(null)).Should().NotThrow();
    }

    [Fact]
    public void RequestAiResolution_WhenConsentMissing_FiresConsentEventAndHoldsRequest()
    {
        var fake = new FakeAiAssistant { IsEnabled = true, IsConsentGiven = false, McpServerPath = "C:/mcp.exe" };
        var vm = CreateVm(DocWithOneConflict(), fake);

        AiConsentRequest? consent = null;
        vm.AiConsentRequested += (_, e) => consent = e;

        vm.RequestAiResolutionCommand.Execute(null);

        consent.Should().NotBeNull();
        consent!.McpServerPath.Should().Be("C:/mcp.exe");
        consent.FilePath.Should().Be("test.cs");
        fake.CallCount.Should().Be(0, "consent hasn't been granted yet");
    }

    [Fact]
    public async Task ResumeAfterConsent_ReplaysPendingRequest()
    {
        var fake = new FakeAiAssistant
        {
            IsEnabled = true,
            IsConsentGiven = false,
            McpServerPath = "C:/mcp.exe",
            Result = new AiResolution("resolved", "because reasons", AiConfidence.High),
        };
        var vm = CreateVm(DocWithOneConflict(), fake);

        var tcs = new TaskCompletionSource<AiResolutionProposal>();
        vm.AiResolutionReceived += (_, e) => tcs.TrySetResult(e);

        vm.RequestAiResolutionCommand.Execute(null);
        // Simulate consent dialog + user accept.
        fake.IsConsentGiven = true;
        vm.ResumeAiRequestAfterConsent();

        var proposal = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        proposal.ProposedText.Should().Be("resolved");
        proposal.Rationale.Should().Be("because reasons");
        proposal.Confidence.Should().Be(AiConfidence.High);
        fake.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AcceptAiResolution_AppliesManualState()
    {
        var fake = new FakeAiAssistant
        {
            IsEnabled = true,
            IsConsentGiven = true,
            McpServerPath = "C:/mcp.exe",
            Result = new AiResolution("final text", string.Empty, AiConfidence.Medium),
        };
        var vm = CreateVm(DocWithOneConflict(), fake);

        var tcs = new TaskCompletionSource<AiResolutionProposal>();
        vm.AiResolutionReceived += (_, e) => tcs.TrySetResult(e);

        vm.RequestAiResolutionCommand.Execute(null);
        var proposal = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        vm.AcceptAiResolution(proposal.RangeIndex, proposal.ProposedText);

        vm.RangeStates.Should().ContainKey(0);
        vm.RangeStates[0].Should().BeOfType<ResolutionState.Manual>()
            .Which.Text.Should().Be("final text");
    }

    [Fact]
    public async Task AssistantException_FiresAiErrorEvent()
    {
        var fake = new FakeAiAssistant
        {
            IsEnabled = true,
            IsConsentGiven = true,
            McpServerPath = "C:/mcp.exe",
            ThrowMessage = "server unreachable",
        };
        var vm = CreateVm(DocWithOneConflict(), fake);

        var tcs = new TaskCompletionSource<string>();
        vm.AiError += (_, msg) => tcs.TrySetResult(msg);

        vm.RequestAiResolutionCommand.Execute(null);

        var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        msg.Should().Be("server unreachable");
    }

    [Fact]
    public void CancelPendingAiRequest_DropsPending()
    {
        var fake = new FakeAiAssistant { IsEnabled = true, IsConsentGiven = false, McpServerPath = "C:/mcp.exe" };
        var vm = CreateVm(DocWithOneConflict(), fake);

        vm.RequestAiResolutionCommand.Execute(null);
        vm.CancelPendingAiRequest();
        // Even if consent is later granted, a bare ResumeAfterConsent shouldn't
        // fire a new call — the pending slot was dropped.
        fake.IsConsentGiven = true;
        vm.ResumeAiRequestAfterConsent();
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public void CanRequestAiResolution_FollowsDocumentAndInFlight()
    {
        var fake = new FakeAiAssistant { IsEnabled = true, IsConsentGiven = true, McpServerPath = "C:/mcp.exe" };
        var vm = CreateVm(DocWithOneConflict(), fake);

        vm.CanRequestAiResolution.Should().BeTrue();
        vm.IsAiRequestInFlight = true;
        vm.CanRequestAiResolution.Should().BeFalse();
        vm.IsAiRequestInFlight = false;
        vm.CanRequestAiResolution.Should().BeTrue();
    }

    [Fact]
    public void ConcurrentRequest_IsGuardedByInFlightFlag()
    {
        var fake = new FakeAiAssistant { IsEnabled = true, IsConsentGiven = true, McpServerPath = "C:/mcp.exe" };
        var vm = CreateVm(DocWithOneConflict(), fake);
        // Simulate a previous click that's still in flight. A second click
        // should be dropped so we don't double-spawn.
        vm.IsAiRequestInFlight = true;
        vm.RequestAiResolutionCommand.Execute(null);
        fake.CallCount.Should().Be(0);
    }

    [Fact]
    public void EngineWithConflictAtFirstLine_ProducesNoContextBefore()
    {
        // A conflict at the top of the file can't have any "before" context.
        // Verify the slice method returns an empty collection rather than
        // throwing or over-reading into line 0.
        var range = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(1, 2),
            Ours: new LineRange(1, 2),
            Theirs: new LineRange(1, 2),
            ResultMarkedRange: new LineRange(1, 8),
            BaseLines: new[] { "baseline" },
            OursLines: new[] { "ours" },
            TheirsLines: new[] { "theirs" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = new MergeDocument(
            "top.cs", string.Empty, string.Empty, string.Empty, string.Empty,
            baseLines: new[] { "baseline", "after" },
            oursLines: new[] { "ours", "after" },
            theirsLines: new[] { "theirs", "after" },
            initialMergedLines: new[] { "after" },
            ranges: new[] { range },
            lineEnding: "\n",
            hasTrailingNewline: true);

        var fake = new FakeAiAssistant
        {
            IsEnabled = true,
            IsConsentGiven = true,
            McpServerPath = "C:/mcp.exe",
            Result = new AiResolution("ok", "r", AiConfidence.High),
        };
        var vm = CreateVm(doc, fake);
        vm.RequestAiResolutionCommand.Execute(null);

        fake.LastRequest.Should().NotBeNull();
        fake.LastRequest!.ContextBefore.Should().BeEmpty();
        fake.LastRequest.ContextAfter.Should().ContainSingle().Which.Should().Be("after");
    }

    [Fact]
    public void EngineWithConflictAtEof_ProducesNoContextAfter()
    {
        // A conflict that consumes the tail of the file has no "after" context.
        var range = new ModifiedBaseRange(
            Index: 0,
            Base: new LineRange(2, 3),
            Ours: new LineRange(2, 3),
            Theirs: new LineRange(2, 3),
            ResultMarkedRange: new LineRange(2, 9),
            BaseLines: new[] { "tail" },
            OursLines: new[] { "ours-tail" },
            TheirsLines: new[] { "theirs-tail" },
            OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
            TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
            IsConflicting: true,
            IsOrderRelevant: true);
        var doc = new MergeDocument(
            "tail.cs", string.Empty, string.Empty, string.Empty, string.Empty,
            baseLines: new[] { "before", "tail" },
            oursLines: new[] { "before", "ours-tail" },
            theirsLines: new[] { "before", "theirs-tail" },
            initialMergedLines: new[] { "before" },
            ranges: new[] { range },
            lineEnding: "\n",
            hasTrailingNewline: true);

        var fake = new FakeAiAssistant
        {
            IsEnabled = true,
            IsConsentGiven = true,
            McpServerPath = "C:/mcp.exe",
            Result = new AiResolution("ok", "r", AiConfidence.High),
        };
        var vm = CreateVm(doc, fake);
        vm.RequestAiResolutionCommand.Execute(null);

        fake.LastRequest.Should().NotBeNull();
        fake.LastRequest!.ContextBefore.Should().ContainSingle().Which.Should().Be("before");
        fake.LastRequest.ContextAfter.Should().BeEmpty();
    }

    /// <summary>Hand-rolled fake; keeps tests free of a mocking library.</summary>
    private sealed class FakeAiAssistant : IAiMergeAssistant
    {
        public bool IsEnabled { get; set; }
        public bool IsConsentGiven { get; set; }
        public string? McpServerPath { get; set; }
        public int CallCount { get; private set; }
        public AiResolution? Result { get; set; }
        public string? ThrowMessage { get; set; }
        public AiResolutionRequest? LastRequest { get; private set; }

        public Task<AiResolution?> RequestResolutionAsync(
            AiResolutionRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (ThrowMessage is not null)
                throw new AiMergeAssistantException(ThrowMessage);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeMergeEngine : IMergeEngine
    {
        private readonly MergeDocument? _doc;
        public FakeMergeEngine(MergeDocument? doc) => _doc = doc;
        public Task<MergeDocument> MergeAsync(
            string filePath, string baseText, string oursText, string theirsText,
            bool ignoreWhitespace = false, string? oursLabel = null, string? theirsLabel = null,
            string? baseLabel = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_doc ?? throw new InvalidOperationException("null doc"));
    }
}
