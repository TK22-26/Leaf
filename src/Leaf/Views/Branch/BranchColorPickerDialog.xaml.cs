using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Views.Branch;

/// <summary>
/// Dialog for §5.14 right-click "Change colour…" on a branch label. Returns
/// one of three outcomes via <see cref="Result"/>: a chosen <see cref="Color"/>,
/// a request to clear the override (Use Auto), or cancel.
///
/// <para>The picker keeps three views in sync — HSV sliders, hex input,
/// preview swatch — without any external colour-picker component. Palette
/// swatches show the active palette so a one-click pick from the curated
/// set is always visible alongside free-form HSV / hex input.</para>
/// </summary>
public partial class BranchColorPickerDialog : Window
{
    /// <summary>Outcome of the dialog.</summary>
    public enum PickerResult
    {
        /// <summary>User cancelled — caller should leave existing state alone.</summary>
        Cancelled,
        /// <summary>User chose a colour — read <see cref="SelectedColor"/>.</summary>
        OverrideSet,
        /// <summary>User clicked Use Auto — caller should clear the override.</summary>
        ResetToAuto,
    }

    /// <summary>Outcome flag set on close. Defaults to <see cref="PickerResult.Cancelled"/>.</summary>
    public PickerResult Result { get; private set; } = PickerResult.Cancelled;

    /// <summary>The chosen colour when <see cref="Result"/> is <see cref="PickerResult.OverrideSet"/>.</summary>
    public Color SelectedColor { get; private set; }

    /// <summary>
    /// Suppress recursive ValueChanged / TextChanged handlers when one of
    /// the synced views (slider, text box) is being driven from another.
    /// </summary>
    private bool _suppressSync;

    /// <param name="branchName">Display name shown in the header + preview pill.</param>
    /// <param name="initialColor">Pre-fill the picker with this colour — typically the resolved current colour for the branch.</param>
    /// <param name="palette">Active palette colours rendered as one-click swatches under the sliders.</param>
    /// <param name="allowUseAuto">
    /// Show the "Use Auto" button. Right-click flow on a branch label sets
    /// this true so the user can clear an explicit override; the palette
    /// editor sets it false because "auto" has no meaning when picking
    /// a palette slot colour.
    /// </param>
    public BranchColorPickerDialog(string branchName, Color initialColor, BranchColorPalette palette, bool allowUseAuto = true)
    {
        InitializeComponent();

        HeaderTextBlock.Text = $"Choose colour for '{branchName}'";
        PreviewLabel.Text = branchName;
        if (!allowUseAuto)
            UseAutoButton.Visibility = Visibility.Collapsed;

        // Bind palette swatches. Build a minimal POCO list rather than
        // exposing the full BranchColorPalette so the data template stays
        // tied to the picker's needs (Color + tooltip hex).
        var swatchItems = new List<PaletteSwatch>();
        foreach (var color in palette.ParsedColors())
        {
            swatchItems.Add(new PaletteSwatch
            {
                Color = color,
                Hex = BranchColorPalette.FormatColor(color),
            });
        }
        PaletteItems.ItemsSource = swatchItems;

        SetColor(initialColor);

        // Hex textbox loses focus when buttons get clicked — Enter should
        // commit the typed hex first, so the user doesn't have to tab out.
        HexTextBox.Focus();
        HexTextBox.SelectAll();
    }

    private void HsvSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSync) return;

        var color = HsvToRgb(HueSlider.Value, SaturationSlider.Value / 100.0, ValueSlider.Value / 100.0);
        SyncFromColor(color, syncSliders: false, syncHex: true);
    }

    private void HexTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        TryCommitHex();
    }

    private void HexTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Commit hex on Enter, then default OK button still fires its
            // click separately because IsDefault=true raises Click on Enter
            // only when no other handler marked it handled. We let it
            // proceed: TryCommitHex returns silently on a bad value, the
            // user sees the rejected text revert to the last valid hex.
            TryCommitHex();
        }
    }

    private void TryCommitHex()
    {
        if (BranchColorPalette.TryParseColor(HexTextBox.Text, out var color))
        {
            SyncFromColor(color, syncSliders: true, syncHex: true);
        }
        else
        {
            // Reject silently by reverting to current preview's hex.
            HexTextBox.Text = BranchColorPalette.FormatColor(PreviewBrush.Color);
        }
    }

    private void PaletteSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is Color color)
        {
            SetColor(color);
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        // Commit any pending hex edit first — otherwise OK after typing
        // a hex without leaving the textbox would discard the typed value.
        TryCommitHex();
        SelectedColor = PreviewBrush.Color;
        Result = PickerResult.OverrideSet;
        DialogResult = true;
        Close();
    }

    private void UseAutoButton_Click(object sender, RoutedEventArgs e)
    {
        Result = PickerResult.ResetToAuto;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = PickerResult.Cancelled;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Set every view (sliders, hex, preview) from a single source colour.
    /// </summary>
    private void SetColor(Color color)
    {
        SyncFromColor(color, syncSliders: true, syncHex: true);
    }

    /// <summary>
    /// Drive the dialog's three synced views from a colour. The booleans
    /// let an originating view skip its own update — e.g. the slider
    /// handler doesn't need to overwrite the slider it just changed.
    /// </summary>
    private void SyncFromColor(Color color, bool syncSliders, bool syncHex)
    {
        _suppressSync = true;
        try
        {
            PreviewBrush.Color = color;

            if (syncSliders)
            {
                var (h, s, v) = RgbToHsv(color);
                HueSlider.Value = h;
                SaturationSlider.Value = s * 100;
                ValueSlider.Value = v * 100;
            }
            HueValueLabel.Text = ((int)Math.Round(HueSlider.Value)).ToString(CultureInfo.InvariantCulture);
            SaturationValueLabel.Text = ((int)Math.Round(SaturationSlider.Value)).ToString(CultureInfo.InvariantCulture);
            ValueValueLabel.Text = ((int)Math.Round(ValueSlider.Value)).ToString(CultureInfo.InvariantCulture);

            if (syncHex)
                HexTextBox.Text = BranchColorPalette.FormatColor(color);
        }
        finally
        {
            _suppressSync = false;
        }
    }

    /// <summary>
    /// HSV → RGB. <paramref name="h"/> in [0,360], <paramref name="s"/> and
    /// <paramref name="v"/> in [0,1]. Standard formula; alpha is forced
    /// opaque since branch colours are always solid.
    /// </summary>
    private static Color HsvToRgb(double h, double s, double v)
    {
        if (s <= 0)
        {
            byte g = (byte)Math.Round(v * 255);
            return Color.FromRgb(g, g, g);
        }

        h = ((h % 360) + 360) % 360;
        double sector = h / 60.0;
        int sectorIndex = (int)Math.Floor(sector);
        double frac = sector - sectorIndex;

        double p = v * (1 - s);
        double q = v * (1 - s * frac);
        double t = v * (1 - s * (1 - frac));

        double r, g_, b;
        switch (sectorIndex)
        {
            case 0: r = v; g_ = t; b = p; break;
            case 1: r = q; g_ = v; b = p; break;
            case 2: r = p; g_ = v; b = t; break;
            case 3: r = p; g_ = q; b = v; break;
            case 4: r = t; g_ = p; b = v; break;
            default: r = v; g_ = p; b = q; break; // 5
        }

        return Color.FromRgb(
            (byte)Math.Clamp(Math.Round(r * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(g_ * 255), 0, 255),
            (byte)Math.Clamp(Math.Round(b * 255), 0, 255));
    }

    /// <summary>
    /// RGB → HSV. Returns (hue 0–360, saturation 0–1, value 0–1). Used to
    /// seed the HSV sliders from an arbitrary input colour.
    /// </summary>
    private static (double h, double s, double v) RgbToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r)
                h = 60 * (((g - b) / delta) % 6);
            else if (max == g)
                h = 60 * (((b - r) / delta) + 2);
            else
                h = 60 * (((r - g) / delta) + 4);
            if (h < 0) h += 360;
        }

        double s = max <= 0 ? 0 : delta / max;
        double v = max;

        return (h, s, v);
    }

    private sealed class PaletteSwatch
    {
        public Color Color { get; init; }
        public string Hex { get; init; } = string.Empty;
    }
}
