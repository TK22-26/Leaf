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
        // N1: detach the RangeStatesChanged subscription when the window
        // closes so a re-opened editor doesn't accumulate a fresh handler
        // each time. The VM outlives the window (owned by MainViewModel
        // until merge completes), so the subscription must be released
        // explicitly on Close.
        Closed += (_, _) =>
        {
            if (_subscribedVm is not null)
            {
                _subscribedVm.RangeStatesChanged -= OnRangeStatesChanged;
                _subscribedVm = null;
            }
        };
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
        // Hard-block foot-gun: the Phase 2c ResultPane is IsReadOnly=true so this
        // handler cannot be reached via user input. If a future developer flips
        // IsReadOnly without first implementing range-aware manual-edit routing,
        // the pre-fix whole-buffer-to-Ranges[0] bug would return and silently
        // corrupt committed output. Fail loudly instead.
        throw new NotImplementedException(
            "Manual editing of the Result pane is not supported in Phase 2c " +
            "(ResultPane.IsReadOnly=true). Phase 3 will reintroduce it with " +
            "per-range text mapping so only the touched range becomes Manual.");
    }
}
