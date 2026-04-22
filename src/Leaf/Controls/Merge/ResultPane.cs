#nullable enable
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.TextEdit;
using Leaf.TextEdit.Document;
using Leaf.TextEdit.Highlighting;

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
/// text (one-way today — the pane is <c>IsReadOnly=true</c>). No conflict chrome
/// is drawn here — overlays live in the parent <see cref="MergeEditorView"/> and
/// share the Result pane's scroll viewport.
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

    private readonly TextEditor _editor = new()
    {
        ShowLineNumbers = true,
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
    }

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
        pane._editor.SyntaxHighlighting = MergeHighlightingResolver.ByFilePath((string?)e.NewValue);
    }
}
