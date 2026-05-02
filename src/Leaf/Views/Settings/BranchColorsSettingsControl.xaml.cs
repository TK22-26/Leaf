using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models;
using Leaf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings panel for §5.14 — palette selection plus custom palette
/// management. Implements <see cref="ISettingsSectionControl"/> so it
/// participates in the dialog's load/save cycle, but most of its work
/// goes through <see cref="IBranchColorPaletteRegistry"/> directly:
/// custom palette mutations are persisted immediately on commit so
/// they don't get lost if the user clicks Cancel on the parent dialog.
/// </summary>
public partial class BranchColorsSettingsControl : UserControl, ISettingsSectionControl
{
    private IBranchColorPaletteRegistry? _registry;
    private SettingsService? _settingsService;
    private AppSettings? _settings;

    private readonly ObservableCollection<BranchColorPalette> _allPalettes = [];
    private readonly ObservableCollection<CustomPaletteRow> _customRows = [];
    private bool _suppressSelectionEvent;

    public BranchColorsSettingsControl()
    {
        InitializeComponent();
        PaletteComboBox.ItemsSource = _allPalettes;
        CustomPalettesItems.ItemsSource = _customRows;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Resolve lazily — XAML may instantiate this before App.Services
        // is built (designer / test harness).
        if (_registry is null)
        {
            _registry = Leaf.App.Services?.GetService<IBranchColorPaletteRegistry>();
            if (_registry is null) return;
            _registry.PalettesChanged += OnRegistryPalettesChanged;
            Reload();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_registry is not null)
        {
            _registry.PalettesChanged -= OnRegistryPalettesChanged;
        }
    }

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        _settings = settings;
        _settingsService ??= Leaf.App.Services?.GetService<SettingsService>();
        Reload();
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        // The active palette id is written to the supplied settings instance
        // so the parent dialog persists it on Close. Custom palette CRUD has
        // already been written via the registry — those calls go through
        // SettingsService.SaveSettings directly so they survive a Cancel
        // on this dialog.
        if (_settings != null)
        {
            settings.DefaultBranchColorPaletteId = _settings.DefaultBranchColorPaletteId;
        }
    }

    private void Reload()
    {
        if (_registry is null) return;
        _settings ??= _settingsService?.LoadSettings();

        _allPalettes.Clear();
        foreach (var palette in _registry.GetAll())
            _allPalettes.Add(palette);

        // Drive the dropdown selection from the persisted id, falling back
        // to the registry default if the id is unknown / blank.
        var activeId = _settings?.DefaultBranchColorPaletteId;
        var active = _registry.GetById(activeId);
        _suppressSelectionEvent = true;
        try
        {
            PaletteComboBox.SelectedItem = active;
        }
        finally
        {
            _suppressSelectionEvent = false;
        }
        UpdatePreviewSwatches(active);

        _customRows.Clear();
        foreach (var palette in _allPalettes)
        {
            if (palette.IsBuiltIn) continue;
            _customRows.Add(BuildCustomRow(palette));
        }
        NoCustomPalettesText.Visibility = _customRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRegistryPalettesChanged(object? sender, EventArgs e)
    {
        // Re-bind on the UI thread — the registry can fire from any caller.
        Dispatcher.Invoke(Reload);
    }

    private void PaletteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvent || _settings is null) return;
        if (PaletteComboBox.SelectedItem is not BranchColorPalette palette) return;

        _settings.DefaultBranchColorPaletteId = palette.Id;
        UpdatePreviewSwatches(palette);

        // Persist the selection immediately as well so the GitGraphViewModel
        // re-paints right away — the Settings dialog Close button only saves
        // on confirm, but per-palette feedback is more useful when it's live.
        if (_settingsService != null)
        {
            // Persist the entire AppSettings (the parent dialog's Close also
            // calls SaveSettings; intermediate writes are still cheap).
            _settingsService.SaveSettings(_settings);
            // Tell every active GitGraphViewModel to re-pull its palette id.
            // The MainViewModel exposes a single instance through the app
            // service container; we resolve it here rather than threading
            // a reference through Settings construction.
            var mainVm = Leaf.App.Services?.GetService<Leaf.ViewModels.MainViewModel>();
            mainVm?.GitGraphViewModel?.RefreshBranchColorsFromSettings();
        }
    }

    private void UpdatePreviewSwatches(BranchColorPalette palette)
    {
        var rows = new List<SwatchRow>();
        foreach (var color in palette.ParsedColors())
        {
            rows.Add(new SwatchRow
            {
                Color = color,
                Hex = BranchColorPalette.FormatColor(color),
            });
        }
        ActiveSwatchPreview.ItemsSource = rows;
    }

    private void NewCustomFromActive_Click(object sender, RoutedEventArgs e)
    {
        if (_registry is null) return;
        var source = (PaletteComboBox.SelectedItem as BranchColorPalette) ?? _registry.Default;
        var clone = _registry.CloneBuiltInForEditing(source);
        if (EditCustomPalette(clone, isNew: true))
        {
            // After editing + saving, switch the active palette to the new
            // custom one — saves the user a second click in the dropdown.
            if (_settings != null)
            {
                _settings.DefaultBranchColorPaletteId = clone.Id;
                _settingsService?.SaveSettings(_settings);
            }
            Reload();
        }
    }

    private void EditCustom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not CustomPaletteRow row || _registry is null) return;
        var existing = _registry.GetById(row.Id);
        if (existing.IsBuiltIn) return; // shouldn't happen — built-ins don't appear here
        EditCustomPalette(existing, isNew: false);
    }

    private void DeleteCustom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not CustomPaletteRow row || _registry is null) return;
        var owner = Window.GetWindow(this);
        var confirm = MessageBox.Show(
            owner,
            $"Delete the custom palette '{row.DisplayName}'?\n\n" +
            "If this palette is currently active, the default palette will be used instead.",
            "Delete custom palette",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _registry.DeleteCustom(row.Id);
        // If we just deleted the active palette, fall back to the default
        // both in our local cache and in the persisted settings.
        if (_settings != null
            && string.Equals(_settings.DefaultBranchColorPaletteId, row.Id, StringComparison.OrdinalIgnoreCase))
        {
            _settings.DefaultBranchColorPaletteId = _registry.Default.Id;
            _settingsService?.SaveSettings(_settings);
        }
    }

    /// <summary>
    /// Open the custom-palette editor. Returns true when the user committed
    /// changes (registry has already been updated). The caller decides
    /// whether to switch the active palette afterwards.
    /// </summary>
    private bool EditCustomPalette(BranchColorPalette palette, bool isNew)
    {
        if (_registry is null) return false;

        var dialog = new EditCustomPaletteDialog(palette)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true) return false;

        try
        {
            _registry.AddOrUpdateCustom(dialog.UpdatedPalette);
            return true;
        }
        catch (ArgumentException ex)
        {
            // Engineering Software Policy: surface the failure visibly
            // rather than silently keeping invalid input in the registry.
            MessageBox.Show(Window.GetWindow(this),
                ex.Message,
                isNew ? "Could not create palette" : "Could not save palette",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static CustomPaletteRow BuildCustomRow(BranchColorPalette palette)
    {
        var swatches = new List<SwatchRow>();
        foreach (var color in palette.ParsedColors())
            swatches.Add(new SwatchRow { Color = color, Hex = BranchColorPalette.FormatColor(color) });
        return new CustomPaletteRow
        {
            Id = palette.Id,
            DisplayName = palette.DisplayName,
            Swatches = swatches,
        };
    }

    private sealed class SwatchRow
    {
        public Color Color { get; init; }
        public string Hex { get; init; } = string.Empty;
    }

    private sealed class CustomPaletteRow
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public IList<SwatchRow> Swatches { get; init; } = [];
    }
}
