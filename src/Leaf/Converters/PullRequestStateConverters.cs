using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Converters;

/// <summary>
/// Converts a <see cref="PullRequestState"/> to a status indicator brush.
/// Open = green, Closed = red, Merged = purple, Draft = gray.
/// </summary>
public class PullRequestStateToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush OpenBrush   = new(Color.FromRgb(0x28, 0xA7, 0x45)); // #28A745
    private static readonly SolidColorBrush ClosedBrush = new(Color.FromRgb(0xCF, 0x22, 0x2E)); // #CF222E
    private static readonly SolidColorBrush MergedBrush = new(Color.FromRgb(0x82, 0x50, 0xDF)); // #8250DF
    private static readonly SolidColorBrush DraftBrush  = new(Color.FromRgb(0x6E, 0x77, 0x81)); // #6E7781
    private static readonly SolidColorBrush GrayBrush   = new(Color.FromRgb(0x6E, 0x77, 0x81));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PullRequestState state)
        {
            return state switch
            {
                PullRequestState.Open   => OpenBrush,
                PullRequestState.Closed => ClosedBrush,
                PullRequestState.Merged => MergedBrush,
                PullRequestState.Draft  => DraftBrush,
                _                       => GrayBrush
            };
        }

        return GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Converts a <see cref="PullRequestState"/> to a human-readable display string.
/// </summary>
public class PullRequestStateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PullRequestState state)
        {
            return state switch
            {
                PullRequestState.Open   => "Open",
                PullRequestState.Closed => "Closed",
                PullRequestState.Merged => "Merged",
                PullRequestState.Draft  => "Draft",
                _                       => state.ToString()
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Converts a <see cref="CheckStatus"/> to a color brush.
/// </summary>
public class CheckStatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x28, 0xA7, 0x45));
    private static readonly SolidColorBrush FailureBrush = new(Color.FromRgb(0xCF, 0x22, 0x2E));
    private static readonly SolidColorBrush PendingBrush = new(Color.FromRgb(0xDB, 0xAB, 0x09));
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromRgb(0x6E, 0x77, 0x81));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Success => SuccessBrush,
                CheckStatus.Failure or CheckStatus.Error => FailureBrush,
                CheckStatus.Pending => PendingBrush,
                _ => NeutralBrush
            };
        }
        return NeutralBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Converts a <see cref="CheckStatus"/> to a FluentIcon symbol name.
/// </summary>
public class CheckStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Success => FluentIcons.Common.Symbol.CheckmarkCircle,
                CheckStatus.Failure or CheckStatus.Error => FluentIcons.Common.Symbol.DismissCircle,
                CheckStatus.Pending => FluentIcons.Common.Symbol.Clock,
                CheckStatus.Cancelled => FluentIcons.Common.Symbol.SubtractCircle,
                _ => FluentIcons.Common.Symbol.Circle
            };
        }
        return FluentIcons.Common.Symbol.Circle;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Converts a <see cref="PullRequestReviewState"/> to a color brush.
/// </summary>
public class ReviewStateToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush ApprovedBrush = new(Color.FromRgb(0x28, 0xA7, 0x45));
    private static readonly SolidColorBrush ChangesBrush = new(Color.FromRgb(0xCF, 0x22, 0x2E));
    private static readonly SolidColorBrush CommentedBrush = new(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly SolidColorBrush ReviewPendingBrush = new(Color.FromRgb(0xDB, 0xAB, 0x09));
    private static readonly SolidColorBrush DismissedBrush = new(Color.FromRgb(0x6E, 0x77, 0x81));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PullRequestReviewState state)
        {
            return state switch
            {
                PullRequestReviewState.Approved => ApprovedBrush,
                PullRequestReviewState.ChangesRequested => ChangesBrush,
                PullRequestReviewState.Commented => CommentedBrush,
                PullRequestReviewState.Pending => ReviewPendingBrush,
                PullRequestReviewState.Dismissed => DismissedBrush,
                _ => DismissedBrush
            };
        }
        return DismissedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Converts a <see cref="PullRequestReviewState"/> to display text.
/// </summary>
public class ReviewStateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is PullRequestReviewState state)
        {
            return state switch
            {
                PullRequestReviewState.Approved => "Approved",
                PullRequestReviewState.ChangesRequested => "Changes requested",
                PullRequestReviewState.Commented => "Commented",
                PullRequestReviewState.Pending => "Pending",
                PullRequestReviewState.Dismissed => "Dismissed",
                _ => state.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
