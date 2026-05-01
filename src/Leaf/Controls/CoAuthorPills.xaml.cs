using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Leaf.Models;

namespace Leaf.Controls;

/// <summary>
/// Inline avatar-only pills for a commit's co-authors, each with a
/// rich name + email tooltip. Centralises the bisect / commit-detail
/// co-author rendering so future changes (avatar size, tooltip
/// formatting, hover state) live in one place. The control collapses
/// to zero when <see cref="CoAuthors"/> is empty / null so parents
/// don't need their own visibility plumbing.
/// </summary>
public partial class CoAuthorPills : UserControl
{
    public CoAuthorPills()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Co-authors to render. Bound directly from the commit being
    /// displayed (<see cref="CommitInfo.CoAuthors"/>). When empty or
    /// null the control collapses.
    /// </summary>
    public static readonly DependencyProperty CoAuthorsProperty =
        DependencyProperty.Register(
            nameof(CoAuthors),
            typeof(IEnumerable<CommitInfo.CoAuthorInfo>),
            typeof(CoAuthorPills),
            new PropertyMetadata(null, OnCoAuthorsChanged));

    public IEnumerable<CommitInfo.CoAuthorInfo>? CoAuthors
    {
        get => (IEnumerable<CommitInfo.CoAuthorInfo>?)GetValue(CoAuthorsProperty);
        set => SetValue(CoAuthorsProperty, value);
    }

    /// <summary>
    /// Read-only flag the XAML uses to collapse when there are no
    /// co-authors. Maintained whenever <see cref="CoAuthors"/> changes.
    /// </summary>
    public static readonly DependencyPropertyKey HasCoAuthorsPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasCoAuthors),
            typeof(bool),
            typeof(CoAuthorPills),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasCoAuthorsProperty = HasCoAuthorsPropertyKey.DependencyProperty;

    public bool HasCoAuthors => (bool)GetValue(HasCoAuthorsProperty);

    private static void OnCoAuthorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CoAuthorPills cp) return;
        var hasAny = e.NewValue is IEnumerable enumerable && enumerable.Cast<object>().Any();
        cp.SetValue(HasCoAuthorsPropertyKey, hasAny);
    }
}
