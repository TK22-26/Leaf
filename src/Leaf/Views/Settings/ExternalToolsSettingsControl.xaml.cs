using System.Windows;
using System.Windows.Controls;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings panel for external diff/merge tools. All writes go through
/// <see cref="IExternalToolConfigService"/> at <see cref="GitConfigScope.Global"/>
/// — a per-repo override can come later if it proves needed.
/// </summary>
public partial class ExternalToolsSettingsControl : UserControl
{
    private IExternalToolConfigService? _configService;
    private IExternalToolDetectorService? _detector;
    private string? _repoPath;
    private bool _suppressComboEvents;

    // Sentinel entry so "no external tool" is a real pickable ComboBox item.
    private static readonly ExternalTool DiffNone = ExternalTool.BuiltIn(ExternalToolKind.Diff);
    private static readonly ExternalTool MergeNone = ExternalTool.BuiltIn(ExternalToolKind.Merge);

    public ExternalToolsSettingsControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Supply services + a repo path (git config --global still needs a
    /// working directory for the process launch, though the actual write
    /// targets ~/.gitconfig). Called by the host dialog before the
    /// section is shown.
    /// </summary>
    public async Task BindAsync(IExternalToolConfigService configService, IExternalToolDetectorService detector, string repoPath)
    {
        _configService = configService;
        _detector = detector;
        _repoPath = repoPath;

        PopulatePresets();
        await LoadCurrentAsync();
    }

    private void PopulatePresets()
    {
        _suppressComboEvents = true;
        try
        {
            DiffToolCombo.Items.Clear();
            DiffToolCombo.Items.Add(DiffNone);
            foreach (var preset in ExternalToolPresets.Diff)
            {
                DiffToolCombo.Items.Add(preset);
            }
            DiffToolCombo.SelectedIndex = 0;

            MergeToolCombo.Items.Clear();
            MergeToolCombo.Items.Add(MergeNone);
            foreach (var preset in ExternalToolPresets.Merge)
            {
                MergeToolCombo.Items.Add(preset);
            }
            MergeToolCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressComboEvents = false;
        }
    }

    private async Task LoadCurrentAsync()
    {
        if (_configService == null || _repoPath == null) return;

        try
        {
            var diff = await _configService.GetCurrentToolAsync(_repoPath, ExternalToolKind.Diff);
            var merge = await _configService.GetCurrentToolAsync(_repoPath, ExternalToolKind.Merge);

            _suppressComboEvents = true;
            ApplyToUi(DiffToolCombo, DiffCommandBox, DiffArgsBox, diff, DiffNone);
            ApplyToUi(MergeToolCombo, MergeCommandBox, MergeArgsBox, merge, MergeNone);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to read config: {ex.Message}";
        }
        finally
        {
            _suppressComboEvents = false;
        }
    }

    private static void ApplyToUi(ComboBox combo, TextBox commandBox, TextBox argsBox, ExternalTool? current, ExternalTool sentinel)
    {
        if (current == null)
        {
            combo.SelectedItem = sentinel;
            commandBox.Text = string.Empty;
            argsBox.Text = string.Empty;
            return;
        }

        var match = combo.Items.OfType<ExternalTool>()
            .FirstOrDefault(t => string.Equals(t.Name, current.Name, StringComparison.OrdinalIgnoreCase));
        combo.SelectedItem = match ?? sentinel;
        commandBox.Text = current.Command;
        argsBox.Text = current.ArgsTemplate;
    }

    private void DiffToolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboEvents) return;
        if (DiffToolCombo.SelectedItem is not ExternalTool tool) return;
        DiffCommandBox.Text = tool.Command;
        DiffArgsBox.Text = tool.ArgsTemplate;
    }

    private void MergeToolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboEvents) return;
        if (MergeToolCombo.SelectedItem is not ExternalTool tool) return;
        MergeCommandBox.Text = tool.Command;
        MergeArgsBox.Text = tool.ArgsTemplate;
    }

    private async void DetectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detector == null) return;
        StatusText.Text = "Detecting…";
        try
        {
            _detector.InvalidateCache();
            var installed = await _detector.GetInstalledToolNamesAsync();
            StatusText.Text = installed.Count == 0
                ? "No installed tools detected."
                : $"Detected: {string.Join(", ", installed.OrderBy(n => n))}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Detection failed: {ex.Message}";
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_configService == null || _repoPath == null) return;

        try
        {
            await ApplyKindAsync(ExternalToolKind.Diff, DiffToolCombo, DiffCommandBox.Text.Trim(), DiffArgsBox.Text.Trim());
            await ApplyKindAsync(ExternalToolKind.Merge, MergeToolCombo, MergeCommandBox.Text.Trim(), MergeArgsBox.Text.Trim());
            StatusText.Text = "Saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private async Task ApplyKindAsync(ExternalToolKind kind, ComboBox combo, string command, string args)
    {
        if (_configService == null || _repoPath == null) return;
        if (combo.SelectedItem is not ExternalTool selection) return;

        if (selection.IsBuiltIn)
        {
            await _configService.ClearSelectedToolAsync(_repoPath, kind, GitConfigScope.Global);
            return;
        }

        // Preserve the user's customisations — if they edited the
        // command or args after picking a preset, the edits win.
        var tool = selection with { Command = command, ArgsTemplate = args };
        await _configService.SetSelectedToolAsync(_repoPath, tool, GitConfigScope.Global);
    }
}
