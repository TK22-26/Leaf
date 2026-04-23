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
    // Match the palette-merge pattern used by MergeMotionTests so the
    // PopoverShow storyboard resolves against Application.Current.Resources
    // (same-thread guarantee) rather than the _localPalette fallback which
    // captured the storyboard on whichever test thread touched it first.
    private static readonly object _paletteLock = new();
    private static bool _paletteMerged;
    private static void EnsureMergeDictionaryMerged()
    {
        lock (_paletteLock)
        {
            if (System.Windows.Application.Current is null)
            {
                try { _ = new System.Windows.Application(); }
                catch (InvalidOperationException) { /* already created */ }
            }
            if (_paletteMerged) return;
            var dict = new System.Windows.ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Leaf;component/Resources/Merge/Merge.xaml",
                    UriKind.Absolute),
            };
            System.Windows.Application.Current!.Resources.MergedDictionaries.Add(dict);
            _paletteMerged = true;
        }
    }

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

        var handler = typeof(BlamePeekPopover).GetMethod(
            "OnPopoverKeyDown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        handler.Invoke(popover, new object[]
        {
            popover,
            new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "h", IntPtr.Zero),
                0,
                System.Windows.Input.Key.Escape)
            {
                RoutedEvent = System.Windows.UIElement.KeyDownEvent,
            },
        });

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

        var handler = typeof(BlamePeekPopover).GetMethod(
            "OnPopoverKeyDown",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        handler.Invoke(popover, new object[]
        {
            popover,
            new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice,
                new System.Windows.Interop.HwndSource(0, 0, 0, 0, 0, "h", IntPtr.Zero),
                0,
                System.Windows.Input.Key.Enter)
            {
                RoutedEvent = System.Windows.UIElement.KeyDownEvent,
            },
        });

        dismissed.Should().BeFalse(
            because: "only Escape dismisses; Enter activates the focused sha link via its Click handler");
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
