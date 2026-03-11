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
    private static readonly Brush ErrorAccentBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));
    private static readonly Brush WarningAccentBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xA3, 0x3B));
    private static readonly Brush SuccessAccentBrush = new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45));
    private static readonly Brush InfoAccentBrush = new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45));
    private ICommand? _clickCommand;
    private object? _clickCommandParameter;
    private NotificationAction? _primaryAction;

    public event EventHandler? CloseRequested;

    static NotificationCard()
    {
        ErrorIconBrush.Freeze();
        ErrorAccentBrush.Freeze();
        WarningAccentBrush.Freeze();
        SuccessAccentBrush.Freeze();
        InfoAccentBrush.Freeze();
    }

    public NotificationCard()
    {
        InitializeComponent();
    }

    public void SetContent(
        string title,
        string description,
        NotificationType type,
        ICommand? clickCommand = null,
        object? clickCommandParameter = null,
        IReadOnlyList<NotificationAction>? actions = null)
    {
        TitleText.Text = title;
        DescriptionText.Text = description;
        _clickCommand = clickCommand;
        _clickCommandParameter = clickCommandParameter;
        _primaryAction = actions?.FirstOrDefault();
        CardBody.Cursor = _clickCommand != null || _primaryAction != null ? Cursors.Hand : Cursors.Arrow;
        DescriptionText.MaxHeight = 54;

        switch (type)
        {
            case NotificationType.Error:
                TypeIcon.Symbol = Symbol.ErrorCircle;
                TypeIcon.Foreground = ErrorIconBrush;
                AccentStrip.Background = ErrorAccentBrush;
                break;
            case NotificationType.Warning:
                TypeIcon.Symbol = Symbol.Warning;
                TypeIcon.Foreground = TryFindResource("SystemFillColorCautionBrush") as Brush
                    ?? Brushes.Orange;
                AccentStrip.Background = WarningAccentBrush;
                break;
            case NotificationType.Success:
                TypeIcon.Symbol = Symbol.CheckmarkCircle;
                TypeIcon.Foreground = TryFindResource("SystemFillColorSuccessBrush") as Brush
                    ?? Brushes.Green;
                AccentStrip.Background = SuccessAccentBrush;
                break;
            case NotificationType.Information:
                TypeIcon.Symbol = Symbol.Info;
                TypeIcon.Foreground = TryFindResource("AccentFillColorDefaultBrush") as Brush
                    ?? Brushes.DodgerBlue;
                AccentStrip.Background = InfoAccentBrush;
                break;
        }
    }

    private void CardBody_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsChildOfCloseButton(source))
            return;

        if (_clickCommand != null)
        {
            try
            {
                if (_clickCommand.CanExecute(_clickCommandParameter))
                {
                    _clickCommand.Execute(_clickCommandParameter);
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Notification", $"Click command failed: {ex.Message}", ex);
            }

            return;
        }

        if (_primaryAction != null)
        {
            try
            {
                _primaryAction.Callback();
            }
            catch (Exception ex)
            {
                Log.Error("Notification", $"Primary action failed: {ex.Message}", ex);
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        DescriptionText.MaxHeight = double.PositiveInfinity;
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
