using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using Leaf.Helpers;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Controls.Merge;

public partial class ConflictSideEditorControl : UserControl
{
    private ConflictSideLineMapping? _mapping;
    private ConflictBackgroundRenderer? _renderer;
    private ConflictCheckboxMargin? _checkboxMargin;
    private ConflictLineNumberMargin? _lineNumberMargin;
    private ConflictRegionOverlay? _overlay;
    private ConflictResolutionViewModel? _viewModel;
    private readonly HashSet<SelectableLine> _subscribedSelectableLines = [];
    private readonly HashSet<MergeRegion> _subscribedRegions = [];
    private IHighlightingDefinition? _lastHighlighting;
    private int _hoverLine = -1;
    private ScrollViewer? _cachedScrollViewer;

    public event EventHandler<double>? ScrollOffsetChanged;

    public static readonly DependencyProperty SideProperty =
        DependencyProperty.Register(nameof(Side), typeof(ConflictSide), typeof(ConflictSideEditorControl),
            new PropertyMetadata(ConflictSide.Ours));

    public ConflictSide Side
    {
        get => (ConflictSide)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    public ConflictSideEditorControl()
    {
        InitializeComponent();

        Editor.TextArea.TextView.Options.EnableVirtualSpace = false;
        Editor.TextArea.TextView.Options.AllowScrollBelowDocument = false;
        Editor.TextArea.SelectionCornerRadius = 0;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(SyntaxHighlightingHelper.KeywordColor);
        Editor.TextArea.TextView.LinkTextUnderline = false;

        // Disable caret and text editing visuals for read-only
        Editor.TextArea.Caret.CaretBrush = Brushes.Transparent;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Attach scroll events
        Editor.TextArea.TextView.ScrollOffsetChanged += OnScrollOffsetChanged;
        Editor.TextArea.TextView.VisualLinesChanged += OnVisualLinesChanged;
        Editor.TextArea.MouseMove += OnEditorMouseMove;
        Editor.TextArea.MouseLeave += OnEditorMouseLeave;

        // Line number margin is custom (ConflictLineNumberMargin), configured in SetContent
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
    }

    /// <summary>
    /// Sets up the editor with content, syntax highlighting, and line mapping.
    /// Called by the view when the ViewModel provides new merge data.
    /// </summary>
    public void SetContent(string content, string filePath, ConflictSideLineMapping mapping,
        ConflictResolutionViewModel viewModel)
    {
        Detach();

        _mapping = mapping;
        _viewModel = viewModel;

        // Apply syntax highlighting
        var highlighting = HighlightingManager.Instance.GetDefinitionByExtension(
            System.IO.Path.GetExtension(filePath));
        if (highlighting != null && !ReferenceEquals(highlighting, _lastHighlighting))
        {
            SyntaxHighlightingHelper.ApplyDarkThemeColors(highlighting);
            _lastHighlighting = highlighting;
        }
        Editor.SyntaxHighlighting = highlighting;

        // Set text
        Editor.Text = content;

        // Set up background renderer
        _renderer = new ConflictBackgroundRenderer();
        _renderer.Configure(_mapping, Side);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);

        // Set up custom line number margin (replaces built-in, skips header lines)
        _lineNumberMargin = new ConflictLineNumberMargin();
        _lineNumberMargin.Configure(_mapping);
        Editor.TextArea.LeftMargins.Insert(0, _lineNumberMargin);

        // Set up checkbox margin — insert after the line number margin
        _checkboxMargin = new ConflictCheckboxMargin();
        _checkboxMargin.Configure(_mapping, Side);
        Editor.TextArea.LeftMargins.Insert(1, _checkboxMargin);

        // Set up overlay
        _overlay = new ConflictRegionOverlay(OverlayCanvas, Editor);
        _overlay.Configure(_mapping, _viewModel);

        // Subscribe to SelectableLine changes
        SubscribeToLineEvents();

        // Subscribe to region resolution changes
        SubscribeToRegionEvents();

        // Initial layout
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _overlay?.ScheduleReposition();
        });
    }

    public void ClearContent()
    {
        Detach();
        Editor.Text = string.Empty;
        Editor.SyntaxHighlighting = null;
    }

    public void ScrollToRegion(int regionIndex)
    {
        if (_mapping == null) return;

        var range = _mapping.GetConflictRange(regionIndex);
        if (range == null) return;

        ScrollToLine(range.StartLine);
    }

    public void ScrollToLine(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > Editor.Document.LineCount) return;

        // Center the line in the viewport
        var textView = Editor.TextArea.TextView;
        var docLine = Editor.Document.GetLineByNumber(lineNumber);
        var visualTop = textView.GetVisualTopByDocumentLine(docLine.LineNumber);
        var viewportHeight = textView.ActualHeight;
        var targetOffset = Math.Max(0, visualTop - viewportHeight / 3);

        Editor.ScrollToVerticalOffset(targetOffset);
    }

    /// <summary>
    /// Returns scroll ratio (0..1) for syncing with editors of different content length.
    /// </summary>
    public double GetScrollRatio()
    {
        var sv = _cachedScrollViewer ??= FindScrollViewer(Editor);
        if (sv == null) return 0;
        var max = sv.ExtentHeight - sv.ViewportHeight;
        return max > 0 ? sv.VerticalOffset / max : 0;
    }

    public void ApplyScrollRatio(double ratio)
    {
        var sv = _cachedScrollViewer ??= FindScrollViewer(Editor);
        if (sv == null) return;
        var max = sv.ExtentHeight - sv.ViewportHeight;
        if (max > 0)
            Editor.ScrollToVerticalOffset(ratio * max);
    }

    public void ApplyScrollOffset(double offset)
    {
        if (Math.Abs(Editor.VerticalOffset - offset) < 0.5) return;
        Editor.ScrollToVerticalOffset(offset);
    }

    private void OnScrollOffsetChanged(object? sender, EventArgs e)
    {
        _overlay?.ScheduleReposition();
        ScrollOffsetChanged?.Invoke(this, Editor.VerticalOffset);
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e)
    {
        _lineNumberMargin?.InvalidateVisual();
        _checkboxMargin?.InvalidateVisual();
        _overlay?.ScheduleReposition();
    }

    private void OnEditorMouseMove(object? sender, MouseEventArgs e)
    {
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        var line = position?.Line ?? -1;
        if (line != _hoverLine)
        {
            _hoverLine = line;
            _renderer?.SetHoverLine(_hoverLine);
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    private void OnEditorMouseLeave(object? sender, MouseEventArgs e)
    {
        if (_hoverLine != -1)
        {
            _hoverLine = -1;
            _renderer?.SetHoverLine(-1);
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    private void SubscribeToLineEvents()
    {
        if (_mapping == null) return;

        foreach (var range in _mapping.AllConflictRanges)
        {
            for (int line = range.StartLine; line <= range.EndLine; line++)
            {
                var selectable = _mapping.GetSelectableLineForLine(line);
                if (selectable != null && _subscribedSelectableLines.Add(selectable))
                {
                    selectable.PropertyChanged += OnSelectableLineChanged;
                }
            }
        }
    }

    private void SubscribeToRegionEvents()
    {
        if (_mapping == null) return;

        foreach (var range in _mapping.AllConflictRanges)
        {
            if (_subscribedRegions.Add(range.Region))
            {
                range.Region.PropertyChanged += OnRegionPropertyChanged;
            }
        }
    }

    private void OnSelectableLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableLine.IsSelected))
        {
            // Invalidate background and checkbox margin
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
                _checkboxMargin?.InvalidateVisual();
            });
        }
    }

    private void OnRegionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergeRegion.Resolution) || e.PropertyName == nameof(MergeRegion.IsResolved))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
                _checkboxMargin?.InvalidateVisual();
                _overlay?.UpdateBarStates();
                _overlay?.ScheduleReposition();
            });
        }
    }

    private void Detach()
    {
        // Unsubscribe SelectableLine events
        foreach (var line in _subscribedSelectableLines)
            line.PropertyChanged -= OnSelectableLineChanged;
        _subscribedSelectableLines.Clear();

        // Unsubscribe region events
        foreach (var region in _subscribedRegions)
            region.PropertyChanged -= OnRegionPropertyChanged;
        _subscribedRegions.Clear();

        // Remove background renderer
        if (_renderer != null)
        {
            Editor.TextArea.TextView.BackgroundRenderers.Remove(_renderer);
            _renderer = null;
        }

        // Remove custom line number margin
        if (_lineNumberMargin != null)
        {
            Editor.TextArea.LeftMargins.Remove(_lineNumberMargin);
            _lineNumberMargin = null;
        }

        // Remove checkbox margin
        if (_checkboxMargin != null)
        {
            Editor.TextArea.LeftMargins.Remove(_checkboxMargin);
            _checkboxMargin = null;
        }

        // Clear overlay
        _overlay?.Clear();
        _overlay = null;

        _mapping = null;
        _viewModel = null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root == null) return null;
        if (root is ScrollViewer viewer) return viewer;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }
}
