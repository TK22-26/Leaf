#nullable enable
using System.Threading;
using FluentAssertions;
using Leaf.Models.Merge;
using Leaf.Services.Merge;
using Leaf.Tests.Fakes;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Tests for the C4 context-menu commands added to
/// <see cref="MergeEditorViewModel"/>: <c>CopySelection</c>,
/// <c>CopyOursVersion</c>, <c>CopyTheirsVersion</c>. These wrap
/// <see cref="Leaf.Services.IClipboardService"/> so the assertions follow
/// the same capture-through-fake pattern used elsewhere.
/// </summary>
public class MergeEditorViewModelCopyContextMenuTests
{
    [Fact]
    public void CopySelection_WithText_SendsThroughClipboard()
    {
        var clip = new FakeClipboardService();
        var vm = CreateVm(clip);

        vm.CopySelectionCommand.Execute("hello world");

        clip.LastText.Should().Be("hello world");
    }

    [Fact]
    public void CopySelection_WithNullText_IsNoOp()
    {
        var clip = new FakeClipboardService();
        var vm = CreateVm(clip);

        vm.CopySelectionCommand.Execute(null);

        clip.LastText.Should().BeNull(
            because: "null selection is the no-right-click-selection case, not a clipboard clear");
    }

    [Fact]
    public void CopySelection_WithEmptyString_IsNoOp()
    {
        var clip = new FakeClipboardService();
        var vm = CreateVm(clip);

        vm.CopySelectionCommand.Execute(string.Empty);

        clip.LastText.Should().BeNull();
    }

    [Fact]
    public void CopyOursVersion_BeforeDocumentLoaded_IsNoOp()
    {
        var clip = new FakeClipboardService();
        var vm = CreateVm(clip);
        vm.Document.Should().BeNull("fixture VM has no document");

        vm.CopyOursVersionCommand.Execute(null);

        clip.LastText.Should().BeNull(
            because: "without a document there's no Ours text to copy");
    }

    [Fact]
    public void CopyOursVersion_WithDocument_CopiesOursText()
    {
        var clip = new FakeClipboardService();
        var vm = CreateVm(clip);
        SetDocument(vm, oursText: "ours-content", theirsText: "theirs-content");

        vm.CopyOursVersionCommand.Execute(null);

        clip.LastText.Should().Be("ours-content");
    }

    [Fact]
    public void CopyTheirsVersion_WithDocument_CopiesTheirsText()
    {
        var clip = new FakeClipboardService();
        var vm = CreateVm(clip);
        SetDocument(vm, oursText: "ours-content", theirsText: "theirs-content");

        vm.CopyTheirsVersionCommand.Execute(null);

        clip.LastText.Should().Be("theirs-content");
    }

    private static MergeEditorViewModel CreateVm(FakeClipboardService clip)
    {
        return new MergeEditorViewModel(
            new FakeGitService(),
            clip,
            new FakeMergeEngine(),
            "C:/test");
    }

    private static void SetDocument(MergeEditorViewModel vm, string oursText, string theirsText)
    {
        var doc = new MergeDocument(
            filePath: "f.cs",
            baseText: "base-content",
            oursText: oursText,
            theirsText: theirsText,
            initialMergedText: "",
            baseLines: Array.Empty<string>(),
            oursLines: Array.Empty<string>(),
            theirsLines: Array.Empty<string>(),
            initialMergedLines: Array.Empty<string>(),
            ranges: Array.Empty<ModifiedBaseRange>(),
            lineEnding: "\n",
            hasTrailingNewline: true);
        typeof(MergeEditorViewModel).GetProperty(nameof(vm.Document))!.SetValue(vm, doc);
    }

    private sealed class FakeMergeEngine : IMergeEngine
    {
        public Task<MergeDocument> MergeAsync(
            string filePath, string baseText, string oursText, string theirsText,
            bool ignoreWhitespace = false, string? oursLabel = null, string? theirsLabel = null,
            string? baseLabel = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new MergeDocument(
                filePath, baseText, oursText, theirsText, "",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), Array.Empty<ModifiedBaseRange>(), "\n", true));
    }
}
