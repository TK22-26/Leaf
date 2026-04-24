#nullable enable
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models;
using Leaf.Services.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Exercises the keyboard-triggered path (<see cref="BlameHoverController.ShowForLineAsync"/>)
/// and the early-return guards. The hover-path is covered indirectly through
/// <see cref="BlamePeekPopoverTests"/> and <see cref="MergeBlameServiceTests"/>;
/// this file pins the public keyboard surface that Alt+B binds to.
/// </summary>
public class BlameHoverControllerTests
{
    private static void EnsureMergeDictionaryMerged() => MergePaletteTestFixture.Ensure();

    [StaFact]
    public async Task ShowForLineAsync_EmptyRepoPath_ReturnsWithoutFetching()
    {
        EnsureMergeDictionaryMerged();
        var service = new RecordingBlameService();
        var controller = new BlameHoverController(
            service,
            repoPathProvider: () => string.Empty,
            filePathProvider: () => "foo.cs",
            commitRequestedCallback: _ => { });

        await controller.ShowForLineAsync(new ContentControl(), 1);

        service.CallCount.Should().Be(0,
            because: "an empty repoPath short-circuits before the service is called");
    }

    [StaFact]
    public async Task ShowForLineAsync_EmptyFilePath_ReturnsWithoutFetching()
    {
        EnsureMergeDictionaryMerged();
        var service = new RecordingBlameService();
        var controller = new BlameHoverController(
            service,
            repoPathProvider: () => "/repo",
            filePathProvider: () => null,
            commitRequestedCallback: _ => { });

        await controller.ShowForLineAsync(new ContentControl(), 1);

        service.CallCount.Should().Be(0);
    }

    [StaFact]
    public async Task ShowForLineAsync_LineZeroOrBelow_ReturnsWithoutFetching()
    {
        EnsureMergeDictionaryMerged();
        var service = new RecordingBlameService();
        var controller = new BlameHoverController(
            service,
            repoPathProvider: () => "/repo",
            filePathProvider: () => "foo.cs",
            commitRequestedCallback: _ => { });

        await controller.ShowForLineAsync(new ContentControl(), 0);
        await controller.ShowForLineAsync(new ContentControl(), -5);

        service.CallCount.Should().Be(0,
            because: "invalid line numbers short-circuit so the service isn't probed needlessly");
    }

    [StaFact]
    public async Task ShowForLineAsync_ValidInput_InvokesService()
    {
        EnsureMergeDictionaryMerged();
        var service = new RecordingBlameService();
        var controller = new BlameHoverController(
            service,
            repoPathProvider: () => "/repo",
            filePathProvider: () => "foo.cs",
            commitRequestedCallback: _ => { });

        await controller.ShowForLineAsync(new ContentControl(), 7);

        service.CallCount.Should().Be(1);
        service.LastLine.Should().Be(7);
    }

    [StaFact]
    public void Dispose_DetachesHandlersFromTrackedPanes()
    {
        // Regression guard: TrackPane attaches three handlers (Enter/
        // Move/Leave) per pane, and Dispose must detach them all.
        // Without explicit detach the MouseMove lambda (which captures
        // `resolver`) would keep the disposed controller alive through
        // the pane's event subscription — a real leak if panes outlive
        // their controller (e.g. editor reopen).
        EnsureMergeDictionaryMerged();
        var service = new RecordingBlameService();
        var controller = new BlameHoverController(
            service,
            repoPathProvider: () => "/repo",
            filePathProvider: () => "foo.cs",
            commitRequestedCallback: _ => { });

        var pane = new ContentControl();
        controller.TrackPane(pane, (_, _) => 1);

        var subsField = typeof(BlameHoverController)
            .GetField("_paneSubscriptions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var subs = (System.Collections.ICollection)subsField.GetValue(controller)!;
        subs.Count.Should().Be(1, because: "TrackPane should record one subscription tuple");

        controller.Dispose();

        subs.Count.Should().Be(0,
            because: "Dispose must detach every subscription so a stale controller can't respond to future pane events");
    }

    [StaFact]
    public void TrackedPane_MouseEnter_UpdatesCurrentHoveredPane()
    {
        // Cross-pane transit invariant: the controller tracks which pane
        // holds the pointer so OnPaneMouseLeave's deferred dismiss can
        // tell "pointer went outside" from "pointer went to sibling pane".
        // Without this, moving Ours → Theirs produces a close-then-reopen
        // flicker while the 500 ms debounce runs.
        EnsureMergeDictionaryMerged();
        var service = new RecordingBlameService();
        var controller = new BlameHoverController(
            service,
            repoPathProvider: () => "/repo",
            filePathProvider: () => "foo.cs",
            commitRequestedCallback: _ => { });

        var paneA = new ContentControl();
        var paneB = new ContentControl();
        controller.TrackPane(paneA, (_, _) => 1);
        controller.TrackPane(paneB, (_, _) => 1);

        var currentField = typeof(BlameHoverController)
            .GetField("_currentHoveredPane",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        // Sanity: nothing hovered yet.
        currentField.GetValue(controller).Should().BeNull();

        paneA.RaiseEvent(MakeMouseEvent(paneA, System.Windows.Input.Mouse.MouseEnterEvent));
        currentField.GetValue(controller).Should().BeSameAs(paneA,
            because: "MouseEnter on paneA sets it as the current hovered pane");

        // Cross-pane transit: paneA.MouseLeave fires, then paneB.MouseEnter.
        paneA.RaiseEvent(MakeMouseEvent(paneA, System.Windows.Input.Mouse.MouseLeaveEvent));
        paneB.RaiseEvent(MakeMouseEvent(paneB, System.Windows.Input.Mouse.MouseEnterEvent));
        currentField.GetValue(controller).Should().BeSameAs(paneB,
            because: "after transit to sibling pane the tracker follows the pointer, not a null");

        // Leaving paneB with no sibling entry clears the tracker.
        paneB.RaiseEvent(MakeMouseEvent(paneB, System.Windows.Input.Mouse.MouseLeaveEvent));
        currentField.GetValue(controller).Should().BeNull(
            because: "leaving the last tracked pane clears the tracker so the deferred dismiss can run");
    }

    private static System.Windows.Input.MouseEventArgs MakeMouseEvent(
        System.Windows.IInputElement source,
        System.Windows.RoutedEvent routedEvent)
    {
        // Mouse events need a MouseDevice + timestamp + PresentationSource;
        // since tests don't host a Window, construct a dummy HwndSource
        // that matches the same pattern the KeyEvent tests use.
        return new System.Windows.Input.MouseEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice,
            timestamp: 0)
        {
            RoutedEvent = routedEvent,
            Source = source,
        };
    }

    [StaFact]
    public async Task ShowForLineAsync_SwallowsServiceException()
    {
        // Fire-and-forget contract: the keybinding handler discards the
        // Task, so any exception here would land on UnobservedTaskException.
        // The broad catch must keep the task faulted-free even when the
        // service throws.
        EnsureMergeDictionaryMerged();
        var service = new RecordingBlameService
        {
            ThrowOnNextCall = new InvalidOperationException("blame failed"),
        };
        var controller = new BlameHoverController(
            service,
            repoPathProvider: () => "/repo",
            filePathProvider: () => "foo.cs",
            commitRequestedCallback: _ => { });

        await FluentActions.Awaiting(() =>
            controller.ShowForLineAsync(new ContentControl(), 1)).Should().NotThrowAsync();
    }

    private sealed class RecordingBlameService : IMergeBlameService
    {
        public int CallCount { get; private set; }
        public int LastLine { get; private set; }
        public Exception? ThrowOnNextCall { get; set; }

        public Task<FileBlameLine?> GetLineBlameAsync(string repoPath, string filePath, int oneBasedLineNumber, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastLine = oneBasedLineNumber;
            if (ThrowOnNextCall is { } ex)
            {
                ThrowOnNextCall = null;
                throw ex;
            }
            return Task.FromResult<FileBlameLine?>(null);
        }

        public void InvalidateRepo(string repoPath) { }
    }
}
