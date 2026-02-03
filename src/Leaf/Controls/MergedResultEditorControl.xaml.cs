using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Controls;

public partial class MergedResultEditorControl : UserControl
{
    private ConflictResolutionViewModel? _viewModel;
    private bool _isUpdatingFromViewModel;
    private readonly HashSet<MergedLine> _trackedLines = [];
    private int _hoverLine = -1;
    private bool _invalidatePending;

    public MergedResultEditorControl()
    {
        InitializeComponent();

        Editor.TextArea.TextView.Options.EnableVirtualSpace = false;
        Editor.TextArea.TextView.Options.AllowScrollBelowDocument = false;
        Editor.Loaded += OnEditorLoaded;
        BackgroundLayer.AttachEditor(Editor);
        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.MouseMove += OnEditorMouseMove;
        Editor.TextArea.MouseLeave += OnEditorMouseLeave;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.MergedLines.CollectionChanged -= OnMergedLinesChanged;
            foreach (var line in _trackedLines)
            {
                line.PropertyChanged -= OnMergedLinePropertyChanged;
            }
            _trackedLines.Clear();
        }

        _viewModel = e.NewValue as ConflictResolutionViewModel;
        if (_viewModel == null)
        {
            Editor.Text = string.Empty;
            BackgroundLayer.SetLines(null);
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.MergedLines.CollectionChanged += OnMergedLinesChanged;
        SyncFromViewModel();
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureLineNumberMargin();
    }

    private void ConfigureLineNumberMargin()
    {
        var lineNumberMargin = Editor.TextArea.LeftMargins.OfType<LineNumberMargin>().FirstOrDefault();
        if (lineNumberMargin == null)
            return;

        lineNumberMargin.Width = 36;
        lineNumberMargin.Margin = new Thickness(0, 0, 6, 0);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConflictResolutionViewModel.MergedContent))
        {
            SyncFromViewModel();
        }
    }

    private void OnMergedLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        BackgroundLayer.SetLines(_viewModel?.MergedLines);
        TrackLineChanges();
        ScheduleInvalidateVisual();
    }

    /// <summary>
    /// Coalesce multiple InvalidateVisual() calls into a single call via Dispatcher.
    /// </summary>
    private void ScheduleInvalidateVisual()
    {
        if (_invalidatePending)
            return;

        _invalidatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _invalidatePending = false;
            BackgroundLayer.InvalidateVisual();
        });
    }

    private void SyncFromViewModel()
    {
        if (_viewModel == null)
            return;

        _isUpdatingFromViewModel = true;
        try
        {
            Editor.Text = _viewModel.MergedContent ?? string.Empty;
            BackgroundLayer.SetLines(_viewModel.MergedLines);
            TrackLineChanges();
            ScheduleInvalidateVisual();
        }
        finally
        {
            _isUpdatingFromViewModel = false;
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingFromViewModel || _viewModel == null)
            return;

        _viewModel.UpdateMergedLinesFromText(Editor.Text);
        BackgroundLayer.SetLines(_viewModel.MergedLines);
        TrackLineChanges();
        ScheduleInvalidateVisual();
    }

    private void OnEditorMouseMove(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_viewModel == null)
            return;

        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        var line = position?.Line ?? -1;
        if (line != _hoverLine)
        {
            _hoverLine = line;
            BackgroundLayer.SetHoverLine(_hoverLine);
            BackgroundLayer.InvalidateVisual();
        }
    }

    private void OnEditorMouseLeave(object? sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_hoverLine == -1)
            return;

        _hoverLine = -1;
        BackgroundLayer.SetHoverLine(_hoverLine);
        BackgroundLayer.InvalidateVisual();
    }

    private void TrackLineChanges()
    {
        if (_viewModel == null)
            return;

        foreach (var line in _viewModel.MergedLines)
        {
            if (_trackedLines.Add(line))
            {
                line.PropertyChanged += OnMergedLinePropertyChanged;
            }
        }
    }

    private void OnMergedLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MergedLine.Source))
        {
            ScheduleInvalidateVisual();
        }
    }
}

internal sealed class MergedResultBackground : FrameworkElement
{
    private TextEditor? _editor;
    private IReadOnlyList<MergedLine>? _lines;
    private int _hoverLine;
    private bool _isAttached;
    private bool _isRendering;
    private static int _renderCount;

    public MergedResultBackground()
    {
    }

    public void SetLines(IReadOnlyList<MergedLine>? lines)
    {
        _lines = lines;
    }

    public void SetHoverLine(int hoverLine)
    {
        _hoverLine = hoverLine;
    }

    public void AttachEditor(TextEditor editor)
    {
        if (_isAttached)
            return;

        _isAttached = true;
        _editor = editor;
        var textView = _editor.TextArea.TextView;
        textView.VisualLinesChanged += (_, __) =>
        {
            if (!_isRendering)
            {
                InvalidateVisual();
            }
        };
        textView.ScrollOffsetChanged += (_, __) =>
        {
            if (!_isRendering)
            {
                InvalidateVisual();
            }
        };
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        _renderCount++;
        if (_renderCount % 100 == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[MergedResultBackground] OnRender called {_renderCount} times");
        }

        if (_lines == null || _editor == null)
            return;

        // Guard against render loops
        if (_isRendering)
        {
            System.Diagnostics.Debug.WriteLine($"[MergedResultBackground] Prevented re-entrant render!");
            return;
        }

        _isRendering = true;
        try
        {
            var textView = _editor.TextArea.TextView;

            // Don't call EnsureVisualLines() as it can trigger VisualLinesChanged event
            // which causes a render loop. Just check if lines are already valid.
            if (!textView.VisualLinesValid)
                return;

            if (!IsLoaded || !textView.IsLoaded)
                return;

            var origin = textView.TransformToVisual(this).Transform(new Point(0, 0));
            var width = ActualWidth;

            foreach (var visualLine in textView.VisualLines)
            {
                var lineNumber = visualLine.FirstDocumentLine.LineNumber;
                if (lineNumber < 1 || lineNumber > _lines.Count)
                    continue;

                var line = _lines[lineNumber - 1];
                var brush = GetBrush(line.Source, lineNumber == _hoverLine);
                if (brush == null)
                    continue;

                var y = origin.Y + visualLine.VisualTop - textView.VerticalOffset;
                var rect = new Rect(0, y, width, visualLine.Height);
                drawingContext.DrawRectangle(brush, null, rect);
            }
        }
        finally
        {
            _isRendering = false;
        }
    }

    // Cached brushes to avoid creating new ones every render call
    private static readonly Brush OursBrush = CreateFrozenBrush(Color.FromArgb(0xB3, 0x1E, 0x3A, 0x5F));
    private static readonly Brush TheirsBrush = CreateFrozenBrush(Color.FromArgb(0xB3, 0x14, 0x53, 0x2D));
    private static readonly Brush ManualBrush = CreateFrozenBrush(Color.FromArgb(0xB3, 0x6D, 0x28, 0xD9));
    private static readonly Brush OursHoverBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0x1E, 0x3A, 0x5F));
    private static readonly Brush TheirsHoverBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0x14, 0x53, 0x2D));
    private static readonly Brush ManualHoverBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0x6D, 0x28, 0xD9));
    private static readonly Brush DefaultHoverBrush = CreateFrozenBrush(Color.FromArgb(0x80, 0xB4, 0x53, 0x09));

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush? GetBrush(MergedLineSource source, bool isHover)
    {
        if (isHover)
        {
            return source switch
            {
                MergedLineSource.Ours => OursHoverBrush,
                MergedLineSource.Theirs => TheirsHoverBrush,
                MergedLineSource.Manual => ManualHoverBrush,
                _ => DefaultHoverBrush
            };
        }

        return source switch
        {
            MergedLineSource.Ours => OursBrush,
            MergedLineSource.Theirs => TheirsBrush,
            MergedLineSource.Manual => ManualBrush,
            _ => null
        };
    }
}
