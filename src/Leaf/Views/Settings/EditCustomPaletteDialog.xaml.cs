using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Views.Settings;

/// <summary>
/// Add/edit dialog for a custom <see cref="BranchColorPalette"/>. Supports
/// renaming, adding, removing, reordering, and editing individual colours
/// (each colour-edit reuses <see cref="Branch.BranchColorPickerDialog"/>
/// so the picker behaviour is shared with the right-click flow).
/// </summary>
public partial class EditCustomPaletteDialog : Window
{
    private readonly BranchColorPalette _source;
    private readonly ObservableCollection<ColorRow> _rows = [];

    /// <summary>
    /// The committed palette after the user clicks Save. Same id as the
    /// input <paramref name="source"/>; <see cref="BranchColorPalette.IsBuiltIn"/>
    /// always false (the registry rejects updates to built-ins).
    /// </summary>
    public BranchColorPalette UpdatedPalette { get; private set; }

    public EditCustomPaletteDialog(BranchColorPalette source)
    {
        InitializeComponent();
        _source = source ?? throw new ArgumentNullException(nameof(source));
        UpdatedPalette = source; // overwritten on Save

        NameTextBox.Text = source.DisplayName;
        ColorsList.ItemsSource = _rows;

        foreach (var color in source.ParsedColors())
            _rows.Add(new ColorRow { Color = color });
    }

    private void AddColor_Click(object sender, RoutedEventArgs e)
    {
        // Seed with the last colour in the list, or a sensible default if
        // we're starting empty.
        var seed = _rows.Count > 0 ? _rows[^1].Color : Color.FromRgb(0x3B, 0x82, 0xF6);
        var picker = new Branch.BranchColorPickerDialog("New colour", seed, _source, allowUseAuto: false) { Owner = this };
        if (picker.ShowDialog() != true) return;
        if (picker.Result == Branch.BranchColorPickerDialog.PickerResult.OverrideSet)
            _rows.Add(new ColorRow { Color = picker.SelectedColor });
        // ResetToAuto / Cancelled — no change to the palette being edited.
    }

    private void EditColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ColorRow row }) return;
        var picker = new Branch.BranchColorPickerDialog("Edit colour", row.Color, _source, allowUseAuto: false) { Owner = this };
        if (picker.ShowDialog() != true) return;
        if (picker.Result == Branch.BranchColorPickerDialog.PickerResult.OverrideSet)
        {
            // Replace the row in place so the ListBox re-binds — ColorRow's
            // Color is settable, and we re-fetch via index to swap the row
            // entry for change notification on bound items.
            var index = _rows.IndexOf(row);
            if (index >= 0) _rows[index] = new ColorRow { Color = picker.SelectedColor };
        }
    }

    private void RemoveColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ColorRow row }) return;
        if (_rows.Count <= 1)
        {
            // Per Engineering Software Policy: refuse to leave the palette
            // empty rather than silently accept it. Registry would reject
            // on save anyway; failing earlier is more discoverable.
            MessageBox.Show(this,
                "A palette must have at least one colour.",
                "Cannot remove",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        _rows.Remove(row);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ColorRow row }) return;
        var index = _rows.IndexOf(row);
        if (index > 0) _rows.Move(index, index - 1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ColorRow row }) return;
        var index = _rows.IndexOf(row);
        if (index >= 0 && index < _rows.Count - 1) _rows.Move(index, index + 1);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this,
                "Palette name cannot be empty.",
                "Save palette",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        if (_rows.Count == 0)
        {
            MessageBox.Show(this,
                "A palette must have at least one colour.",
                "Save palette",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var hexes = new List<string>(_rows.Count);
        foreach (var row in _rows)
            hexes.Add(BranchColorPalette.FormatColor(row.Color));

        UpdatedPalette = new BranchColorPalette
        {
            Id = _source.Id,
            DisplayName = name,
            Colors = hexes,
            IsBuiltIn = false,
        };

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private sealed class ColorRow
    {
        public Color Color { get; init; }
        public string Hex => BranchColorPalette.FormatColor(Color);
    }
}
