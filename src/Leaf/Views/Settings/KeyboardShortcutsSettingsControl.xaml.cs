using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Leaf.Services.Shortcuts;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings panel that lets the user view and rebind every shortcut
/// registered with <see cref="IShortcutService"/>. The panel reads the
/// service directly (live data) — there's no separate Apply step. A
/// rebind takes effect the moment the user presses Save.
/// </summary>
public partial class KeyboardShortcutsSettingsControl : UserControl
{
    private readonly ObservableCollection<ShortcutRowViewModel> _rows = [];
    private readonly ICollectionView _view;
    private IShortcutService? _service;

    public KeyboardShortcutsSettingsControl()
    {
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutRowViewModel.Category)));
        _view.Filter = FilterPredicate;
        ShortcutsList.ItemsSource = _view;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Resolve lazily — the control may be instantiated by the XAML
        // parser before App.Services is built.
        if (_service is null)
        {
            _service = Leaf.App.Services?.GetService<IShortcutService>();
            if (_service is null) return;
            Reload();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Cancel any in-flight edit so a future re-show starts clean.
        foreach (var row in _rows) row.CancelEdit();
    }

    private void Reload()
    {
        if (_service is null) return;
        _rows.Clear();
        foreach (var def in _service.Definitions)
        {
            _rows.Add(new ShortcutRowViewModel(def, _service));
        }
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not ShortcutRowViewModel row) return false;
        var query = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return true;
        return row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void EditOrSave_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutRowViewModel row }) return;
        if (row.IsEditing) row.CommitEdit();
        else row.BeginEdit();
        FocusCaptureFor(row);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShortcutRowViewModel row }) return;
        row.ResetToDefault();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _service?.ResetAll();
        Reload();
    }

    private void Capture_KeyDown(object sender, KeyEventArgs e)
    {
        // Standard rebind UX: Esc cancels, Enter saves.
        if (sender is not TextBlock { Tag: ShortcutRowViewModel row }) return;
        if (e.Key == Key.Escape) { row.CancelEdit(); e.Handled = true; }
        else if (e.Key == Key.Enter) { row.CommitEdit(); e.Handled = true; }
    }

    private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBlock { Tag: ShortcutRowViewModel row }) return;
        // Esc / Enter are the control verbs; everything else is part of
        // the gesture being captured.
        if (e.Key is Key.Escape or Key.Enter) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Modifier-only presses (Ctrl/Shift/Alt by themselves) don't
        // form a valid gesture and KeyGesture would throw on them.
        // Wait for the user to press a real key while holding the
        // modifier; the placeholder "Press a key combination..." stays
        // up until then.
        if (IsModifierKey(key)) return;

        row.SetCapture(key, Keyboard.Modifiers);
        e.Handled = true;
    }

    private static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
          or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    private void FocusCaptureFor(ShortcutRowViewModel row)
    {
        // The capture TextBlock is inside the per-row template; find it
        // via the visual tree on the next render pass and focus it so
        // the user can immediately type their new gesture.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var container = ShortcutsList.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement;
            var textBlock = container is null ? null : FindCaptureTextBlock(container);
            textBlock?.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static TextBlock? FindCaptureTextBlock(DependencyObject root)
    {
        // The capture TextBlock has Focusable=True and a Tag pointing at
        // the row VM; the display TextBlock doesn't. Scan visually.
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb && tb.Focusable && tb.Tag is ShortcutRowViewModel) return tb;
            var hit = FindCaptureTextBlock(child);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>Per-row VM. Owns the in-flight capture state plus the displayed values.</summary>
    public partial class ShortcutRowViewModel : ObservableObject
    {
        private readonly ShortcutDefinition _definition;
        private readonly IShortcutService _service;
        private static readonly KeyGestureConverter GestureConverter = new();

        public ShortcutRowViewModel(ShortcutDefinition definition, IShortcutService service)
        {
            _definition = definition;
            _service = service;
        }

        public string Id => _definition.Id;
        public string Label => _definition.Label;
        public string Category => _definition.Category;

        public string GestureDisplay
        {
            get
            {
                var gesture = _service.GetGesture(_definition.Id);
                if (gesture is null) return "(unbound)";
                return GestureConverter.ConvertToInvariantString(gesture) ?? "(invalid)";
            }
        }

        /// <summary>Foreground brush — tertiary text when unbound (less prominent), primary otherwise. <see cref="FrameworkElement.TryFindResource(object)"/> rather than <c>FindResource</c> so a missing theme key doesn't crash the Settings dialog.</summary>
        public Brush GestureForeground
        {
            get
            {
                var key = _service.GetGesture(_definition.Id) is null
                    ? "TextFillColorTertiaryBrush"
                    : "TextFillColorPrimaryBrush";
                return Application.Current.TryFindResource(key) as Brush
                    ?? SystemColors.ControlTextBrush;
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CaptureDisplay))]
        private bool _isEditing;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CaptureDisplay))]
        [NotifyPropertyChangedFor(nameof(HasConflict))]
        [NotifyPropertyChangedFor(nameof(ConflictMessage))]
        private KeyGesture? _captured;

        public string CaptureDisplay
        {
            get
            {
                if (!IsEditing) return string.Empty;
                if (Captured is null) return "Press a key combination…";
                return GestureConverter.ConvertToInvariantString(Captured) ?? "(invalid)";
            }
        }

        public bool HasConflict => ConflictMessage.Length > 0;

        public string ConflictMessage
        {
            get
            {
                if (Captured is null) return string.Empty;
                var conflict = _service.FindConflict(Captured, _definition.Scope);
                if (conflict is null || conflict == _definition.Id) return string.Empty;
                // The service auto-unbinds the conflicting row when the
                // user saves -- this message tells them what's about to
                // happen so the change isn't a surprise.
                return $"Already used by '{conflict}'. Saving will unbind it.";
            }
        }

        public void BeginEdit()
        {
            Captured = null;
            IsEditing = true;
        }

        public void SetCapture(Key key, ModifierKeys modifiers)
        {
            try
            {
                Captured = new KeyGesture(key, modifiers);
            }
            catch (NotSupportedException)
            {
                // KeyGesture throws when the combination isn't valid as
                // a shortcut (e.g. plain alphanumerics with no modifier).
                // Show that to the user via an empty Captured + the
                // "Press a key combination" placeholder.
                Captured = null;
            }
        }

        public void CommitEdit()
        {
            if (!IsEditing) return;
            // Empty capture = user opened edit but didn't press anything
            // — treat as cancel rather than unbind. ResetToDefault is
            // the explicit way to clear, and the dedicated Reset button
            // already exposes it.
            if (Captured is not null)
            {
                _service.SetGesture(_definition.Id, Captured);
                OnPropertyChanged(nameof(GestureDisplay));
                OnPropertyChanged(nameof(GestureForeground));
            }
            IsEditing = false;
            Captured = null;
        }

        public void CancelEdit()
        {
            IsEditing = false;
            Captured = null;
        }

        public void ResetToDefault()
        {
            _service.SetGesture(_definition.Id, _definition.DefaultGesture);
            IsEditing = false;
            Captured = null;
            OnPropertyChanged(nameof(GestureDisplay));
            OnPropertyChanged(nameof(GestureForeground));
        }
    }
}
