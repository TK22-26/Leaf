using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Leaf.Helpers;
using Leaf.Models;
using Leaf.ViewModels;
using System.Linq;

namespace Leaf.Controls;

/// <summary>
/// Interaction logic for DiffViewerControl.xaml
/// </summary>
public partial class DiffViewerControl : UserControl
{
    private DiffViewerViewModel? _viewModel;
    private readonly DiffBackgroundRenderer _renderer = new();
    private ScrollViewer? _blameScrollViewer;
    private bool _isSyncingBlameScroll;
    private IHighlightingDefinition? _lastHighlighting;

    public DiffViewerControl()
    {
        InitializeComponent();

        // Set up background renderer for diff highlighting
        DiffEditor.TextArea.TextView.BackgroundRenderers.Add(_renderer);

        // Handle DataContext changes
        DataContextChanged += OnDataContextChanged;

        // Configure editor
        ConfigureEditor(DiffEditor);
        ConfigureEditor(BlameEditor);

        Loaded += (_, _) =>
        {
            AttachBlameScrollSync();
            UpdateBlameLineHeight();
        };
    }

    private static void ConfigureEditor(TextEditor editor)
    {
        editor.TextArea.TextView.Options.EnableVirtualSpace = false;
        editor.TextArea.TextView.Options.AllowScrollBelowDocument = false;
        editor.TextArea.SelectionCornerRadius = 0;
        editor.TextArea.SelectionBorder = null;

        // Style hyperlinks to match dark theme (light blue instead of dark blue)
        editor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(SyntaxHighlightingHelper.KeywordColor);
        editor.TextArea.TextView.LinkTextUnderline = false;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old view model
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        // Subscribe to new view model
        _viewModel = e.NewValue as DiffViewerViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Dispatcher.BeginInvoke(UpdateBlameLineHeight);
            UpdateFromViewModel();
        }
        else
        {
            ClearEditor();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DiffViewerViewModel.InlineContent):
            case nameof(DiffViewerViewModel.BlameContent):
            case nameof(DiffViewerViewModel.Lines):
            case nameof(DiffViewerViewModel.SyntaxHighlighting):
            case nameof(DiffViewerViewModel.Mode):
                UpdateFromViewModel();
                break;
        }
    }

    private void UpdateFromViewModel()
    {
        if (_viewModel == null)
            return;

        // Apply dark theme colors to syntax highlighting
        var highlighting = _viewModel.SyntaxHighlighting;
        if (highlighting != null && !ReferenceEquals(highlighting, _lastHighlighting))
        {
            ApplyDarkThemeColors(highlighting);
            _lastHighlighting = highlighting;
        }

        if (_viewModel.IsDiffMode)
        {
            DiffEditor.SyntaxHighlighting = highlighting;
            DiffEditor.Text = _viewModel.InlineContent;
            _renderer.SetLines(_viewModel.Lines);
            DiffEditor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
            DiffEditor.ScrollToHome();
        }
        else
        {
            _renderer.SetLines(null);
        }

        if (_viewModel.IsBlameMode && BlameEditor != null)
        {
            BlameEditor.SyntaxHighlighting = highlighting;
            if (!string.Equals(BlameEditor.Text, _viewModel.BlameContent, StringComparison.Ordinal))
            {
                BlameEditor.Text = _viewModel.BlameContent;
            }
            BlameEditor.ScrollToHome();
        }
    }

    private static void ApplyDarkThemeColors(IHighlightingDefinition highlighting)
    {
        SyntaxHighlightingHelper.ApplyDarkThemeColors(highlighting);
    }

    private void ClearEditor()
    {
        DiffEditor.Text = string.Empty;
        DiffEditor.SyntaxHighlighting = null;
        _renderer.SetLines(null);
        if (BlameEditor != null)
        {
            BlameEditor.Text = string.Empty;
            BlameEditor.SyntaxHighlighting = null;
        }
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel?.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void HunkItem_RevertHunkRequested(object? sender, DiffHunk hunk)
    {
        if (_viewModel != null)
        {
            await _viewModel.RevertHunkAsync(hunk);
        }
    }

    private void AttachBlameScrollSync()
    {
        _blameScrollViewer = BlameScrollViewer;
        if (BlameEditor != null)
        {
            BlameEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) =>
            {
                if (_blameScrollViewer == null)
                {
                    return;
                }

                if (_isSyncingBlameScroll)
                {
                    return;
                }

                if (!BlameEditor.TextArea.TextView.VisualLinesValid)
                {
                    return;
                }

                _isSyncingBlameScroll = true;
                var lineHeight = BlameEditor.TextArea.TextView.DefaultLineHeight;
                var firstLine = BlameEditor.TextArea.TextView.VisualLines.FirstOrDefault()?.FirstDocumentLine?.LineNumber ?? 1;
                var target = Math.Max(0, (firstLine - 1) * lineHeight);
                _blameScrollViewer.ScrollToVerticalOffset(target);
                _isSyncingBlameScroll = false;
            };
        }
    }

    private void UpdateBlameLineHeight()
    {
        if (_viewModel == null || BlameEditor == null)
        {
            return;
        }

        var height = BlameEditor.TextArea.TextView.DefaultLineHeight;
        if (height > 0)
        {
            _viewModel.BlameLineHeight = height;
        }

    }

}
