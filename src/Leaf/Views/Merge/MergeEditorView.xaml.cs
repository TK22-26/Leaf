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
            DetachFromVm();
            _subscribedVm = null;
        };
    }

    private MergeEditorViewModel? Vm => DataContext as MergeEditorViewModel;

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromVm();
        _subscribedVm = Vm;
        if (_subscribedVm is not null)
        {
            _subscribedVm.RangeStatesChanged += OnRangeStatesChanged;
            _subscribedVm.AiConsentRequested += OnAiConsentRequested;
            _subscribedVm.AiResolutionReceived += OnAiResolutionReceived;
            _subscribedVm.AiError += OnAiError;
        }
    }

    private void DetachFromVm()
    {
        if (_subscribedVm is null) return;
        _subscribedVm.RangeStatesChanged -= OnRangeStatesChanged;
        _subscribedVm.AiConsentRequested -= OnAiConsentRequested;
        _subscribedVm.AiResolutionReceived -= OnAiResolutionReceived;
        _subscribedVm.AiError -= OnAiError;
    }

    private void OnAiConsentRequested(object? sender, AiConsentRequest e)
    {
        if (Vm is null) return;
        var dlg = new AiConsentDialog(e.McpServerPath, e.ContextLines)
        {
            Owner = this,
        };
        var accepted = dlg.ShowDialog() == true;
        if (accepted)
        {
            // Persist consent so this dialog doesn't appear again on next click.
            // SettingsService is a DI singleton — resolving from the app-wide
            // provider here is acceptable because the view is the consent
            // flow's owner; keeping the write in the dialog keeps the VM
            // decoupled from SettingsService.
            var settings = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Leaf.Services.SettingsService>(Leaf.App.Services);
            var current = settings.LoadSettings();
            current.AiMergeConsentGiven = true;
            // Also flip the master toggle on — the feature is obviously enabled
            // if the user just consented. Without this, a user who enabled
            // "show the AI button" via settings but never flipped consent
            // would still be blocked on the next click.
            current.AiMergeEnabled = true;
            settings.SaveSettings(current);
            Vm.ResumeAiRequestAfterConsent();
        }
        else
        {
            Vm.CancelPendingAiRequest();
        }
    }

    private void OnAiResolutionReceived(object? sender, AiResolutionProposal e)
    {
        if (Vm is null) return;
        var dlg = new AiResolutionDialog(e.ProposedText, e.Rationale, e.Confidence)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() == true)
        {
            Vm.AcceptAiResolution(e.RangeIndex, dlg.AcceptedText);
        }
    }

    private void OnAiError(object? sender, string message)
    {
        MessageBox.Show(
            this,
            message,
            "AI merge assistant",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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

    // ── Scroll / minimap wire-up (Phase 4) ───────────────────────────────

    private bool _suppressScrollSync;

    private void OnOursScrollChanged(object? sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (Vm is null) return;
        ConnectionCanvas.OursVerticalOffset = e.VerticalOffset;
        // Sync the Theirs pane to the same vertical offset. The canvas draws
        // straight-across bezier curves between matching line indices, which
        // only remains meaningful when the two panes scroll together. A
        // flag prevents re-entrant ping-ponging when the mirrored scroll
        // fires its own ScrollChanged.
        if (!_suppressScrollSync && TheirsScrollViewer is not null
            && Math.Abs(TheirsScrollViewer.VerticalOffset - e.VerticalOffset) > 0.5)
        {
            _suppressScrollSync = true;
            try { TheirsScrollViewer.ScrollToVerticalOffset(e.VerticalOffset); }
            finally { _suppressScrollSync = false; }
        }
    }

    private void OnTheirsScrollChanged(object? sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (Vm is null) return;
        ConnectionCanvas.TheirsVerticalOffset = e.VerticalOffset;
        if (!_suppressScrollSync && OursScrollViewer is not null
            && Math.Abs(OursScrollViewer.VerticalOffset - e.VerticalOffset) > 0.5)
        {
            _suppressScrollSync = true;
            try { OursScrollViewer.ScrollToVerticalOffset(e.VerticalOffset); }
            finally { _suppressScrollSync = false; }
        }
    }

    private void OnOursMinimapJump(object? sender, MinimapJumpEventArgs e)
    {
        ScrollPaneToLine(OursScrollViewer, e.LineNumber);
    }

    private void OnTheirsMinimapJump(object? sender, MinimapJumpEventArgs e)
    {
        ScrollPaneToLine(TheirsScrollViewer, e.LineNumber);
    }

    private void ScrollPaneToLine(System.Windows.Controls.ScrollViewer sv, int lineNumber1Based)
    {
        var layout = Vm?.Layout;
        if (layout is null || sv is null) return;
        var y = layout.GetVisualTop(lineNumber1Based);
        // Center the target line in the viewport when possible.
        var target = Math.Max(0, y - sv.ViewportHeight / 2);
        sv.ScrollToVerticalOffset(target);
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
