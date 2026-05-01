using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Leaf.Controls;

/// <summary>
/// A button with an attached dropdown popup. Click opens the dropdown;
/// click again closes it. The popup animates in via the standard WPF
/// Slide animation, the chevron flips state with the popup, and any
/// button click inside the popup auto-closes it (so consumers can
/// build menu-style dropdowns by stacking <see cref="System.Windows.Controls.Button"/>
/// elements without wiring per-item dismiss handlers).
/// </summary>
/// <remarks>
/// Why this exists: the ad-hoc <c>Button</c>+<c>Popup</c> we had in
/// the bisect view shipped without an animation, no chevron rotation,
/// and re-opened immediately when the user clicked the trigger to
/// dismiss it (no debounce). Centralising the pattern here means
/// every dropdown in the app picks up the same affordances and we
/// fix bugs in one place.
/// </remarks>
public class DropdownButton : ContentControl
{
    private Popup? _popup;
    private ToggleButton? _toggleButton;

    /// <summary>
    /// Timestamp of the last popup-close event. Used to debounce
    /// re-open: if the user clicks the trigger button to close the
    /// popup, WPF first raises the StaysOpen=false dismiss
    /// (popup.Closed → toggle.IsChecked=false) AND THEN the click
    /// re-fires the toggle's Checked. Without a debounce window,
    /// the popup re-opens immediately. 200ms is plenty.
    /// </summary>
    private DateTime _popupClosedTime = DateTime.MinValue;

    // The implicit Style for DropdownButton lives in App.xaml's merged
    // resources (Controls/DropdownButton.xaml). It's parsed in the
    // App resource scope where the Fluent theme's implicit ToggleButton
    // style is reachable via BasedOn={StaticResource {x:Type
    // ToggleButton}}, so our inner ToggleButton inherits the proper
    // button chrome and we only override the IsChecked-state visuals.
    // The previous incarnation loaded the dictionary standalone in
    // the constructor — that parsed in an isolated scope, BasedOn
    // resolved to nothing, and the ToggleButton had no template (it
    // rendered as just text + chevron, no button background).

    /// <summary>
    /// Content rendered inside the dropdown popup. Typically a
    /// <see cref="StackPanel"/> of buttons (one per menu item) but
    /// any FrameworkElement works.
    /// </summary>
    public static readonly DependencyProperty DropdownContentProperty =
        DependencyProperty.Register(
            nameof(DropdownContent),
            typeof(object),
            typeof(DropdownButton),
            new PropertyMetadata(null));

    [Description("Content rendered inside the dropdown popup.")]
    public object? DropdownContent
    {
        get => GetValue(DropdownContentProperty);
        set => SetValue(DropdownContentProperty, value);
    }

    /// <summary>
    /// True when the popup is currently visible. Read-mostly; setting
    /// to true opens the popup, false closes it. Useful for VMs that
    /// want to programmatically dismiss the dropdown after running an
    /// action (most consumers don't need this — inner-button clicks
    /// close automatically).
    /// </summary>
    public static readonly DependencyProperty IsDropdownOpenProperty =
        DependencyProperty.Register(
            nameof(IsDropdownOpen),
            typeof(bool),
            typeof(DropdownButton),
            new PropertyMetadata(false));

    public bool IsDropdownOpen
    {
        get => (bool)GetValue(IsDropdownOpenProperty);
        set => SetValue(IsDropdownOpenProperty, value);
    }

    /// <summary>
    /// Whether to render the trailing chevron glyph. Off for buttons
    /// that already carry their own affordance (e.g. an icon-only
    /// kebab where adding a chevron would be redundant).
    /// </summary>
    public static readonly DependencyProperty ShowChevronProperty =
        DependencyProperty.Register(
            nameof(ShowChevron),
            typeof(bool),
            typeof(DropdownButton),
            new PropertyMetadata(true));

    [Description("Whether the trailing chevron glyph is rendered.")]
    public bool ShowChevron
    {
        get => (bool)GetValue(ShowChevronProperty);
        set => SetValue(ShowChevronProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_toggleButton != null)
            _toggleButton.Checked -= OnToggleChecked;
        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnPopupChildClick));
        }

        _toggleButton = GetTemplateChild("PART_Button") as ToggleButton;
        _popup = GetTemplateChild("PART_Popup") as Popup;

        if (_toggleButton != null)
            _toggleButton.Checked += OnToggleChecked;
        if (_popup != null)
        {
            _popup.Closed += OnPopupClosed;
            // Auto-dismiss when any button inside the popup is clicked
            // — saves consumers from wiring per-item Click handlers
            // just to close the menu after a selection.
            _popup.AddHandler(
                ButtonBase.ClickEvent,
                new RoutedEventHandler(OnPopupChildClick));
        }
    }

    private void OnToggleChecked(object sender, RoutedEventArgs e)
    {
        // Debounce: if popup just closed (within 200ms), the user is
        // clicking the trigger to dismiss. Don't reopen immediately.
        if ((DateTime.Now - _popupClosedTime).TotalMilliseconds < 200)
        {
            if (_toggleButton != null)
                _toggleButton.IsChecked = false;
            return;
        }

        if (_popup != null)
            _popup.IsOpen = true;
        IsDropdownOpen = true;
    }

    private void OnPopupChildClick(object sender, RoutedEventArgs e)
    {
        if (_popup != null)
            _popup.IsOpen = false;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _popupClosedTime = DateTime.Now;
        if (_toggleButton != null)
            _toggleButton.IsChecked = false;
        IsDropdownOpen = false;
    }
}
