using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Services.Shortcuts;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for WorkingChangesView.xaml
/// </summary>
public partial class WorkingChangesView : UserControl
{
    private IShortcutService? _shortcutService;
    // WPF fires Loaded / Unloaded multiple times across a UserControl's
    // lifetime (parent rehosting, virtualised lists, even theme reloads).
    // Without a guard we'd subscribe to GestureChanged twice and leave
    // one dangling on Unload. Tracked separately from _shortcutService
    // because the resolved service stays cached across cycles.
    private bool _subscribed;

    public WorkingChangesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Resolve the shortcut service lazily — the view may be
        // instantiated by XAML before App.Services is fully built.
        _shortcutService ??= Leaf.App.Services?.GetService<IShortcutService>();
        if (_shortcutService is null) return;

        ApplyShortcuts();
        if (!_subscribed)
        {
            _shortcutService.GestureChanged += OnShortcutGestureChanged;
            _subscribed = true;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_shortcutService is not null && _subscribed)
        {
            _shortcutService.GestureChanged -= OnShortcutGestureChanged;
            _subscribed = false;
        }
    }

    private void OnShortcutGestureChanged(object? sender, string? commandId)
    {
        // Either a specific id or null (ResetAll). Cheapest correct response
        // is to rebuild every binding we own here — only one for now.
        ApplyShortcuts();
    }

    /// <summary>
    /// Bind §5.15 Ctrl+T at the working-changes view scope so it doesn't
    /// steal the keystroke from text boxes in other parts of the app.
    /// The CommitInputControl exposes <c>OpenTemplatePickerCommand</c>
    /// which we reference here.
    /// </summary>
    private void ApplyShortcuts()
    {
        InputBindings.Clear();
        if (_shortcutService is null || CommitInput is null) return;

        var gesture = _shortcutService.GetGesture(ShortcutCommandId.Commit.OpenTemplatePicker);
        if (gesture is null) return; // user has unbound it
        InputBindings.Add(new KeyBinding(CommitInput.OpenTemplatePickerCommand, gesture));
    }
}
