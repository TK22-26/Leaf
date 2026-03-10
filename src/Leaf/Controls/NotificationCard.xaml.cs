using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentIcons.Common;
using Leaf.Services;

namespace Leaf.Controls;

public partial class NotificationCard : UserControl
{
    private static readonly Brush ErrorIconBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));

    public event EventHandler? CloseRequested;

    static NotificationCard()
    {
        ErrorIconBrush.Freeze();
    }

    public NotificationCard()
    {
        InitializeComponent();
    }

    public void SetContent(string title, string description, NotificationType type)
    {
        TitleText.Text = title;
        DescriptionText.Text = description;

        switch (type)
        {
            case NotificationType.Error:
                TypeIcon.Symbol = Symbol.ErrorCircle;
                TypeIcon.Foreground = ErrorIconBrush;
                break;
            case NotificationType.Warning:
                TypeIcon.Symbol = Symbol.Warning;
                TypeIcon.Foreground = TryFindResource("SystemFillColorCautionBrush") as Brush
                    ?? Brushes.Orange;
                break;
            case NotificationType.Success:
                TypeIcon.Symbol = Symbol.CheckmarkCircle;
                TypeIcon.Foreground = TryFindResource("SystemFillColorSuccessBrush") as Brush
                    ?? Brushes.Green;
                break;
            case NotificationType.Information:
                TypeIcon.Symbol = Symbol.Info;
                TypeIcon.Foreground = TryFindResource("AccentFillColorDefaultBrush") as Brush
                    ?? Brushes.DodgerBlue;
                break;
        }
    }

    private void CardBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Don't expand if clicking the close button area
        if (e.OriginalSource is DependencyObject source && IsChildOfCloseButton(source))
            return;

        DescriptionText.MaxHeight = double.PositiveInfinity;
        CardBody.Cursor = Cursors.Arrow;
    }

    private bool IsChildOfCloseButton(DependencyObject element)
    {
        while (element != null)
        {
            if (ReferenceEquals(element, CloseButton))
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
