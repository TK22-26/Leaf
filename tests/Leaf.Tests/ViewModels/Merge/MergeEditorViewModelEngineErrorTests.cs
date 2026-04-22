#nullable enable
using System.Threading;
using FluentAssertions;
using Leaf.Models;
using Leaf.Models.Merge;
using Leaf.Services.Merge;
using Leaf.Tests.Fakes;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.ViewModels.Merge;

/// <summary>
/// Pins <see cref="MergeEditorViewModel.BuildDocumentForSelectedAsync"/>'s
/// exception-handling contract. The prior catch was narrow
/// (<see cref="OperationCanceledException"/> + <c>MergeEngineException</c>)
/// and let IOException / InvalidOperationException / UnauthorizedAccessException
/// propagate through FireAndForget, leaving the VM in a half-loaded state:
/// sidebar on the new file, panes still showing the previous one. The
/// broadened catch now surfaces those as IsEngineError with the exception
/// message so the view stays coherent.
/// </summary>
public class MergeEditorViewModelEngineErrorTests
{
    [Fact]
    public async Task BuildDocument_IOException_FromEngine_SurfacesAsEngineError()
    {
        var engine = new ThrowingEngine(new System.IO.IOException("blob stream failed"));
        var vm = new MergeEditorViewModel(
            new FakeGitService(), new FakeClipboardService(), engine, "C:/test");

        vm.Conflicts.Add(new ConflictInfo { FilePath = "foo.cs" });
        vm.SelectedConflict = vm.Conflicts[0];
        await WaitForBuildAsync(vm);

        vm.IsEngineError.Should().BeTrue(
            because: "IOException from the engine must land on the engine-error path, not propagate as an unhandled exception");
        vm.EngineErrorMessage.Should().Contain("blob stream failed");
        vm.Document.Should().BeNull(because: "panes must not keep showing a stale document when the new one failed to load");
    }

    [Fact]
    public async Task BuildDocument_InvalidOperationException_SurfacesAsEngineError()
    {
        var engine = new ThrowingEngine(new InvalidOperationException("document corrupt"));
        var vm = new MergeEditorViewModel(
            new FakeGitService(), new FakeClipboardService(), engine, "C:/test");

        vm.Conflicts.Add(new ConflictInfo { FilePath = "foo.cs" });
        vm.SelectedConflict = vm.Conflicts[0];
        await WaitForBuildAsync(vm);

        vm.IsEngineError.Should().BeTrue();
        vm.EngineErrorMessage.Should().Contain("document corrupt");
    }

    [Fact]
    public async Task BuildDocument_UnauthorizedAccess_SurfacesAsEngineError()
    {
        var engine = new ThrowingEngine(new UnauthorizedAccessException("locked by antivirus"));
        var vm = new MergeEditorViewModel(
            new FakeGitService(), new FakeClipboardService(), engine, "C:/test");

        vm.Conflicts.Add(new ConflictInfo { FilePath = "foo.cs" });
        vm.SelectedConflict = vm.Conflicts[0];
        await WaitForBuildAsync(vm);

        vm.IsEngineError.Should().BeTrue();
        vm.EngineErrorMessage.Should().Contain("locked by antivirus");
    }

    private static async Task WaitForBuildAsync(MergeEditorViewModel vm)
    {
        // BuildDocumentForSelectedAsync is fired via FireAndForget from the
        // SelectedConflict setter; pump the dispatcher briefly so the async
        // engine throw reaches its awaiting continuation on the UI thread.
        for (int i = 0; i < 20 && !vm.IsEngineError; i++)
        {
            await Task.Delay(25).ConfigureAwait(true);
        }
    }

    private sealed class ThrowingEngine : IMergeEngine
    {
        private readonly Exception _toThrow;
        public ThrowingEngine(Exception toThrow) => _toThrow = toThrow;
        public Task<MergeDocument> MergeAsync(
            string filePath, string baseText, string oursText, string theirsText,
            bool ignoreWhitespace = false, string? oursLabel = null, string? theirsLabel = null,
            string? baseLabel = null, CancellationToken cancellationToken = default)
            => Task.FromException<MergeDocument>(_toThrow);
    }
}
