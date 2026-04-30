using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Leaf.Models;
using Leaf.Services;
using Microsoft.Win32;

namespace Leaf.Views;

/// <summary>
/// File-picker + preview-list dialog used by
/// <see cref="ViewModels.MainViewModel.ApplyPatchAsync"/>. The dialog
/// owns the picked file list (so the user can re-order / remove without
/// reopening the file dialog) and exposes the chosen
/// <see cref="ApplyPatchStrategy"/> on OK. Header parsing is delegated
/// to <see cref="IPatchService.PreviewAsync"/> so the same logic that
/// drives validation also drives the displayed metadata.
/// </summary>
public partial class ApplyPatchDialog : Window
{
    private readonly IPatchService _patchService;
    private readonly ObservableCollection<PatchPreviewItem> _items = new();

    public ApplyPatchDialog(IPatchService patchService)
    {
        _patchService = patchService ?? throw new ArgumentNullException(nameof(patchService));
        InitializeComponent();
        PreviewList.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => UpdateState();
        UpdateState();
    }

    /// <summary>The patches to apply, in list order.</summary>
    public IReadOnlyList<string> PatchFiles { get; private set; } = [];

    public ApplyPatchStrategy Strategy { get; private set; } = ApplyPatchStrategy.Am;

    private void UpdateState()
    {
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = _items.Count > 0;
        var selected = PreviewList.SelectedItems.Count;
        RemoveSelectedButton.IsEnabled = selected > 0;
        // Reorder is single-selection only — multi-select Move would
        // either need batched index math or pile the moved items into a
        // contiguous run, both of which surprise the user.
        var single = selected == 1;
        var idx = single ? PreviewList.SelectedIndex : -1;
        MoveUpButton.IsEnabled = single && idx > 0;
        MoveDownButton.IsEnabled = single && idx >= 0 && idx < _items.Count - 1;
    }

    private void PreviewList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateState();

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Patch Files",
            Filter = "Patch files (*.patch;*.mbox;*.eml)|*.patch;*.mbox;*.eml|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        // Skip duplicates so re-clicking Add doesn't blow up the list when
        // the user re-picks a file. Set semantics on the path string is
        // close enough — case-insensitive on Windows.
        var existing = new HashSet<string>(_items.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
        // Sort by file name so format-patch's 0001-, 0002-, ... ordering
        // is preserved regardless of the order the OS returned them in,
        // and so a second batch interleaves correctly with an existing
        // list. `git am` applies in argument order, so this directly
        // governs commit order.
        var toAdd = dialog.FileNames
            .Where(p => existing.Add(p))
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (toAdd.Count == 0) return;

        try
        {
            // PreviewAsync is pure I/O (file reads). For very large patch
            // batches we still hop to a background thread so the UI stays
            // responsive while we parse — the dialog is otherwise frozen
            // through the await on its own dispatcher.
            var previews = await Task.Run(() => _patchService.PreviewAsync(toAdd));
            foreach (var p in previews) _items.Add(p);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Apply Patch", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = PreviewList.SelectedItems.Cast<PatchPreviewItem>().ToList();
        foreach (var item in selected) _items.Remove(item);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var idx = PreviewList.SelectedIndex;
        if (idx <= 0) return;
        _items.Move(idx, idx - 1);
        PreviewList.SelectedIndex = idx - 1;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var idx = PreviewList.SelectedIndex;
        if (idx < 0 || idx >= _items.Count - 1) return;
        _items.Move(idx, idx + 1);
        PreviewList.SelectedIndex = idx + 1;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        PatchFiles = _items.Select(i => i.FilePath).ToList();
        Strategy = ApplyStrategyRadio.IsChecked == true ? ApplyPatchStrategy.Apply : ApplyPatchStrategy.Am;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
