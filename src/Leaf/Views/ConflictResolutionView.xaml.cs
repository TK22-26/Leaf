using System.ComponentModel;
using System.Windows;
using Leaf.Services;
using Leaf.Controls.Merge;
using Leaf.ViewModels;

namespace Leaf.Views;

public partial class ConflictResolutionView : Window
{
    private ConflictResolutionViewModel? _viewModel;

    public ConflictResolutionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.MergeCompleted -= ViewModel_MergeCompleted;
            _viewModel.RequestScrollToRegion -= ViewModel_RequestScrollToRegion;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        // Detach scroll sync from old editors
        OursEditor.ScrollOffsetChanged -= OnOursScrollOffsetChanged;
        TheirsEditor.ScrollOffsetChanged -= OnTheirsScrollOffsetChanged;
        MergedEditor.ScrollOffsetChanged -= OnMergedScrollOffsetChanged;

        _viewModel = e.NewValue as ConflictResolutionViewModel;

        if (_viewModel != null)
        {
            _viewModel.MergeCompleted += ViewModel_MergeCompleted;
            _viewModel.RequestScrollToRegion += ViewModel_RequestScrollToRegion;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Attach scroll sync
            OursEditor.ScrollOffsetChanged += OnOursScrollOffsetChanged;
            TheirsEditor.ScrollOffsetChanged += OnTheirsScrollOffsetChanged;
            MergedEditor.ScrollOffsetChanged += OnMergedScrollOffsetChanged;

            Log.Info("MergeUI", $"WindowOpened: files={_viewModel.TotalCount} source={_viewModel.SourceBranch} target={_viewModel.TargetBranch}");

            // If mappings were already built before we subscribed, push them now
            if (_viewModel.OursLineMapping != null || _viewModel.TheirsLineMapping != null)
                UpdateEditors();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
            Log.Info("MergeUI", $"WindowClosed: filesResolved={_viewModel.ResolvedCount}/{_viewModel.TotalCount}");

        OursEditor.ClearContent();
        TheirsEditor.ClearContent();
        base.OnClosed(e);
    }

    private void ViewModel_MergeCompleted(object? sender, bool success)
    {
        Close();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null) return;

        // When line mappings change, push content to editors
        if (e.PropertyName == nameof(ConflictResolutionViewModel.OursLineMapping) ||
            e.PropertyName == nameof(ConflictResolutionViewModel.TheirsLineMapping))
        {
            UpdateEditors();
        }
    }

    private void UpdateEditors()
    {
        if (_viewModel == null) return;

        var filePath = _viewModel.SelectedConflict?.FilePath ?? string.Empty;

        if (_viewModel.OursLineMapping != null)
        {
            OursEditor.SetContent(_viewModel.OursFileContent, filePath, _viewModel.OursLineMapping, _viewModel);
        }
        else
        {
            OursEditor.ClearContent();
        }

        if (_viewModel.TheirsLineMapping != null)
        {
            TheirsEditor.SetContent(_viewModel.TheirsFileContent, filePath, _viewModel.TheirsLineMapping, _viewModel);
        }
        else
        {
            TheirsEditor.ClearContent();
        }
    }

    private void ViewModel_RequestScrollToRegion(object? sender, int regionIndex)
    {
        if (_viewModel?.CurrentMergeResult == null) return;
        if (regionIndex < 0 || regionIndex >= _viewModel.CurrentMergeResult.Regions.Count) return;

        Dispatcher.BeginInvoke(() =>
        {
            OursEditor.ScrollToRegion(regionIndex);
            TheirsEditor.ScrollToRegion(regionIndex);
        });
    }

    // --- Scroll sync ---
    // Uses IsMouseOver to distinguish user-initiated scrolls from programmatic echo events.
    // When we programmatically scroll Theirs in response to Ours, Theirs fires its own
    // ScrollOffsetChanged — but the mouse is over Ours, not Theirs, so the echo is ignored.

    private void OnOursScrollOffsetChanged(object? sender, double offset)
    {
        if (_viewModel?.IsSyncScrollEnabled != true) return;
        if (!OursEditor.IsMouseOver && !OursEditor.IsKeyboardFocusWithin) return;

        // Raw offset for structurally aligned Ours↔Theirs
        TheirsEditor.ApplyScrollOffset(offset);
        // Ratio for Merged (different content length)
        MergedEditor.ApplyScrollRatio(OursEditor.GetScrollRatio());
    }

    private void OnTheirsScrollOffsetChanged(object? sender, double offset)
    {
        if (_viewModel?.IsSyncScrollEnabled != true) return;
        if (!TheirsEditor.IsMouseOver && !TheirsEditor.IsKeyboardFocusWithin) return;

        // Raw offset for structurally aligned Theirs↔Ours
        OursEditor.ApplyScrollOffset(offset);
        // Ratio for Merged (different content length)
        MergedEditor.ApplyScrollRatio(TheirsEditor.GetScrollRatio());
    }

    private void OnMergedScrollOffsetChanged(object? sender, double ratio)
    {
        if (_viewModel?.IsSyncScrollEnabled != true) return;
        if (!MergedEditor.IsMouseOver && !MergedEditor.IsKeyboardFocusWithin) return;

        OursEditor.ApplyScrollRatio(ratio);
        TheirsEditor.ApplyScrollRatio(ratio);
    }
}
