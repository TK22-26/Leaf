#nullable enable
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Helpers;
using Leaf.Models.Merge;
using Leaf.TextEdit;
using Leaf.TextEdit.Document;
using Leaf.TextEdit.Highlighting;
using Leaf.TextEdit.Rendering;

namespace Leaf.Controls.Merge;

/// <summary>
/// Editable result pane for the merge editor. Thin wrapper around the vendored
/// <see cref="TextEditor"/> that binds its <see cref="Leaf.TextEdit.Rendering.TextView"/>
/// font metrics to the shared <see cref="MergePaneGlyphLayout"/> so the pane
/// aligns pixel-perfectly with the custom <see cref="ReadOnlyMergePane"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// We expose a <see cref="Text"/> dependency property bound to the VM's composed
/// text. The DP is registered with <c>BindsTwoWayByDefault</c> so Phase 3's
/// editable-result mode can wire the reverse path through this same DP without
/// re-registering; today the pane is <c>IsReadOnly=true</c> so the reverse path
/// is unused. No conflict chrome is drawn here — overlays live in the parent
/// <see cref="MergeEditorView"/> and share the Result pane's scroll viewport.
/// </para>
/// <para>
/// When Phase 3 re-enables manual editing with range-aware text mapping, the
/// pane will surface user edits back to the VM so affected regions flip to
/// <see cref="Leaf.Models.Merge.ResolutionState.Manual"/>. No such surface
/// exists today — adding it without a consumer would be dead scaffolding.
/// </para>
/// </remarks>
public sealed class ResultPane : ContentControl
{
    static ResultPane()
    {
        // AvalonEdit's TextEditor does not inherit Foreground / Background from
        // its outer ContentControl — its default style paints text in the WPF
        // fallback colour (black on dark = invisible). Override metadata here
        // so a Foreground / Background DP assignment on ResultPane forwards
        // into _editor. Matches how ReadOnlyMergePane exposes a Foreground DP
        // and consumes it in the custom draw path.
        ForegroundProperty.OverrideMetadata(typeof(ResultPane),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnForegroundChanged));
        BackgroundProperty.OverrideMetadata(typeof(ResultPane),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.None, OnBackgroundChanged));
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ResultPane),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MergePaneGlyphLayout), typeof(ResultPane),
        new FrameworkPropertyMetadata(null, OnLayoutChanged));

    /// <summary>
    /// File path of the conflict being rendered. C1 uses this to resolve an
    /// <see cref="IHighlightingDefinition"/> by extension through AvalonEdit's
    /// <see cref="HighlightingManager.Instance"/> so the result pane gets
    /// syntax-coloured code. Null or an unknown extension disables
    /// highlighting (plain foreground colour).
    /// </summary>
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(ResultPane),
        new FrameworkPropertyMetadata(null, OnFilePathChanged));

    /// <summary>
    /// Mirrors the inner <see cref="TextEditor"/>'s <c>TextArea.TextView.ScrollOffset.Y</c>.
    /// The CodeLens overlay (C1) binds this so its per-conflict bars track
    /// the result pane's scroll position. Read-only in practice — the pane
    /// publishes the value; consumers don't write back.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.Register(
        nameof(VerticalOffset), typeof(double), typeof(ResultPane),
        new PropertyMetadata(0.0));

    /// <summary>
    /// Reference to the active <see cref="MergeDocument"/>. The inline
    /// CodeLens generator uses this to map marker line numbers to range
    /// indices so each <em>Accept Ours / Theirs / Both / Compare</em>
    /// click carries the right CommandParameter. Bound from MergeEditorView
    /// to the VM's <c>Document</c> property.
    /// </summary>
    public static readonly DependencyProperty MergeDocumentProperty = DependencyProperty.Register(
        nameof(MergeDocument), typeof(MergeDocument), typeof(ResultPane),
        new PropertyMetadata(null, OnGeneratorInputChanged));

    /// <summary>
    /// Live <see cref="ResolutionState"/> dictionary. The background renderer
    /// reads this to paint resolved conflicts with the resolved-overlay tint.
    /// Bound from <c>MergeEditorView.xaml</c> to the VM's <c>RangeStates</c>
    /// property; mutated in-place for accept-side clicks, so consumers should
    /// call <see cref="RefreshResolvedTints"/> after a state change to repaint.
    /// </summary>
    public static readonly DependencyProperty RangeStatesProperty = DependencyProperty.Register(
        nameof(RangeStates), typeof(IReadOnlyDictionary<int, ResolutionState>), typeof(ResultPane),
        new PropertyMetadata(null, OnGeneratorInputChanged));

    public static readonly DependencyProperty AcceptOursCommandProperty = DependencyProperty.Register(
        nameof(AcceptOursCommand), typeof(ICommand), typeof(ResultPane));

    public static readonly DependencyProperty AcceptTheirsCommandProperty = DependencyProperty.Register(
        nameof(AcceptTheirsCommand), typeof(ICommand), typeof(ResultPane));

    public static readonly DependencyProperty AcceptBothCommandProperty = DependencyProperty.Register(
        nameof(AcceptBothCommand), typeof(ICommand), typeof(ResultPane));

    public static readonly DependencyProperty CompareCommandProperty = DependencyProperty.Register(
        nameof(CompareCommand), typeof(ICommand), typeof(ResultPane));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MergePaneGlyphLayout? Layout
    {
        get => (MergePaneGlyphLayout?)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        private set => SetValue(VerticalOffsetProperty, value);
    }

    public MergeDocument? MergeDocument
    {
        get => (MergeDocument?)GetValue(MergeDocumentProperty);
        set => SetValue(MergeDocumentProperty, value);
    }

    public IReadOnlyDictionary<int, ResolutionState>? RangeStates
    {
        get => (IReadOnlyDictionary<int, ResolutionState>?)GetValue(RangeStatesProperty);
        set => SetValue(RangeStatesProperty, value);
    }

    /// <summary>
    /// Force a repaint of the background renderer so resolved-conflict
    /// tints update after an in-place mutation of the RangeStates
    /// dictionary. The host <c>MergeEditorView</c> calls this from its
    /// existing <c>RangeStatesChanged</c> hook alongside the other
    /// in-place-state refresh entry points.
    /// </summary>
    public void RefreshResolvedTints()
    {
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        // RangeStates is mutated in place on accept-side clicks, so the
        // line-number map needs the same explicit kick — it tracks resolved
        // vs unresolved per-conflict to decide which side's numbers to draw.
        _lineNumberMargin.Refresh();
        // Re-run element generators so the inline toolbar's
        // FindRangeIndexForLine pass walks the FRESH document text and
        // re-pairs marker rows to the correct conflict indices. Without
        // this, after the user accepts conflict N the toolbars on every
        // unresolved conflict below shift one slot and fire commands
        // against the wrong range — the "everything below jumbles" bug.
        _editor.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Scroll the result pane so <paramref name="lineNumber1Based"/> is at
    /// the top of the viewport. Mirrors <c>ScrollPaneToLine</c> for the
    /// ReadOnly panes — used by the F8 / chevron / Alt-arrow conflict
    /// navigators so all three panes track together when the user
    /// jumps between conflicts.
    /// </summary>
    public void ScrollToLine(int lineNumber1Based)
    {
        if (lineNumber1Based < 1) return;
        if (_editor.Document is null) return;
        if (lineNumber1Based > _editor.Document.LineCount) return;
        _editor.ScrollToLine(lineNumber1Based);
    }

    public ICommand? AcceptOursCommand
    {
        get => (ICommand?)GetValue(AcceptOursCommandProperty);
        set => SetValue(AcceptOursCommandProperty, value);
    }

    public ICommand? AcceptTheirsCommand
    {
        get => (ICommand?)GetValue(AcceptTheirsCommandProperty);
        set => SetValue(AcceptTheirsCommandProperty, value);
    }

    public ICommand? AcceptBothCommand
    {
        get => (ICommand?)GetValue(AcceptBothCommandProperty);
        set => SetValue(AcceptBothCommandProperty, value);
    }

    public ICommand? CompareCommand
    {
        get => (ICommand?)GetValue(CompareCommandProperty);
        set => SetValue(CompareCommandProperty, value);
    }

    private readonly TextEditor _editor = new()
    {
        // The custom MergeResultLineNumberMargin replaces the stock gutter so
        // marker lines (toolbar / BASE / THEIRS / END rows) get NO number and
        // in-conflict content shows file-side-specific numbers (ours / base /
        // theirs StartLine + offset). ShowLineNumbers=true would add a second
        // stock margin alongside ours.
        ShowLineNumbers = false,
        // Phase 2c ships the Result pane as read-only: manual editing requires
        // per-range text mapping (Phase 3) to know which range the user's edit
        // falls inside. Without that, whole-buffer edits destroyed both the
        // caret state and the conflict-marker commit gate. The composed text
        // is still fully driven by the VM's RangeStates → accept-ours /
        // accept-theirs / accept-both resolution. Will flip back to editable
        // once Phase 3 ships the range-aware text-change handler.
        IsReadOnly = true,
        // Phase 4: keep the pane non-focusable so its vendored TextArea's
        // CommandBindings (Undo/Redo/Cut/Paste) don't swallow window-level
        // keyboard shortcuts. With IsReadOnly=true there's nothing the user
        // could do here that requires focus; Ctrl+C for selection copy works
        // without focus via the vendored ApplicationCommands routing.
        Focusable = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    public ResultPane()
    {
        Content = _editor;
        // Forward the TextView's scroll offset to VerticalOffset so the
        // CodeLens overlay (and any future chrome) can track pane scrolling
        // declaratively via WPF bindings rather than hooking the event itself.
        _editor.TextArea.TextView.ScrollOffsetChanged += (_, _) =>
            VerticalOffset = _editor.TextArea.TextView.ScrollOffset.Y;

        // Phantom-line CodeLens: replace each conflict-marker line in the
        // rendered text with an inline UI affordance. Closures pull the
        // current values of the dependency properties at generation time
        // so the editor doesn't have to be rebuilt when commands or the
        // document change — AvalonEdit re-runs the generator on every
        // visual-line construction (i.e. scroll, layout invalidation).
        _editor.TextArea.TextView.ElementGenerators.Add(
            new ConflictMarkerInlineGenerator(
                () => MergeDocument,
                () => RangeStates,
                () => AcceptOursCommand,
                () => AcceptTheirsCommand,
                () => AcceptBothCommand,
                () => CompareCommand));

        // Side-tinted backgrounds: ours blue / theirs green / base grey for
        // unresolved conflicts; resolved-overlay green for resolved ones.
        // Painted under the text by AvalonEdit's BackgroundRenderers layer
        // so syntax-highlighted glyphs read on top of the tint.
        _editor.TextArea.TextView.BackgroundRenderers.Add(
            new ResultPaneBackgroundRenderer(() => MergeDocument, () => RangeStates));

        // Custom gutter that skips marker lines and renders file-side line
        // numbers inside conflicts. Stored so OnGeneratorInputChanged can
        // poke it to rebuild after a state-dictionary mutation (which doesn't
        // change document text and so wouldn't fire TextChanged).
        _lineNumberMargin = new MergeResultLineNumberMargin(
            () => MergeDocument,
            () => RangeStates);
        _editor.TextArea.LeftMargins.Add(_lineNumberMargin);

        // Detach from the bound Layout when the pane leaves the visual tree.
        // Layout instances are reused across pane re-binds, so without this
        // teardown the pane keeps a strong reference back from
        // Layout.PropertyChanged and accumulates one ResultPane per merge-
        // editor open over a long Leaf session.
        Unloaded += OnPaneUnloaded;
    }

    private void OnPaneUnloaded(object sender, RoutedEventArgs e)
    {
        if (Layout is { } layout)
        {
            layout.PropertyChanged -= OnLayoutPropertyChanged;
        }
    }

    private readonly MergeResultLineNumberMargin _lineNumberMargin;

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // One-way push from the Text DP into the AvalonEdit document. Pane is
        // IsReadOnly=true so there's no reverse path to guard against — user
        // typing can't fire TextChanged on _editor. Phase 3 (editable Result)
        // will re-add a TextChanged subscription when it ships the range-aware
        // ResolutionState.Manual routing.
        var pane = (ResultPane)d;
        var newText = (string?)e.NewValue ?? string.Empty;
        if (pane._editor.Text == newText) return;
        pane._editor.Document ??= new TextDocument();
        pane._editor.Document.Text = newText;
        // The Text DP fires when the VM's ComposedText recomputes — which
        // happens after a RangeStates mutation. Both the line-number margin
        // and the BackgroundRenderer derive from RangeStates AND the new
        // doc text, but neither hears about in-place dictionary mutations.
        // Kick both here so they rebuild against the fresh doc + state in
        // a single synchronous step, ordered AFTER Document.Text has been
        // assigned. RefreshResolvedTints (called from the VM's
        // RangeStatesChanged event) may run before this binding propagation
        // completes, so without the kick here the BG layer is left painting
        // its pre-acceptance tint map.
        pane._lineNumberMargin?.Refresh();
        pane._editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Mirror ReadOnlyMergePane.OnLayoutChanged: detach from the old Layout
        // before attaching to the new one. Without the detach, a Layout DP
        // re-assignment (e.g. DataContext swap or light-theme propagation)
        // would accumulate subscribers on the previous Layout instance,
        // double-invoking ApplyLayout and eventually leaking.
        var pane = (ResultPane)d;
        if (e.OldValue is MergePaneGlyphLayout oldLayout)
            oldLayout.PropertyChanged -= pane.OnLayoutPropertyChanged;
        if (e.NewValue is MergePaneGlyphLayout newLayout)
        {
            pane.ApplyLayout(newLayout);
            newLayout.PropertyChanged += pane.OnLayoutPropertyChanged;
        }
    }

    private void OnLayoutPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Layout is { } layout) ApplyLayout(layout);
    }

    private void ApplyLayout(MergePaneGlyphLayout layout)
    {
        _editor.FontFamily = layout.FontFamily;
        _editor.FontSize = layout.FontSize;
        _editor.FontWeight = layout.FontWeight;
        _editor.FontStyle = layout.FontStyle;
        _editor.FontStretch = layout.FontStretch;
        _editor.Options.IndentationSize = layout.TabSize;
    }

    private static void OnFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pane = (ResultPane)d;
        var definition = MergeHighlightingResolver.ByFilePath((string?)e.NewValue);
        if (definition is not null)
        {
            // Same dark-theme remap the ReadOnlyMergePane + DiffViewerControl
            // apply. Without this, AvalonEdit's stock .xshd colours include
            // dark blues that read as invisible on the dark pane surface.
            SyntaxHighlightingHelper.ApplyDarkThemeColors(definition);
        }
        pane._editor.SyntaxHighlighting = definition;
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ResultPane)d)._editor.Foreground = (Brush?)e.NewValue ?? Brushes.Transparent;
    }

    private static void OnBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ResultPane)d)._editor.Background = (Brush?)e.NewValue ?? Brushes.Transparent;
    }

    /// <summary>
    /// Force the editor to re-run its element generators when the inputs the
    /// inline CodeLens depends on change (most importantly: a new conflict
    /// document arrives). Without this the generator's cached visual lines
    /// would keep pointing at the previous document's range list and either
    /// throw on stale lookups or render the wrong toolbar at each marker.
    /// </summary>
    private static void OnGeneratorInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pane = (ResultPane)d;
        // Re-run element generators so the inline CodeLens picks up the new
        // ranges, and rebuild the line-number map so file-side numbers
        // re-anchor to the new document's range list.
        pane._lineNumberMargin.Refresh();
        pane._editor.TextArea.TextView.Redraw();
    }
}
