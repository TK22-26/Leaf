using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Leaf.Services;

namespace Leaf.Controls;

public partial class NotificationHost : UserControl
{
    private const int MaxVisible = 3;
    private static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(10);

    private INotificationService? _notificationService;
    private readonly Dictionary<NotificationCard, DispatcherTimer> _timers = new();

    public NotificationHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public INotificationService? NotificationService
    {
        get => _notificationService;
        set
        {
            if (_notificationService != null)
                _notificationService.NotificationRequested -= OnNotificationRequested;

            _notificationService = value;

            if (_notificationService != null && IsLoaded)
                _notificationService.NotificationRequested += OnNotificationRequested;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_notificationService != null)
            _notificationService.NotificationRequested += OnNotificationRequested;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_notificationService != null)
            _notificationService.NotificationRequested -= OnNotificationRequested;

        // Stop all timers and clear cards
        foreach (var timer in _timers.Values)
            timer.Stop();
        _timers.Clear();
        CardStack.Children.Clear();
    }

    private void OnNotificationRequested(NotificationMessage message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnNotificationRequested(message));
            return;
        }

        // Evict oldest if at max capacity (no animation)
        while (CardStack.Children.Count >= MaxVisible)
        {
            if (CardStack.Children[0] is NotificationCard oldest)
                RemoveCard(oldest, animate: false);
        }

        var card = new NotificationCard();
        card.SetContent(message.Title, message.Description, message.Type);
        card.CloseRequested += (_, _) => DismissCard(card);
        card.Margin = new Thickness(0, 0, 0, 8);

        CardStack.Children.Add(card);
        AnimateEntrance(card);
    }

    private void AnimateEntrance(NotificationCard card)
    {
        var translateTransform = card.RenderTransform as TranslateTransform
            ?? new TranslateTransform(-400, 0);
        card.RenderTransform = translateTransform;

        var slideIn = new DoubleAnimation
        {
            From = -400,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(150)
        };

        slideIn.Completed += (_, _) => StartAutoDismissTimer(card);

        translateTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        card.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void StartAutoDismissTimer(NotificationCard card)
    {
        var timer = new DispatcherTimer { Interval = AutoDismissDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DismissCard(card);
        };
        _timers[card] = timer;
        timer.Start();
    }

    private void DismissCard(NotificationCard card)
    {
        if (!CardStack.Children.Contains(card))
            return;

        // Stop timer if running
        if (_timers.TryGetValue(card, out var timer))
        {
            timer.Stop();
            _timers.Remove(card);
        }

        AnimateDismiss(card);
    }

    private void AnimateDismiss(NotificationCard card)
    {
        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) =>
        {
            CardStack.Children.Remove(card);
        };

        card.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void RemoveCard(NotificationCard card, bool animate)
    {
        if (_timers.TryGetValue(card, out var timer))
        {
            timer.Stop();
            _timers.Remove(card);
        }

        if (animate)
            AnimateDismiss(card);
        else
            CardStack.Children.Remove(card);
    }
}
