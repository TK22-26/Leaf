using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Leaf.Controls;

public partial class PillToggle : UserControl
{
    private TranslateTransform? _thumbTransform;
    private Border? _thumbBorder;
    private Grid? _rootGrid;
    private bool _awaitingLayout;
    private TextBlock? _leftLabel;
    private TextBlock? _rightLabel;

    public static readonly DependencyProperty LeftTextProperty =
        DependencyProperty.Register(
            nameof(LeftText),
            typeof(string),
            typeof(PillToggle),
            new PropertyMetadata("Left"));

    public static readonly DependencyProperty RightTextProperty =
        DependencyProperty.Register(
            nameof(RightText),
            typeof(string),
            typeof(PillToggle),
            new PropertyMetadata("Right"));

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(
            nameof(IsChecked),
            typeof(bool),
            typeof(PillToggle),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));

    public static readonly DependencyProperty LabelFontSizeProperty =
        DependencyProperty.Register(
            nameof(LabelFontSize),
            typeof(double),
            typeof(PillToggle),
            new PropertyMetadata(13.0));

    public PillToggle()
    {
        InitializeComponent();
        ToggleRoot.Loaded += (_, _) =>
        {
            CacheTemplateParts();
            UpdateVisualState(IsChecked, animate: false, "Loaded");
        };
        ToggleRoot.SizeChanged += (_, _) => UpdateVisualState(IsChecked, animate: false, "SizeChanged");
        ToggleRoot.Checked += (_, e) =>
        {
            UpdateVisualState(true, animate: true, "Checked");
            Checked?.Invoke(this, e);
        };
        ToggleRoot.Unchecked += (_, e) =>
        {
            UpdateVisualState(false, animate: true, "Unchecked");
            Unchecked?.Invoke(this, e);
        };
    }

    public string LeftText
    {
        get => (string)GetValue(LeftTextProperty);
        set => SetValue(LeftTextProperty, value);
    }

    public string RightText
    {
        get => (string)GetValue(RightTextProperty);
        set => SetValue(RightTextProperty, value);
    }

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public double LabelFontSize
    {
        get => (double)GetValue(LabelFontSizeProperty);
        set => SetValue(LabelFontSizeProperty, value);
    }

    public event RoutedEventHandler? Checked;
    public event RoutedEventHandler? Unchecked;

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PillToggle control)
        {
            control.UpdateVisualState(control.IsChecked, animate: true, "IsCheckedChanged");
        }
    }

    private void CacheTemplateParts()
    {
        ToggleRoot.ApplyTemplate();
        _rootGrid = ToggleRoot.Template.FindName("RootGrid", ToggleRoot) as Grid;
        if (ToggleRoot.Template.FindName("PillThumb", ToggleRoot) is Border thumb)
        {
            _thumbBorder = thumb;
            _thumbTransform = thumb.RenderTransform as TranslateTransform;
            if (_thumbTransform == null)
            {
                _thumbTransform = new TranslateTransform();
                thumb.RenderTransform = _thumbTransform;
            }
        }

        _leftLabel = ToggleRoot.Template.FindName("LeftLabel", ToggleRoot) as TextBlock;
        _rightLabel = ToggleRoot.Template.FindName("RightLabel", ToggleRoot) as TextBlock;
    }

    private void UpdateVisualState(bool isChecked, bool animate, string source)
    {
        if (_thumbTransform == null || _leftLabel == null || _rightLabel == null || _thumbBorder == null)
        {
            CacheTemplateParts();
        }

        if (_thumbTransform == null || _thumbBorder == null)
        {
            return;
        }

        // Keep the inner padding proportional so the thumb doesn't look overly inset at small sizes.
        double desiredPad = Math.Max(0, Math.Round(ToggleRoot.ActualHeight * 0.06));
        if (_rootGrid != null && !_rootGrid.Margin.Equals(new Thickness(desiredPad)))
        {
            _rootGrid.Margin = new Thickness(desiredPad);
            Dispatcher.BeginInvoke(() => UpdateVisualState(isChecked, animate: false, "PaddingAdjusted"), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        var selectedBrush = TryFindResource("TextFillColorInverseBrush") as SolidColorBrush;
        var deselectedBrush = TryFindResource("TextFillColorSecondaryBrush") as SolidColorBrush;

        if (_leftLabel != null && _rightLabel != null)
        {
            var leftTarget = isChecked ? deselectedBrush : selectedBrush;
            var rightTarget = isChecked ? selectedBrush : deselectedBrush;
            if (leftTarget != null)
            {
                ApplyGradientText(_leftLabel, leftTarget.Color, animate);
            }
            if (rightTarget != null)
            {
                ApplyGradientText(_rightLabel, rightTarget.Color, animate);
            }
        }

        double rootWidth = _rootGrid?.ActualWidth ?? 0;
        if (rootWidth > 0)
        {
            _thumbBorder.Width = rootWidth / 2;
        }
        double thumbWidth = _thumbBorder.ActualWidth;
        double target = isChecked ? Math.Max(0, rootWidth - thumbWidth) : 0;
        if (rootWidth <= 0 || thumbWidth <= 0)
        {
            if (!_awaitingLayout)
            {
                _awaitingLayout = true;
                EventHandler? handler = null;
                handler = (_, _) =>
                {
                    ToggleRoot.LayoutUpdated -= handler;
                    _awaitingLayout = false;
                    UpdateVisualState(isChecked, animate: false, "LayoutUpdated");
                };
                ToggleRoot.LayoutUpdated += handler;
            }
            return;
        }

        if (animate)
        {
            var animation = new DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromSeconds(0.24),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _thumbTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }
        else
        {
            _thumbTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _thumbTransform.X = target;
        }

    }

    private static void ApplyGradientText(TextBlock label, Color target, bool animate)
    {
        var currentBrush = label.Foreground as LinearGradientBrush;
        if (currentBrush == null)
        {
            currentBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(target, 0),
                    new GradientStop(target, 1)
                }
            };
            label.Foreground = currentBrush;
        }

        var start = currentBrush.GradientStops[0];
        var end = currentBrush.GradientStops[1];
        var startTarget = AdjustColor(target, 1.06);
        var endTarget = AdjustColor(target, 0.94);

        if (!animate)
        {
            start.Color = startTarget;
            end.Color = endTarget;
            return;
        }

        var duration = TimeSpan.FromSeconds(0.22);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var startAnim = new ColorAnimation(startTarget, duration) { EasingFunction = easing };
        var endAnim = new ColorAnimation(endTarget, duration) { EasingFunction = easing };
        start.BeginAnimation(GradientStop.ColorProperty, startAnim);
        end.BeginAnimation(GradientStop.ColorProperty, endAnim);
    }

    private static Color AdjustColor(Color color, double factor)
    {
        byte Scale(byte c)
        {
            var value = (int)Math.Round(c * factor);
            return (byte)Math.Clamp(value, 0, 255);
        }

        return Color.FromArgb(color.A, Scale(color.R), Scale(color.G), Scale(color.B));
    }
}
