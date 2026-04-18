#nullable enable
using System.Windows;
using System.Windows.Controls;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Leaf.ViewModels.Merge;

namespace Leaf.Views.Merge;

/// <summary>
/// Top-level view for the Phase 2c merge editor. Hosts a <see cref="ListBox"/> file list
/// plus the two <see cref="ReadOnlyMergePane"/> input panes and a
/// <see cref="ResultPane"/> for the editable composed output. DataContext is a
/// <see cref="MergeEditorViewModel"/>.
/// </summary>
public partial class MergeEditorView : Window
{
    private MergeEditorViewModel? _subscribedVm;

    public MergeEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MergeEditorViewModel? Vm => DataContext as MergeEditorViewModel;

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.RangeStatesChanged -= OnRangeStatesChanged;
        }
        _subscribedVm = Vm;
        if (_subscribedVm is not null)
        {
            _subscribedVm.RangeStatesChanged += OnRangeStatesChanged;
        }
    }

    private void OnRangeStatesChanged(object? sender, EventArgs e)
    {
        // Invalidate both input panes so the accept-checkbox glyphs re-render
        // after any resolution-changing operation (checkbox click, footer
        // AcceptAllOurs/Theirs, Undo, Redo). RangeStates is a plain dictionary
        // — this is the designated re-render channel.
        OursPane.InvalidateVisual();
        TheirsPane.InvalidateVisual();
    }

    private void OnOursCheckboxToggled(object sender, MergePaneCheckboxEventArgs e)
    {
        ApplyCheckbox(e);
    }

    private void OnTheirsCheckboxToggled(object sender, MergePaneCheckboxEventArgs e)
    {
        ApplyCheckbox(e);
    }

    private void ApplyCheckbox(MergePaneCheckboxEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;

        // Determine whether the OTHER side is currently accepted. If yes, the
        // new state is AcceptBoth (composable); otherwise it's a single-side accept.
        var otherSide = e.Side == MergePaneSide.Ours ? MergePaneSide.Theirs : MergePaneSide.Ours;
        var otherAccepted = vm.RangeStates.TryGetValue(e.RangeIndex, out var st) && st switch
        {
            ResolutionState.AcceptBoth => true,
            ResolutionState.AcceptOurs => otherSide == MergePaneSide.Ours,
            ResolutionState.AcceptTheirs => otherSide == MergePaneSide.Theirs,
            _ => false,
        };

        // Compute new state.
        if (e.IsAccepted && otherAccepted)
        {
            // Both now accepted; preserve whichever was clicked first as "FirstOurs" hint.
            var firstOurs = e.Side == MergePaneSide.Ours;
            vm.AcceptBothCommand.Execute(e.RangeIndex);
            // AcceptBoth via command defaults to firstOurs=true; re-apply if theirs was clicked.
            if (!firstOurs) vm.AcceptBothTheirsFirstCommand.Execute(e.RangeIndex);
        }
        else if (e.IsAccepted)
        {
            if (e.Side == MergePaneSide.Ours) vm.AcceptOursCommand.Execute(e.RangeIndex);
            else vm.AcceptTheirsCommand.Execute(e.RangeIndex);
        }
        else
        {
            vm.UnresolveCommand.Execute(e.RangeIndex);
        }

        // RangeStatesChanged fires inside each command, re-rendering is handled by
        // OnRangeStatesChanged (subscribed from OnDataContextChanged).
    }

    private void OnResultTextChanged(object? sender, string text)
    {
        // Manual edit is a whole-buffer operation for now. A future Phase can scope
        // this to the specific edited range.
        var vm = Vm;
        if (vm is null || vm.Document is null) return;

        // Heuristic: treat the entire result as a single manual override on the first range.
        // This is intentionally coarse for Phase 2c — the audit will refine.
        if (vm.Document.Ranges.Count > 0)
        {
            vm.ApplyManualText(vm.Document.Ranges[0].Index, text);
        }
    }
}
