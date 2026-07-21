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
    // Storyboard-clone needs Application.Current.Resources populated on
    // the STA test thread — delegated to the shared fixture.
    private static void EnsureMergeDictionaryMerged() => MergePaletteTestFixture.Ensure();

    [StaFact]
    public void SetRecord_CopiesFieldsFromBlameLine()
    {
        EnsureMergeDictionaryMerged();
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
        EnsureMergeDictionaryMerged();
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
    public void Popover_IsFocusable_SoKeyboardUsersCanTabIntoIt()
    {
        // Regression guard for the keyboard a11y fix: the popover used to
        // have Focusable="False", blocking keyboard users from reaching
        // the sha Hyperlink. Now the root is focusable with Cycle tab
        // navigation; the Hyperlink inside is natively focusable and
        // becomes the first tab-stop.
        var popover = new BlamePeekPopover();
        popover.Focusable.Should().BeTrue(
            because: "keyboard users must be able to Tab into the popover to reach the sha link");
    }

    [StaFact]
    public void DismissRequested_Fires_OnEscapeKeyDown()
    {
        // Raise the KeyDown routed event through WPF's event system
        // (popover.RaiseEvent) rather than reflecting into the private
        // handler. This validates the XAML binding `KeyDown="OnPopoverKeyDown"`
        // is still wired — a previous reflection-based draft passed even
        // if the XAML attribute were deleted.
        EnsureMergeDictionaryMerged();
        var popover = new BlamePeekPopover();
        popover.SetRecord(new FileBlameLine
        {
            Sha = "abcdef1234567890",
            Author = "Alice",
            Subject = "Fix",
        });

        bool dismissed = false;
        popover.DismissRequested += (_, _) => dismissed = true;

        RaiseKeyEvent(popover, System.Windows.Input.Key.Escape);

        dismissed.Should().BeTrue(because: "Escape inside the popover must fire DismissRequested");
    }

    [StaFact]
    public void DismissRequested_DoesNotFire_OnNonEscapeKey()
    {
        EnsureMergeDictionaryMerged();
        var popover = new BlamePeekPopover();
        popover.SetRecord(new FileBlameLine { Sha = "abc", Author = "A", Subject = "s" });

        bool dismissed = false;
        popover.DismissRequested += (_, _) => dismissed = true;

        RaiseKeyEvent(popover, System.Windows.Input.Key.Enter);

        dismissed.Should().BeFalse(
            because: "only Escape dismisses; Enter activates the focused sha link via its Click handler");
    }

    private static void RaiseKeyEvent(System.Windows.UIElement target, System.Windows.Input.Key key)
    {
        // A KeyEventArgs needs a real PresentationSource; in a headless
        // STA test we haven't shown a Window. The HwndSource is created
        // and disposed PER CALL on the current test's STA thread: an
        // earlier shared-static source bound itself to whichever xunit
        // STA thread touched the class first, and once that thread's
        // dispatcher exited the dead hwnd made RaiseEvent throw
        // NullReferenceException intermittently (order/load-dependent
        // flake). Per-call lifetime is thread-correct and also disposes
        // the native handle deterministically instead of leaking it.
        using var source = new System.Windows.Interop.HwndSource(
            new System.Windows.Interop.HwndSourceParameters("leaf-merge-test-keys"));
        var args = new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            source,
            timestamp: 0,
            key)
        {
            RoutedEvent = System.Windows.UIElement.KeyDownEvent,
            Source = target,
        };
        target.RaiseEvent(args);
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
