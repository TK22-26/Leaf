#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Controls.Merge;
using Leaf.Models.Merge;
using Leaf.Services;
using Leaf.Services.Shortcuts;
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
    private BlameHoverController? _blameHover;
    private System.Windows.Controls.Primitives.Popup? _notePopup;
    private NoteEditor? _noteEditor;
    private int _activeNoteRange = -1;

    public MergeEditorView()
    {
        // RelayCommand comes from CommunityToolkit.Mvvm, already a
        // project-level PackageReference. The VM layer uses [RelayCommand]
        // code-gen; at the view layer we instantiate RelayCommand directly
        // since the action lives in the code-behind, not the VM.
        ShowBlamePeekForCurrentConflictCommand =
            new CommunityToolkit.Mvvm.Input.RelayCommand(ShowBlamePeekForCurrentConflict);
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // §5.9: drive InputBindings through IShortcutService so user
        // overrides take effect. ApplyShortcuts runs once the DataContext
        // (the VM) is set — without it, the command bindings have no
        // target. We re-apply on GestureChanged so a Settings rebind
        // takes effect even while the editor is open.
        _shortcutService = ResolveShortcutService();
        if (_shortcutService is not null)
        {
            _shortcutService.GestureChanged += OnShortcutGestureChanged;
        }

        // N1: detach the RangeStatesChanged subscription when the window
        // closes so a re-opened editor doesn't accumulate a fresh handler
        // each time. The VM outlives the window (owned by MainViewModel
        // until merge completes), so the subscription must be released
        // explicitly on Close.
        Closed += (_, _) =>
        {
            DetachFromVm();
            _subscribedVm = null;
            _blameHover?.Dispose();
            _blameHover = null;
            if (_shortcutService is not null)
            {
                _shortcutService.GestureChanged -= OnShortcutGestureChanged;
            }
            // C6: mirror the VM-detach pattern for the pane-event subscriptions
            // set up in WireNoteEditor. Panes are owned by this window so GC
            // would reclaim them either way, but explicit detach keeps the
            // subscription table symmetric.
            OursPane.NoteEditRequested -= OnPaneNoteEditRequested;
            TheirsPane.NoteEditRequested -= OnPaneNoteEditRequested;
            if (_noteEditor is not null)
            {
                _noteEditor.CommitRequested -= OnNoteEditorCommit;
            }
            if (_notePopup is not null)
            {
                _notePopup.IsOpen = false;
                _notePopup.PlacementTarget = null;
            }
        };
        // C1: restore persisted grid-splitter widths/heights on load and
        // save the final values on close. Settings persist per user.
        Loaded += OnMergeEditorLoaded;
        Closing += OnMergeEditorClosing;
    }

    private readonly IShortcutService? _shortcutService;

    private static IShortcutService? ResolveShortcutService()
    {
        // Same service-locator pattern other parts of MergeEditorView
        // use (ResolveSettingsService etc.). Returns null at design-time
        // when App.Services isn't built — the editor still functions,
        // just without runtime-customisable shortcuts.
        if (Leaf.App.Services is null) return null;
        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<IShortcutService>(Leaf.App.Services);
    }

    private void OnShortcutGestureChanged(object? sender, string? commandId) => ApplyShortcuts();

    private void ApplyShortcuts()
    {
        if (_shortcutService is null) return;
        if (DataContext is not MergeEditorViewModel vm) return;

        // Preserve the single hardcoded alias declared in XAML
        // (Ctrl+Shift+Z = Redo). Strip everything else and rebuild from
        // the registry so a Settings rebind is visible immediately.
        for (var i = InputBindings.Count - 1; i >= 0; i--)
        {
            if (InputBindings[i] is KeyBinding kb &&
                kb.Key == Key.Z && kb.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                continue;
            }
            InputBindings.RemoveAt(i);
        }

        BindMerge(ShortcutCommandId.Merge.AcceptOurs, vm.AcceptCurrentConflictOursCommand);
        BindMerge(ShortcutCommandId.Merge.AcceptTheirs, vm.AcceptCurrentConflictTheirsCommand);
        BindMerge(ShortcutCommandId.Merge.AcceptBoth, vm.AcceptCurrentConflictBothCommand);
        BindMerge(ShortcutCommandId.Merge.NextConflict, vm.NextConflictCommand);
        BindMerge(ShortcutCommandId.Merge.PreviousConflict, vm.PreviousConflictCommand);
        BindMerge(ShortcutCommandId.Merge.NextChangeSpan, vm.NextChangeSpanCommand);
        BindMerge(ShortcutCommandId.Merge.PreviousChangeSpan, vm.PreviousChangeSpanCommand);
        BindMerge(ShortcutCommandId.Merge.NextAutoMergedRegion, vm.NextAutoMergedRegionCommand);
        BindMerge(ShortcutCommandId.Merge.PreviousAutoMergedRegion, vm.PreviousAutoMergedRegionCommand);
        BindMerge(ShortcutCommandId.Merge.OpenPalette, vm.OpenPaletteCommand);
        BindMerge(ShortcutCommandId.Merge.MarkResolved, vm.MarkResolvedCommand);
        BindMerge(ShortcutCommandId.Merge.Undo, vm.UndoCommand);
        BindMerge(ShortcutCommandId.Merge.Redo, vm.RedoCommand);
        BindMerge(ShortcutCommandId.Merge.RequestAiResolution, vm.RequestAiResolutionCommand);
        BindMerge(ShortcutCommandId.Merge.ShowBlamePeek, ShowBlamePeekForCurrentConflictCommand);
    }

    private void BindMerge(string commandId, ICommand command)
    {
        if (_shortcutService is null) return;
        var gesture = _shortcutService.GetGesture(commandId);
        if (gesture is null) return;
        InputBindings.Add(new KeyBinding(command, gesture));
    }

    /// <summary>
    /// Raised when the user clicks the sha link in a BlamePeekPopover.
    /// MainViewModel subscribes and navigates the commit graph to the
    /// clicked commit.
    /// </summary>
    public event EventHandler<string>? CommitJumpRequested;

    /// <summary>
    /// Alt+B handler bound from <see cref="Window.InputBindings"/>. Lives
    /// on the view (not the VM) because the blame popover / hover
    /// controller are view-layer — the VM doesn't know about Popups. Fires
    /// the popover for the current conflict's start line in whichever
    /// read-only pane has keyboard focus (falls back to OursPane).
    /// </summary>
    public System.Windows.Input.ICommand ShowBlamePeekForCurrentConflictCommand { get; }

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

        // C5: blame peek on hover. One controller + popup serves both input
        // panes; hover on either pane drives the same debounce timer so
        // sliding the pointer across them doesn't spawn parallel fetches.
        WireBlameHover();

        // C6: one NoteEditor popup reused by both panes. Each pane raises
        // NoteEditRequested with the clicked range index + glyph rect; the
        // popup anchors to the pane that fired the event.
        WireNoteEditor();
    }

    private void ShowBlamePeekForCurrentConflict()
    {
        if (_blameHover is null || Vm is null || Vm.Document is null) return;
        var conflicting = Vm.Document.ConflictingRanges.ToList();
        if (conflicting.Count == 0) return;
        var idx = Math.Clamp(Vm.CurrentConflictIndex, 0, conflicting.Count - 1);
        var range = conflicting[idx];

        // Pane-selection contract: if focus is inside the Theirs pane,
        // show Theirs-side blame; otherwise show Ours-side blame. The
        // Ours fallback covers:
        //   • focus on the Ours pane (primary case),
        //   • focus on the Result pane — blame against HEAD on composed
        //     result content is semantically incoherent, so the left-
        //     hand ancestor of the result (Ours) is the defensible
        //     default,
        //   • focus elsewhere (file tree, palette, footer) — user
        //     pressed Alt+B without being on a pane; Ours is the
        //     conventional "current side" for single-side views.
        // Line resolves off the selected side's StartLine — that's
        // where the conflict begins on the HEAD-blamed file.
        var focused = System.Windows.Input.Keyboard.FocusedElement;
        ReadOnlyMergePane targetPane;
        int line;
        if (focused is System.Windows.DependencyObject dep
            && IsInTreeOf(dep, TheirsPane))
        {
            targetPane = TheirsPane;
            line = range.Theirs.StartLine;
        }
        else
        {
            targetPane = OursPane;
            line = range.Ours.StartLine;
        }
        if (line < 1) return;

        // Fire-and-forget: ShowForLineAsync awaits the blame fetch but the
        // keybinding handler can't. Exceptions are logged inside the
        // controller; nothing here needs the result.
        _ = _blameHover.ShowForLineAsync(targetPane, line);
    }

    private static bool IsInTreeOf(System.Windows.DependencyObject candidate, System.Windows.DependencyObject ancestor)
    {
        var current = candidate;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                   ?? System.Windows.LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void WireNoteEditor()
    {
        _noteEditor = new NoteEditor();
        _notePopup = new System.Windows.Controls.Primitives.Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            CustomPopupPlacementCallback = PlaceNoteEditor,
            Child = _noteEditor,
        };
        _noteEditor.CommitRequested += OnNoteEditorCommit;
        _noteEditor.CancelRequested += (_, _) => ClosePopup();
        OursPane.NoteEditRequested += OnPaneNoteEditRequested;
        TheirsPane.NoteEditRequested += OnPaneNoteEditRequested;
    }

    /// <summary>
    /// Place the note editor to the right of the clicked glyph by default;
    /// if the popup would clip off the target's right edge, flip to the
    /// left. PlacementMode.Right alone would merely slide the popup along
    /// the target, leaving it partly covering the glyph at the pane's edge.
    /// </summary>
    private static System.Windows.Controls.Primitives.CustomPopupPlacement[] PlaceNoteEditor(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        // `targetSize` is the PlacementRectangle (glyph rect) size.
        // Primary: just to the right of the glyph with an 8 px gap.
        var rightOfGlyph = new System.Windows.Controls.Primitives.CustomPopupPlacement(
            new Point(targetSize.Width + 8, 0),
            System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal);
        // Fallback: just to the left if right-side doesn't fit. Offsets are
        // relative to the PlacementRectangle origin.
        var leftOfGlyph = new System.Windows.Controls.Primitives.CustomPopupPlacement(
            new Point(-popupSize.Width - 8, 0),
            System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal);
        return new[] { rightOfGlyph, leftOfGlyph };
    }

    private void OnPaneNoteEditRequested(object? sender, NoteEditRequestedEventArgs e)
    {
        if (_noteEditor is null || _notePopup is null || Vm is null) return;
        _activeNoteRange = e.RangeIndex;
        // Seed with the existing note (if any) so re-clicking the bubble
        // edits the current note rather than blanking it.
        Vm.RangeStates.TryGetValue(e.RangeIndex, out var state);
        _noteEditor.NoteText = state?.Note ?? string.Empty;
        _notePopup.PlacementTarget = sender as System.Windows.FrameworkElement;
        _notePopup.PlacementRectangle = e.GlyphRect;
        _notePopup.IsOpen = true;
    }

    private void OnNoteEditorCommit(object? sender, string text)
    {
        if (Vm is null || _activeNoteRange < 0) { ClosePopup(); return; }
        // Tuple element names are compile-time metadata only — the
        // [RelayCommand]-generated IRelayCommand<(int,string?)> unboxes the
        // positional tuple regardless of field names on the caller side.
        Vm.AddNoteCommand.Execute((_activeNoteRange, text));
        ClosePopup();
    }

    private void ClosePopup()
    {
        if (_notePopup is not null) _notePopup.IsOpen = false;
        _activeNoteRange = -1;
    }

    private void WireBlameHover()
    {
        var service = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Leaf.Services.Merge.IMergeBlameService>(Leaf.App.Services);
        _blameHover = new BlameHoverController(
            service,
            repoPathProvider: () => Vm?.RepoPath ?? string.Empty,
            filePathProvider: () => Vm?.SelectedConflict?.FilePath,
            commitRequestedCallback: sha => CommitJumpRequested?.Invoke(this, sha));

        // Line-at-point resolver for ReadOnlyMergePane: the pane is hosted in
        // a ScrollViewer that translates the pointer into content-local
        // coordinates automatically (MouseMove fires on the pane itself, so
        // the point is already relative to its un-scrolled content extent).
        BlameHoverController.LineAtPointResolver romps = (pane, point) =>
        {
            if (pane is ReadOnlyMergePane romp && romp.Layout is { } layout && romp.Lines.Count > 0)
            {
                var line = layout.LineIndexAtYOffset(point.Y);
                return line >= 1 && line <= romp.Lines.Count ? line : null;
            }
            return null;
        };
        _blameHover.TrackPane(OursPane, romps);
        _blameHover.TrackPane(TheirsPane, romps);
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
            // §5.9: bindings need the VM in place to resolve their
            // commands, so we apply them here rather than in the
            // constructor.
            ApplyShortcuts();
            _subscribedVm.RangeStatesChanged += OnRangeStatesChanged;
            _subscribedVm.AiConsentRequested += OnAiConsentRequested;
            _subscribedVm.AiResolutionReceived += OnAiResolutionReceived;
            _subscribedVm.AiError += OnAiError;
            _subscribedVm.CompareRequested += OnCompareRequested;
            _subscribedVm.PropertyChanged += OnVmPropertyChanged;
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
        _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
    }

    /// <summary>
    /// Listen for VM property changes that should trigger view-side
    /// effects beyond plain DP binding. Today: <c>CurrentConflictIndex</c>
    /// changes drive a scroll-into-view on all three panes so F8 /
    /// Shift+F8 / sticky-header chevrons / Alt+arrow span navigation
    /// actually move the user's viewport — without this the index just
    /// updates as a private state value and the panes stay where they
    /// were.
    /// </summary>
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergeEditorViewModel.CurrentConflictIndex))
        {
            ScrollAllPanesToCurrentConflict();
        }
    }

    private void ScrollAllPanesToCurrentConflict()
    {
        if (Vm?.Document is null) return;
        var conflicting = Vm.Document.ConflictingRanges.ToList();
        if (conflicting.Count == 0) return;
        var idx = Math.Clamp(Vm.CurrentConflictIndex, 0, conflicting.Count - 1);
        var range = conflicting[idx];
        // For one-sided conflicts (e.g. theirs added lines that ours didn't
        // touch) the empty side has StartLine=0, which would scroll that pane
        // to negative offset → effectively to 0 → the mirror sync in
        // OnOursScrollChanged / OnTheirsScrollChanged would then yank the
        // OTHER pane back to 0 too, hiding the sticky banner. Skip the empty
        // side; the mirror handler will sync it from the non-empty side's
        // post-scroll offset.
        if (!range.Ours.IsEmpty)
            ScrollPaneToLine(OursScrollViewer, range.Ours.StartLine);
        if (!range.Theirs.IsEmpty)
            ScrollPaneToLine(TheirsScrollViewer, range.Theirs.StartLine);
        // Result pane uses AvalonEdit's own scroll API; ScrollToLine wraps it.
        // ResultMarkedRange always has at least the marker lines so it is
        // never empty for a real conflicting range.
        if (!range.ResultMarkedRange.IsEmpty)
            ResultPaneInstance?.ScrollToLine(range.ResultMarkedRange.StartLine);
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
        // — this is the designated re-render channel. Every control whose
        // rendering depends on RangeStates needs an explicit refresh
        // because dictionary mutation in place doesn't fire WPF DP change
        // notifications. If you add a new RangeStates-consuming control,
        // add a Refresh() entry point on it and call it here.
        OursPane.InvalidateVisual();
        TheirsPane.InvalidateVisual();
        OursSticky?.RefreshState();
        TheirsSticky?.RefreshState();
        ResultSticky?.RefreshState();
        OursOverviewRuler?.Refresh();
        TheirsOverviewRuler?.Refresh();
        OursMinimapPreview?.Refresh();
        TheirsMinimapPreview?.Refresh();
        ConnectionCanvas?.Refresh();
        // ResultPane's BackgroundRenderer reads the live RangeStates dict;
        // an in-place mutation doesn't fire its DP callback, so explicitly
        // invalidate the background layer to repaint resolved-overlay tints.
        ResultPaneInstance?.RefreshResolvedTints();
        // The previous floating CodeLensActionBar + SegmentedAcceptPillOverlay
        // were removed when ConflictMarkerInlineGenerator landed; their
        // Refresh hooks are no longer needed because the inline generator
        // re-runs on every TextView.Redraw() (which the ResultPane DPs
        // trigger via OnGeneratorInputChanged).
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

    // Shared by the V6 ConflictOverviewRuler (12 px tick strip) and
    // ConflictMinimapPreview (60–80 px text preview). Both raise
    // MinimapJumpEventArgs with a 1-based target line; the handler scrolls
    // the paired pane's ScrollViewer.
    private void OnOursScrollJumpRequested(object? sender, MinimapJumpEventArgs e)
    {
        ScrollPaneToLine(OursScrollViewer, e.LineNumber);
    }

    private void OnTheirsScrollJumpRequested(object? sender, MinimapJumpEventArgs e)
    {
        ScrollPaneToLine(TheirsScrollViewer, e.LineNumber);
    }

    private void ScrollPaneToLine(System.Windows.Controls.ScrollViewer sv, int lineNumber1Based)
    {
        var layout = Vm?.Layout;
        if (layout is null || sv is null) return;
        // Defensive: callers should already filter empty ranges, but a 0 or
        // negative line number falls through layout.GetVisualTop to a
        // negative Y and the mirror-scroll sync would then yank both panes
        // to offset 0 (banner disappears). Bail rather than no-op-scroll.
        if (lineNumber1Based < 1) return;
        var y = layout.GetVisualTop(lineNumber1Based);
        // Center the target line in the viewport when possible.
        var target = Math.Max(0, y - sv.ViewportHeight / 2);
        // V5: smooth animated scroll via Merge.Motion.MinimapJump (400 ms
        // ease-out) — replaces the previous instant jump so the user can
        // track which direction the pane moved.
        Leaf.Controls.Merge.MergeMotionHelpers.SmoothScrollTo(sv, target);
    }

}
