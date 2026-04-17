using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluentIcons.Common;
using FluentIcons.Wpf;
using Leaf.TextEdit;
using Leaf.TextEdit.Rendering;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Controls.Merge;

/// <summary>
/// Manages floating per-conflict button bars on a Canvas overlay.
/// Positions bars relative to the text view's visible conflict regions.
/// </summary>
public sealed class ConflictRegionOverlay
{
    private readonly Canvas _canvas;
    private readonly TextEditor _editor;
    private ConflictSideLineMapping? _mapping;
    private ConflictResolutionViewModel? _viewModel;
    private readonly Dictionary<int, Border> _regionBars = new();
    private bool _repositionPending;

    // Bar background brushes - frozen
    private static readonly Brush ResolvedBg = CreateFrozen(Color.FromArgb(0xAA, 0x22, 0xC5, 0x5E));
    private static readonly Brush UnresolvedBg = CreateFrozen(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A));
    private static readonly Brush WarningColor = CreateFrozen(Color.FromRgb(0xE5, 0xA2, 0x20));
    private static readonly Brush ResolvedColor = CreateFrozen(Color.FromRgb(0x22, 0xC5, 0x5E));

    public ConflictRegionOverlay(Canvas canvas, TextEditor editor)
    {
        _canvas = canvas;
        _editor = editor;
        _canvas.IsHitTestVisible = false; // Canvas itself is transparent to hits
    }

    public void Configure(ConflictSideLineMapping? mapping, ConflictResolutionViewModel? viewModel)
    {
        _mapping = mapping;
        _viewModel = viewModel;
        RebuildBars();
    }

    public void ScheduleReposition()
    {
        if (_repositionPending) return;
        _repositionPending = true;
        _canvas.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _repositionPending = false;
            Reposition();
        });
    }

    public void Clear()
    {
        _canvas.Children.Clear();
        _regionBars.Clear();
    }

    private void RebuildBars()
    {
        Clear();
        if (_mapping == null || _viewModel == null) return;

        foreach (var range in _mapping.AllConflictRanges)
        {
            var bar = CreateBar(range.Region);
            _regionBars[range.Region.Index] = bar;
            _canvas.Children.Add(bar);
        }

        ScheduleReposition();
    }

    public void UpdateBarStates()
    {
        if (_mapping == null) return;

        foreach (var range in _mapping.AllConflictRanges)
        {
            if (_regionBars.TryGetValue(range.Region.Index, out var bar))
            {
                UpdateBarContent(bar, range.Region);
            }
        }
    }

    private void Reposition()
    {
        if (_mapping == null) return;

        var textView = _editor.TextArea.TextView;
        if (!textView.VisualLinesValid) return;

        // Transform from text view coordinates to canvas coordinates
        Point origin;
        try
        {
            origin = textView.TransformToVisual(_canvas).Transform(new Point(0, 0));
        }
        catch (InvalidOperationException ex)
        {
            // Not in the same visual tree yet — layout pass hasn't caught up.
            Leaf.Services.Log.Info("ConflictOverlay", $"TransformToVisual failed: {ex.Message}");
            return;
        }

        foreach (var range in _mapping.AllConflictRanges)
        {
            if (!_regionBars.TryGetValue(range.Region.Index, out var bar)) continue;

            double visualTop;
            try
            {
                // Position on the header line (blank line inserted before conflict content)
                var headerLineNum = Math.Min(range.HeaderLine, _editor.Document.LineCount);
                var docLine = _editor.Document.GetLineByNumber(headerLineNum);
                visualTop = textView.GetVisualTopByDocumentLine(docLine.LineNumber);
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException
                                    or InvalidOperationException)
            {
                // Document was mutated between mapping and render — hide the
                // bar for this region; it will reposition on the next pass.
                Leaf.Services.Log.Info("ConflictOverlay", $"Line lookup failed for region {range.Region.Index}: {ex.GetType().Name}: {ex.Message}");
                bar.Visibility = Visibility.Collapsed;
                continue;
            }

            double yInTextView = visualTop - textView.VerticalOffset;
            double barHeight = bar.DesiredSize.Height > 0 ? bar.DesiredSize.Height : 28;

            // Hide if outside the viewport
            if (yInTextView + barHeight < 0 || yInTextView > textView.ActualHeight + barHeight)
            {
                bar.Visibility = Visibility.Collapsed;
                continue;
            }

            bar.Visibility = Visibility.Visible;
            Canvas.SetLeft(bar, 0);
            bar.Width = _canvas.ActualWidth;
            Canvas.SetTop(bar, origin.Y + yInTextView);
        }
    }

    private Border CreateBar(MergeRegion region)
    {
        var bar = new Border
        {
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(0),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };

        UpdateBarContent(bar, region);
        return bar;
    }

    private void UpdateBarContent(Border bar, MergeRegion region)
    {
        var leftStack = new StackPanel { Orientation = Orientation.Horizontal };

        if (region.IsResolved)
        {
            bar.Background = ResolvedBg;
            bar.Opacity = 1.0;

            leftStack.Children.Add(new SymbolIcon
            {
                Symbol = Symbol.CheckmarkCircle,
                FontSize = 10,
                Foreground = ResolvedColor,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = $"Conflict {region.ConflictNumber} — Resolved: {region.ResolutionLabel}",
                FontSize = 10,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            bar.Background = UnresolvedBg;
            bar.Opacity = 1.0;

            leftStack.Children.Add(new SymbolIcon
            {
                Symbol = Symbol.Warning,
                FontSize = 10,
                Foreground = WarningColor,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            leftStack.Children.Add(new TextBlock
            {
                Text = $"Conflict {region.ConflictNumber}",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });

            leftStack.Children.Add(new TextBlock
            {
                Text = region.ResolutionLabel,
                FontSize = 9,
                Foreground = Brushes.White,
                Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        bar.Child = leftStack;
    }

    private static Brush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

}
