#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.TextEdit;
using Leaf.TextEdit.Document;

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
/// text, plus a <see cref="ResultTextChanged"/> event that fires on edits so the VM
/// can flip affected regions to <c>ResolutionState.Manual</c>. No conflict chrome
/// is drawn here — overlays live in the parent <see cref="MergeEditorView"/> and
/// share the Result pane's scroll viewport.
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

    /// <summary>Fires when the user edits the result pane. The string is the full buffer.</summary>
    public event EventHandler<string>? ResultTextChanged;

    private readonly TextEditor _editor = new()
    {
        ShowLineNumbers = true,
        IsReadOnly = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    public ResultPane()
    {
        Content = _editor;
        _editor.TextChanged += OnEditorTextChanged;
    }

    private bool _suppressChangeEvent;

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressChangeEvent) return;
        var txt = _editor.Text;
        if (!Equals(GetValue(TextProperty), txt))
        {
            SetCurrentValue(TextProperty, txt);
        }
        ResultTextChanged?.Invoke(this, txt);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pane = (ResultPane)d;
        var newText = (string?)e.NewValue ?? string.Empty;
        if (pane._editor.Text == newText) return;
        pane._suppressChangeEvent = true;
        try
        {
            pane._editor.Document ??= new TextDocument();
            pane._editor.Document.Text = newText;
        }
        finally { pane._suppressChangeEvent = false; }
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pane = (ResultPane)d;
        if (e.NewValue is MergePaneGlyphLayout layout)
        {
            pane.ApplyLayout(layout);
            layout.PropertyChanged += (_, _) => pane.ApplyLayout(layout);
        }
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
}
