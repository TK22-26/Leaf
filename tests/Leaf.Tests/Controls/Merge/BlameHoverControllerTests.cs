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
    // Match the palette-merge pattern used by BlamePeekPopoverTests so the
    // PopoverShow storyboard clone runs on the same STA dispatcher thread
    // that the popover's Focus+animation path uses.
    private static readonly object _paletteLock = new();
    private static bool _paletteMerged;
    private static void EnsureMergeDictionaryMerged()
    {
        lock (_paletteLock)
        {
            if (Application.Current is null)
            {
                try { _ = new Application(); }
                catch (InvalidOperationException) { /* already created */ }
            }
            if (_paletteMerged) return;
            var dict = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Leaf;component/Resources/Merge/Merge.xaml",
                    UriKind.Absolute),
            };
            Application.Current!.Resources.MergedDictionaries.Add(dict);
            _paletteMerged = true;
        }
    }

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
