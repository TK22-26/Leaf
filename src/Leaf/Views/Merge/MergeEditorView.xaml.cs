#nullable enable
using System.Windows;
using System.Windows.Controls;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Leaf.Services;
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
        // C1: restore persisted grid-splitter widths/heights on load and
        // save the final values on close. Settings persist per user.
        Loaded += OnMergeEditorLoaded;
        Closing += OnMergeEditorClosing;
    }

    private static SettingsService ResolveSettingsService() =>
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<SettingsService>(Leaf.App.Services);

    // Loaded can re-fire whenever the element re-enters the visual tree
    // (tab recycling, ContentControl swap, re-parenting in a layout change).
    // Without this gate each reload would: (a) overwrite the user's current
    // splitter layout with the persisted settings, and (b) stack another
    // pair of GotKeyboardFocus / LostKeyboardFocus handlers on each card —
    // every focus change would then kick two pulses racing on the same
    // BorderBrush. Flip on first load; never unset (the editor lives for
    // one merge session).
    private bool _loadedOnce;

    // Defensive bounds so a corrupt or hand-edited settings file (Infinity,
    // NaN, negative, absurdly large) can't crash the editor's first paint:
    // new GridLength(double.PositiveInfinity, Star) throws ArgumentException,
    // and NaN bypasses every `> 0` gate. Width clamp allows 40–4000 px
    // (narrow sidebar through dual-4K setup); star ratios allow 0.1–10 (any
    // pane at least 1/10 of its siblings, never more than 10x).
    internal const double MinFileListWidthPx = 40.0;
    internal const double MaxFileListWidthPx = 4000.0;
    internal const double MinPaneRatio = 0.1;
    internal const double MaxPaneRatio = 10.0;

    /// <summary>
    /// Reject non-finite / non-positive values and clamp everything else
    /// into the allowed range before constructing a <see cref="GridLength"/>.
    /// Exposed as <c>internal</c> so unit tests can pin the coercion table
    /// without having to stand up a full MergeEditorView.
    /// </summary>
    internal static bool TryCoerceWidth(double raw, double min, double max, out double clamped)
    {
        if (!double.IsFinite(raw) || raw <= 0)
        {
            clamped = 0;
            return false;
        }
        clamped = Math.Clamp(raw, min, max);
        return true;
    }

    private void OnMergeEditorLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;
        _loadedOnce = true;

        var settings = ResolveSettingsService().LoadSettings();
        if (TryCoerceWidth(settings.MergeFileListWidth, MinFileListWidthPx, MaxFileListWidthPx, out var fileListWidth))
        {
            FileListColumn.Width = new GridLength(fileListWidth, GridUnitType.Pixel);
        }
        if (TryCoerceWidth(settings.MergeOursPaneRatio, MinPaneRatio, MaxPaneRatio, out var oursRatio))
        {
            OursColumn.Width = new GridLength(oursRatio, GridUnitType.Star);
        }
        if (TryCoerceWidth(settings.MergeTheirsPaneRatio, MinPaneRatio, MaxPaneRatio, out var theirsRatio))
        {
            TheirsColumn.Width = new GridLength(theirsRatio, GridUnitType.Star);
        }
        if (TryCoerceWidth(settings.MergeResultRowRatio, MinPaneRatio, MaxPaneRatio, out var resultRatio))
        {
            ResultRow.Height = new GridLength(resultRatio, GridUnitType.Star);
        }

        // V5: wire the Merge.Motion.PaneFocus animated pulse on Ours / Theirs /
        // Result PaneCard borders. GotKeyboardFocus / LostKeyboardFocus are
        // routed events that fire when focus enters or leaves the subtree, so
        // hooking the card Border catches focus landing on any descendant
        // (scroll viewer, pane body, embedded text editor).
        WirePaneFocusPulse(OursCard);
        WirePaneFocusPulse(TheirsCard);
        WirePaneFocusPulse(ResultCard);
    }

    private static void WirePaneFocusPulse(System.Windows.Controls.Border card)
    {
        // On both focus-in and focus-out, pass restoreResourceKey so the
        // animation's Completed hook rebinds BorderBrush back to its palette
        // DynamicResource key. Without the rebind on GotKeyboardFocus, the
        // focused pane's BorderBrush stays pinned at the snapshot colour the
        // animation ended on — a V8 theme swap while the pane is focused
        // would fail to repaint it until focus leaves.
        card.GotKeyboardFocus += (_, _) =>
            Leaf.Controls.Merge.MergeMotionHelpers.PulsePaneFocusColour(
                card,
                Leaf.Controls.Merge.MergePaletteResources.ResolveColor("Merge.Border.Focus.Color"),
                restoreResourceKey: "Merge.Border.Focus");
        card.LostKeyboardFocus += (_, _) =>
            Leaf.Controls.Merge.MergeMotionHelpers.PulsePaneFocusColour(
                card,
                Leaf.Controls.Merge.MergePaletteResources.ResolveColor("Merge.Border.Subtle.Color"),
                restoreResourceKey: "Merge.Border.Subtle");
    }

    private void OnMergeEditorClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var service = ResolveSettingsService();
        var settings = service.LoadSettings();
        // Persist both ActualWidth and star-Value so round-tripping works
        // whether the user left the splitter at a pixel position or a
        // star-value ratio. Star columns report their star-count via
        // Width.Value; pixel columns report the pixel width via ActualWidth.
        settings.MergeFileListWidth = FileListColumn.ActualWidth > 0
            ? FileListColumn.ActualWidth
            : settings.MergeFileListWidth;
        settings.MergeOursPaneRatio = OursColumn.Width.IsStar
            ? OursColumn.Width.Value
            : OursColumn.ActualWidth;
        settings.MergeTheirsPaneRatio = TheirsColumn.Width.IsStar
            ? TheirsColumn.Width.Value
            : TheirsColumn.ActualWidth;
        settings.MergeResultRowRatio = ResultRow.Height.IsStar
            ? ResultRow.Height.Value
            : ResultRow.ActualHeight;
        service.SaveSettings(settings);
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
            _subscribedVm.CompareRequested += OnCompareRequested;
        }
    }

    private void DetachFromVm()
    {
        if (_subscribedVm is null) return;
        _subscribedVm.RangeStatesChanged -= OnRangeStatesChanged;
        _subscribedVm.AiConsentRequested -= OnAiConsentRequested;
        _subscribedVm.AiResolutionReceived -= OnAiResolutionReceived;
        _subscribedVm.AiError -= OnAiError;
        _subscribedVm.CompareRequested -= OnCompareRequested;
    }

    private void OnCompareRequested(object? sender, int rangeIndex)
    {
        // CodeLens "Compare" → scroll Ours + Theirs panes so the user can see
        // both versions of this range side-by-side before picking. The result
        // pane is left alone because its content is the composed output, not
        // a raw "Ours" or "Theirs" view.
        var range = Vm?.Document?.Ranges.FirstOrDefault(r => r.Index == rangeIndex);
        if (range is null) return;
        ScrollPaneToLine(OursScrollViewer, range.Ours.StartLine);
        ScrollPaneToLine(TheirsScrollViewer, range.Theirs.StartLine);
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

    /// <summary>
    /// Phase 6: reset the image pane's viewport (zoom + pan) so a user who's
    /// panned the image off-screen can recover without switching files.
    /// Mode, swipe, onion-skin are preserved — the common "lost the image"
    /// case is a zoom/pan accident, not a mode choice.
    /// </summary>
    private void OnResetImageView_Click(object sender, RoutedEventArgs e)
    {
        Vm?.ImageViewport?.ResetView();
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

    // V5 tracks the previous range-state snapshot so OnRangeStatesChanged can
    // detect newly-resolved ranges and kick their Merge.Motion.RangeResolve
    // fade-in. Plain Dictionary copy — the RangeStates reference is mutated
    // in place, so we need a by-value snapshot to diff against.
    private Dictionary<int, Leaf.Models.Merge.ResolutionState>? _previousRangeStates;

    private void OnRangeStatesChanged(object? sender, EventArgs e)
    {
        // V5: detect any state change (including resolved→resolved switches
        // like AcceptOurs→AcceptTheirs) and kick the Merge.Motion.RangeResolve
        // fade-in on both input panes so the two sides animate in lockstep.
        // Restarting the fade on an already-resolved range is fine — the
        // animation is idempotent, giving the user a confirmation pulse each
        // time they flip the resolution.
        var current = Vm?.RangeStates;
        if (current is not null && _previousRangeStates is not null)
        {
            foreach (var kvp in current)
            {
                var previouslyHad = _previousRangeStates.TryGetValue(kvp.Key, out var prev);
                var stateChanged = !previouslyHad || !Equals(prev, kvp.Value);
                if (!stateChanged) continue;
                var nowResolved = kvp.Value is not Leaf.Models.Merge.ResolutionState.Unresolved;
                if (nowResolved)
                {
                    OursPane.StartRangeResolveAnimation(kvp.Key);
                    TheirsPane.StartRangeResolveAnimation(kvp.Key);
                }
                // C2: SegmentedAcceptPill handles its own state-change visual
                // via UpdateCellHighlighting when its State DP changes; no
                // per-pane animation needed anymore.
            }
        }
        _previousRangeStates = current is null
            ? null
            : new Dictionary<int, Leaf.Models.Merge.ResolutionState>(current);

        // Invalidate both input panes so the change-bar / overlay re-render
        // after any resolution-changing operation (pill click, footer
        // AcceptAllOurs/Theirs, Undo, Redo). RangeStates is a plain dictionary
        // — this is the designated re-render channel. Controls whose
        // rendering depends on RangeStates need an explicit refresh because
        // dictionary mutation in place doesn't fire WPF DP change
        // notifications: the pill overlay caches per-range State on its
        // children, and the three sticky headers cache a "· <state>" label.
        OursPane.InvalidateVisual();
        TheirsPane.InvalidateVisual();
        SegmentedPills?.RefreshPillStates();
        OursSticky?.RefreshState();
        TheirsSticky?.RefreshState();
        ResultSticky?.RefreshState();
    }

    // ── Scroll / minimap wire-up (Phase 4) ───────────────────────────────

    private bool _suppressScrollSync;

    private void OnOursScrollChanged(object? sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (Vm is null) return;
        ConnectionCanvas.OursVerticalOffset = e.VerticalOffset;
        if (OursSticky is not null) OursSticky.VerticalOffset = e.VerticalOffset;
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
        if (TheirsSticky is not null) TheirsSticky.VerticalOffset = e.VerticalOffset;
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
        // V5: smooth animated scroll via Merge.Motion.MinimapJump (400 ms
        // ease-out) — replaces the previous instant jump so the user can
        // track which direction the pane moved.
        Leaf.Controls.Merge.MergeMotionHelpers.SmoothScrollTo(sv, target);
    }

}
