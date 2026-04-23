#nullable enable
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for the C5 <see cref="BlamePeekPopover"/> control.
/// Presentation-only — we verify SetRecord populates the DPs and the
/// sha click raises CommitRequested with the full sha (not the short one
/// the UI shows).
/// </summary>
public class BlamePeekPopoverTests
{
    [StaFact]
    public void SetRecord_CopiesFieldsFromBlameLine()
    {
        var popover = new BlamePeekPopover();
        var record = new FileBlameLine
        {
            LineNumber = 42,
            Sha = "abcdef1234567890",
            Author = "Alice",
            Date = DateTimeOffset.UtcNow.AddHours(-2),
            Subject = "Fix the thing",
            Content = "some line",
        };

        popover.SetRecord(record);

        popover.Author.Should().Be("Alice");
        popover.Subject.Should().Be("Fix the thing");
        popover.ShortSha.Should().Be("abcdef1");
        popover.FullSha.Should().Be("abcdef1234567890");
        popover.RelativeDate.Should().Contain("h ago");
    }

    [StaFact]
    public void CommitRequested_FiresWithFullSha_NotShortSha()
    {
        var popover = new BlamePeekPopover();
        popover.SetRecord(new FileBlameLine
        {
            Sha = "abcdef1234567890",
            Author = "Alice",
            Subject = "x",
        });

        string? captured = null;
        popover.CommitRequested += (_, sha) => captured = sha;

        // Simulate the Hyperlink click by invoking the private handler
        // through reflection; the popover's own Click binding does the
        // same thing at runtime. This keeps the test independent of WPF
        // routed-event plumbing.
        var handler = typeof(BlamePeekPopover).GetMethod(
            "OnShaLinkClicked",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        handler.Invoke(popover, new object[]
        {
            popover,
            new System.Windows.RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent),
        });

        captured.Should().Be("abcdef1234567890",
            because: "the graph navigator needs the full sha to disambiguate short-sha collisions");
    }

    [StaFact]
    public void CommitRequested_DoesNotFire_WhenFullShaIsEmpty()
    {
        var popover = new BlamePeekPopover();
        // Intentionally do not call SetRecord — FullSha remains empty.
        bool fired = false;
        popover.CommitRequested += (_, _) => fired = true;

        var handler = typeof(BlamePeekPopover).GetMethod(
            "OnShaLinkClicked",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        handler.Invoke(popover, new object[]
        {
            popover,
            new System.Windows.RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent),
        });

        fired.Should().BeFalse(because: "no sha to navigate to should not fire the event");
    }
}
