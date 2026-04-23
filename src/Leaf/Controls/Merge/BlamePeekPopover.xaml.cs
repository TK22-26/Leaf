#nullable enable
using System.Windows;
using System.Windows.Controls;
using Leaf.Models;

namespace Leaf.Controls.Merge;

/// <summary>
/// Hover popover consumed by <c>ReadOnlyMergePane</c> / <c>ResultPane</c>
/// to surface blame metadata for the line the pointer is dwelling on.
/// Owns no git plumbing — callers feed it a pre-resolved
/// <see cref="FileBlameLine"/> via <see cref="SetRecord"/>, and the popover
/// raises <see cref="CommitRequested"/> with the full sha when the user
/// clicks the short-sha link.
/// </summary>
/// <remarks>
/// Presentation-only by design: the debounce + cache lives in
/// <see cref="Leaf.Services.Merge.IMergeBlameService"/> so the control
/// stays testable without a git context and reusable for any future
/// hover surface (AI proposals, conflict-range notes).
/// </remarks>
public partial class BlamePeekPopover : UserControl
{
    public static readonly DependencyProperty AuthorProperty = DependencyProperty.Register(
        nameof(Author), typeof(string), typeof(BlamePeekPopover),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RelativeDateProperty = DependencyProperty.Register(
        nameof(RelativeDate), typeof(string), typeof(BlamePeekPopover),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubjectProperty = DependencyProperty.Register(
        nameof(Subject), typeof(string), typeof(BlamePeekPopover),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShortShaProperty = DependencyProperty.Register(
        nameof(ShortSha), typeof(string), typeof(BlamePeekPopover),
        new PropertyMetadata(string.Empty));

    public string Author
    {
        get => (string)GetValue(AuthorProperty);
        set => SetValue(AuthorProperty, value);
    }

    public string RelativeDate
    {
        get => (string)GetValue(RelativeDateProperty);
        set => SetValue(RelativeDateProperty, value);
    }

    public string Subject
    {
        get => (string)GetValue(SubjectProperty);
        set => SetValue(SubjectProperty, value);
    }

    public string ShortSha
    {
        get => (string)GetValue(ShortShaProperty);
        set => SetValue(ShortShaProperty, value);
    }

    /// <summary>Full sha of the commit being peeked. Used in <see cref="CommitRequested"/>.</summary>
    public string FullSha { get; private set; } = string.Empty;

    /// <summary>
    /// Raised when the user clicks the short-sha link. Callers forward this
    /// to whatever surface navigates the commit graph (MainViewModel.SelectCommitBySha).
    /// </summary>
    public event EventHandler<string>? CommitRequested;

    public BlamePeekPopover()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Populate the popover from a <see cref="FileBlameLine"/> record.
    /// Running the animation here (rather than in the host's Popup.Opened)
    /// keeps the "data in → anim fires" contract obvious.
    /// </summary>
    public void SetRecord(FileBlameLine record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Author = record.Author;
        RelativeDate = record.DateDisplay;
        Subject = record.Subject;
        ShortSha = record.ShortSha;
        FullSha = record.Sha;
        // Kick the 200 ms fade + 2 px translate from plan §D3 PopoverShow.
        MergeMotionHelpers.PlayPopoverShow(this);
    }

    private void OnShaLinkClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(FullSha))
        {
            CommitRequested?.Invoke(this, FullSha);
        }
        e.Handled = true;
    }
}
