using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
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
    private ScrollViewer? _blameEditorScrollViewer;
    private bool _isSyncingBlameScroll;
    private IHighlightingDefinition? _lastHighlighting;

    // Dark theme colors for syntax highlighting
    private static readonly Color KeywordColor = Color.FromRgb(0x56, 0x9C, 0xD6);      // Light blue
    private static readonly Color StringColor = Color.FromRgb(0xCE, 0x91, 0x78);       // Orange/salmon
    private static readonly Color CommentColor = Color.FromRgb(0x6A, 0x99, 0x55);      // Green
    private static readonly Color NumberColor = Color.FromRgb(0xB5, 0xCE, 0xA8);       // Light green
    private static readonly Color TypeColor = Color.FromRgb(0x55, 0x98, 0xD0);         // Blue (for class, string, bool, int)
    private static readonly Color MethodColor = Color.FromRgb(0xDC, 0xDC, 0xAA);       // Yellow
    private static readonly Color PreprocessorColor = Color.FromRgb(0x9B, 0x9B, 0x9B); // Gray
    private static readonly Color XmlTagColor = Color.FromRgb(0x56, 0x9C, 0xD6);       // Light blue
    private static readonly Color XmlAttributeColor = Color.FromRgb(0x9C, 0xDC, 0xFE); // Lighter blue
    private static readonly Color XmlValueColor = Color.FromRgb(0xCE, 0x91, 0x78);     // Orange/salmon

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
        editor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(KeywordColor);
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
        // Map of common color names to dark theme colors
        var colorMap = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            // C#/Java/JavaScript keywords
            { "Keyword", KeywordColor },
            { "Keywords", KeywordColor },
            { "ControlKeywords", KeywordColor },
            { "GotoKeywords", KeywordColor },
            { "AccessKeywords", KeywordColor },
            { "OperatorKeywords", KeywordColor },
            { "SelectionKeywords", KeywordColor },
            { "TrueFalse", KeywordColor },
            { "NullOrValueKeywords", KeywordColor },
            { "Modifiers", KeywordColor },
            { "Visibility", KeywordColor },
            { "ContextKeywords", KeywordColor },
            { "ExceptionKeywords", KeywordColor },
            { "CheckedKeyword", KeywordColor },
            { "UnsafeKeywords", KeywordColor },
            { "QueryKeywords", KeywordColor },
            { "ParamKeywords", KeywordColor },
            { "ParameterModifiers", KeywordColor },
            { "GetSetAddRemove", KeywordColor },
            { "ThisOrBaseReference", KeywordColor },
            { "SemanticKeywords", KeywordColor },

            // Strings and chars
            { "String", StringColor },
            { "Char", StringColor },
            { "StringInterpolation", StringColor },
            { "Uri", StringColor },
            { "Url", StringColor },
            { "Link", StringColor },
            { "Path", StringColor },
            { "JsonString", StringColor },
            { "JsonPropertyName", XmlAttributeColor },
            { "PropertyName", XmlAttributeColor },
            { "Key", XmlAttributeColor },

            // Comments
            { "Comment", CommentColor },
            { "DocComment", CommentColor },
            { "Documentation", CommentColor },
            { "XmlDoc", CommentColor },

            // Numbers
            { "NumberLiteral", NumberColor },
            { "Number", NumberColor },
            { "Digits", NumberColor },

            // Types
            { "Class", TypeColor },
            { "ValueTypes", TypeColor },
            { "ReferenceTypes", TypeColor },
            { "TypeKeywords", TypeColor },
            { "NamespaceKeywords", TypeColor },
            { "Type", TypeColor },
            { "BuiltInTypes", TypeColor },
            { "Interface", TypeColor },
            { "Struct", TypeColor },
            { "Enum", TypeColor },
            { "Delegate", TypeColor },

            // Methods
            { "MethodCall", MethodColor },
            { "MethodName", MethodColor },
            { "FunctionName", MethodColor },
            { "Function", MethodColor },

            // Preprocessor
            { "Preprocessor", PreprocessorColor },
            { "Punctuation", PreprocessorColor },

            // XML/XAML
            { "XmlTag", XmlTagColor },
            { "XmlName", XmlTagColor },
            { "XmlBracket", XmlTagColor },
            { "XmlAttribute", XmlAttributeColor },
            { "AttributeName", XmlAttributeColor },
            { "XmlAttributeValue", XmlValueColor },
            { "AttributeValue", XmlValueColor },
            { "XmlString", XmlValueColor },
            { "XmlComment", CommentColor },
            { "XmlCData", StringColor },
            { "Entity", StringColor },
            { "Entities", StringColor },
        };

        foreach (var namedColor in highlighting.NamedHighlightingColors)
        {
            if (colorMap.TryGetValue(namedColor.Name, out var color))
            {
                namedColor.Foreground = new SimpleHighlightingBrush(color);
            }
            else
            {
                // Fallback: if the color is dark blue, change it to light blue
                FixDarkColor(namedColor);
            }
        }

        // Also process main colors if present
        foreach (var rule in highlighting.MainRuleSet?.Rules ?? [])
        {
            if (rule.Color != null)
            {
                if (rule.Color.Name != null && colorMap.TryGetValue(rule.Color.Name, out var mappedColor))
                {
                    rule.Color.Foreground = new SimpleHighlightingBrush(mappedColor);
                }
                else
                {
                    FixDarkColor(rule.Color);
                }
            }
        }

        // Process nested rule sets with visited tracking to prevent stack overflow
        var visited = new HashSet<HighlightingRuleSet>();
        ProcessRuleSet(highlighting.MainRuleSet, colorMap, visited);
    }

    private static void ProcessRuleSet(HighlightingRuleSet? ruleSet, Dictionary<string, Color> colorMap, HashSet<HighlightingRuleSet> visited)
    {
        if (ruleSet == null || !visited.Add(ruleSet)) return;

        // Process rules in this ruleset
        foreach (var rule in ruleSet.Rules)
        {
            if (rule.Color != null)
            {
                if (rule.Color.Name != null && colorMap.TryGetValue(rule.Color.Name, out var mappedColor))
                {
                    rule.Color.Foreground = new SimpleHighlightingBrush(mappedColor);
                }
                else
                {
                    FixDarkColor(rule.Color);
                }
            }
        }

        // Process spans in this ruleset
        foreach (var span in ruleSet.Spans)
        {
            if (span.SpanColor != null)
            {
                if (span.SpanColor.Name != null && colorMap.TryGetValue(span.SpanColor.Name, out var color))
                {
                    span.SpanColor.Foreground = new SimpleHighlightingBrush(color);
                }
                else
                {
                    FixDarkColor(span.SpanColor);
                }
            }

            if (span.StartColor != null) FixDarkColor(span.StartColor);
            if (span.EndColor != null) FixDarkColor(span.EndColor);

            // Recursively process the span's ruleset
            ProcessRuleSet(span.RuleSet, colorMap, visited);
        }
    }

    private static void FixDarkColor(HighlightingColor highlightingColor)
    {
        // Check if the foreground is a dark color that's hard to read
        if (highlightingColor.Foreground is SimpleHighlightingBrush brush)
        {
            var wpfBrush = brush.GetBrush(null);
            if (wpfBrush is SolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;

                // Calculate if the color is "dark" (low overall brightness)
                var brightness = (color.R + color.G + color.B) / 3.0;

                // Dark blue detection: blue-dominant and dark
                if (color.B > color.R && color.B > color.G && brightness < 180)
                {
                    highlightingColor.Foreground = new SimpleHighlightingBrush(KeywordColor);
                }
                // Dark red detection: red-dominant and dark - change to type blue
                else if (color.R > color.B && color.R > color.G && color.R > 100 && brightness < 180)
                {
                    highlightingColor.Foreground = new SimpleHighlightingBrush(TypeColor);
                }
                // Very dark colors (hard to see on dark background)
                else if (brightness < 100)
                {
                    highlightingColor.Foreground = new SimpleHighlightingBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)); // Light gray
                }
            }
        }
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
        _blameEditorScrollViewer = FindScrollViewer(BlameEditor);
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

    private static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root == null)
        {
            return null;
        }

        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindScrollViewer(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
