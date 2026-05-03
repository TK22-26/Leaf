using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Leaf.Models;
using Leaf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings panel for §5.15 — list / edit / delete commit templates and
/// their per-template ticket regex. Two-column layout: list on the left,
/// editor on the right. Built-in presets stay in the list but with a
/// "built-in" badge; their bodies are editable but the row can't be
/// deleted (Delete becomes "reset to shipped default" when a built-in
/// is selected).
/// </summary>
public partial class CommitTemplatesSettingsControl : UserControl, ISettingsSectionControl
{
    private ICommitTemplateService? _service;
    private readonly ObservableCollection<TemplateRow> _rows = [];
    private TemplateRow? _editing;
    private bool _suppressDirty;
    private bool _subscribed;

    private SettingsService? _settingsService;
    private bool _suppressEnabledClick;

    // The parent SettingsDialog hands us its in-memory AppSettings via
    // LoadSettings on construction and saves THAT same instance on
    // Close. If we write to a fresh LoadSettings() + SaveSettings() pair
    // here, the parent's Close path overwrites our value with its
    // pre-toggle copy. Cache the parent's instance so toggle clicks
    // mutate the same object the dialog will persist.
    private AppSettings? _parentSettings;

    public CommitTemplatesSettingsControl()
    {
        InitializeComponent();
        TemplatesList.ItemsSource = _rows;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _service ??= Leaf.App.Services?.GetService<ICommitTemplateService>();
        _settingsService ??= Leaf.App.Services?.GetService<SettingsService>();
        if (_service is null) return;
        if (!_subscribed)
        {
            _service.TemplatesChanged += OnTemplatesChanged;
            _subscribed = true;
        }
        // Hydration of the master toggle is the parent dialog's job via
        // LoadSettings(AppSettings, ...). When this control's Loaded
        // fires before the parent gets a chance to call LoadSettings
        // (uncommon but possible during XAML init order), fall back to
        // a fresh disk read so the checkbox doesn't render stuck on
        // its default-true state.
        if (_parentSettings is null && _settingsService is not null)
        {
            _suppressEnabledClick = true;
            try { EnabledCheckBox.IsChecked = _settingsService.LoadSettings().CommitTemplatesEnabled; }
            finally { _suppressEnabledClick = false; }
        }
        Reload();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Commit any in-flight edit before the panel closes — without this
        // the user's last keystroke would silently drop on the floor when
        // they hit Save in the parent dialog.
        CommitEditorChanges();
        if (_service is not null && _subscribed)
        {
            _service.TemplatesChanged -= OnTemplatesChanged;
            _subscribed = false;
        }
    }

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        // Cache the parent's instance so EnabledCheckBox_Click mutates
        // the object the SettingsDialog will save on Close. The toggle
        // ALSO immediately writes to disk so a Cancel-style close path
        // doesn't lose the change — but since parent always saves its
        // _settings on Close, we have to mutate that one too.
        _parentSettings = settings;

        _suppressEnabledClick = true;
        try { EnabledCheckBox.IsChecked = settings.CommitTemplatesEnabled; }
        finally { _suppressEnabledClick = false; }
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        // Last-chance flush of pending edits — see OnUnloaded above.
        CommitEditorChanges();
        // Mirror the toggle into the dialog's settings copy. The parent
        // dialog calls _settingsService.SaveSettings(_settings) right
        // after this, so a final write here guarantees the toggle is
        // persisted even if a future change to the click handler stops
        // doing its own immediate save.
        settings.CommitTemplatesEnabled = EnabledCheckBox.IsChecked == true;
    }

    private void EnabledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEnabledClick) return;
        // Mutate the dialog's in-memory AppSettings — that's the
        // instance Close_Click serialises, and SaveSettings on this
        // panel mirrors the toggle back into it as a final flush.
        // No eager disk write: it would race other panels' unflushed
        // edits and is redundant with the dialog's Close-time save.
        if (_parentSettings is not null)
            _parentSettings.CommitTemplatesEnabled = EnabledCheckBox.IsChecked == true;
    }

    private void OnTemplatesChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(Reload);
    }

    private void Reload()
    {
        if (_service is null) return;

        var selectedId = (TemplatesList.SelectedItem as TemplateRow)?.Template.Id;
        _rows.Clear();
        foreach (var template in _service.GetAll())
        {
            _rows.Add(BuildRow(template));
        }

        // Restore selection if possible — keeps the editor on the row the
        // user was working with through a TemplatesChanged event.
        if (!string.IsNullOrEmpty(selectedId))
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(_rows[i].Template.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    TemplatesList.SelectedIndex = i;
                    return;
                }
            }
        }

        if (_rows.Count > 0) TemplatesList.SelectedIndex = 0;
    }

    private static TemplateRow BuildRow(CommitTemplate template) => new()
    {
        Template = template,
        Name = template.Name,
        Badge = template.IsBuiltIn
            ? "built-in"
            : (template.Scope == CommitTemplateScope.Repository ? "this repo" : "custom"),
    };

    private void TemplatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Persist whatever's in the editor for the previously-selected row
        // before swapping. CommitEditorChanges no-ops when nothing changed.
        CommitEditorChanges();

        _editing = TemplatesList.SelectedItem as TemplateRow;
        if (_editing is null)
        {
            EditorPanel.IsEnabled = false;
            DeleteButton.IsEnabled = false;
            return;
        }

        _suppressDirty = true;
        try
        {
            NameTextBox.Text = _editing.Template.Name;
            NameTextBox.IsEnabled = !_editing.Template.IsBuiltIn;
            BodyTextBox.Text = _editing.Template.Body;
            TicketRegexTextBox.Text = _editing.Template.TicketRegex;
            ScopeCombo.SelectedIndex = _editing.Template.Scope == CommitTemplateScope.Repository ? 1 : 0;
            ScopeCombo.IsEnabled = !_editing.Template.IsBuiltIn;
        }
        finally
        {
            _suppressDirty = false;
        }

        EditorPanel.IsEnabled = true;
        DeleteButton.IsEnabled = true;
        DeleteButton.Content = _editing.Template.IsBuiltIn ? "Reset" : "Delete";
        DeleteButton.ToolTip = _editing.Template.IsBuiltIn
            ? "Drop your edits and revert to the shipped values for this preset."
            : "Permanently delete this template.";
    }

    private void EditorField_Changed(object sender, TextChangedEventArgs e)
    {
        // Suppress writes during selection-driven loads; the live-write
        // model means every keystroke would otherwise call AddOrUpdate
        // and roundtrip through settings — which works, but is noisy and
        // creates an edit-loop hazard. Defer to LostFocus / next selection
        // change instead.
        if (_suppressDirty || _editing is null) return;
        _editing.IsDirty = true;
    }

    private void ScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDirty || _editing is null) return;
        _editing.IsDirty = true;
    }

    /// <summary>
    /// Persist whatever's in the editor onto the currently-selected
    /// template. No-op when nothing has changed since the last load
    /// or commit. Surfaces validation errors via a MessageBox.
    /// </summary>
    private void CommitEditorChanges()
    {
        if (_service is null || _editing is null || !_editing.IsDirty) return;

        var name = NameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) && !_editing.Template.IsBuiltIn)
        {
            MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                "Template name cannot be empty.",
                "Invalid template",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            // Revert visual state to last-saved name so the user isn't
            // stuck in a broken edit.
            _suppressDirty = true;
            try { NameTextBox.Text = _editing.Template.Name; }
            finally { _suppressDirty = false; }
            return;
        }

        var scope = (ScopeCombo.SelectedIndex == 1)
            ? CommitTemplateScope.Repository
            : CommitTemplateScope.Global;

        var updated = new CommitTemplate
        {
            Id = _editing.Template.Id,
            Name = _editing.Template.IsBuiltIn ? _editing.Template.Name : name,
            Body = BodyTextBox.Text ?? string.Empty,
            TicketRegex = TicketRegexTextBox.Text ?? string.Empty,
            Scope = _editing.Template.IsBuiltIn ? CommitTemplateScope.Global : scope,
            IsBuiltIn = _editing.Template.IsBuiltIn,
        };

        try
        {
            _service.AddOrUpdate(updated);
            _editing.IsDirty = false;
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                ex.Message, "Cannot save template",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                ex.Message, "Cannot save template",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;

        // Commit any pending edit on the currently-selected row before
        // creating the new one — otherwise the user's keystrokes would
        // get discarded by the selection swap.
        CommitEditorChanges();

        var template = new CommitTemplate
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New template",
            Body = string.Empty,
            TicketRegex = string.Empty,
            Scope = CommitTemplateScope.Global,
            IsBuiltIn = false,
        };
        try
        {
            _service.AddOrUpdate(template);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(Window.GetWindow(this) ?? Application.Current.MainWindow,
                ex.Message, "Cannot create template",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Reload populates _rows with the new entry; select + focus the
        // name box so the user is ready to type.
        Reload();
        for (var i = 0; i < _rows.Count; i++)
        {
            if (string.Equals(_rows[i].Template.Id, template.Id, StringComparison.OrdinalIgnoreCase))
            {
                TemplatesList.SelectedIndex = i;
                NameTextBox.Focus();
                NameTextBox.SelectAll();
                return;
            }
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || _editing is null) return;

        if (_editing.Template.IsBuiltIn)
        {
            // Reset path — drop overrides for the preset.
            _service.Delete(_editing.Template.Id);
            return;
        }

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        var confirm = MessageBox.Show(owner,
            $"Delete template '{_editing.Template.Name}'?",
            "Delete template",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        _service.Delete(_editing.Template.Id);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        var confirm = MessageBox.Show(owner,
            "Reset every template back to the shipped defaults?\n\nYour custom global templates will be deleted, and any tweaks to built-in presets will be reverted.",
            "Reset all templates",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        _service.ResetToDefaults();
    }

    private void InsertPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        // Build an inline ContextMenu rather than a separate XAML resource —
        // the menu is one-shot and the placeholder list is short enough
        // that inlining keeps the wiring obvious.
        var menu = new ContextMenu();
        foreach (var token in new[] { "{branch}", "{date}", "{datetime}", "{user.name}", "{user.email}", "{ticket}", "{cursor}" })
        {
            var item = new MenuItem { Header = token };
            item.Click += (_, _) => InsertAtCaret(token);
            menu.Items.Add(item);
        }
        if (sender is FrameworkElement source)
        {
            menu.PlacementTarget = source;
            menu.IsOpen = true;
        }
    }

    private void InsertAtCaret(string token)
    {
        var caret = BodyTextBox.SelectionStart;
        var text = BodyTextBox.Text ?? string.Empty;
        BodyTextBox.Text = text.Insert(caret, token);
        BodyTextBox.SelectionStart = caret + token.Length;
        BodyTextBox.Focus();
        if (_editing is not null) _editing.IsDirty = true;
    }

    private sealed class TemplateRow
    {
        public CommitTemplate Template { get; init; } = null!;
        public string Name { get; init; } = string.Empty;
        public string Badge { get; init; } = string.Empty;
        public bool IsDirty { get; set; }
    }
}
