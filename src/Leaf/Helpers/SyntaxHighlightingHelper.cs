using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Leaf.Helpers;

/// <summary>
/// Shared dark theme color mapping for AvalonEdit syntax highlighting.
/// Extracted from DiffViewerControl to avoid duplication.
/// </summary>
public static class SyntaxHighlightingHelper
{
    // Dark theme colors for syntax highlighting
    public static readonly Color KeywordColor = Color.FromRgb(0x56, 0x9C, 0xD6);      // Light blue
    public static readonly Color StringColor = Color.FromRgb(0xCE, 0x91, 0x78);       // Orange/salmon
    public static readonly Color CommentColor = Color.FromRgb(0x6A, 0x99, 0x55);      // Green
    public static readonly Color NumberColor = Color.FromRgb(0xB5, 0xCE, 0xA8);       // Light green
    public static readonly Color TypeColor = Color.FromRgb(0x55, 0x98, 0xD0);         // Blue
    public static readonly Color MethodColor = Color.FromRgb(0xDC, 0xDC, 0xAA);       // Yellow
    public static readonly Color PreprocessorColor = Color.FromRgb(0x9B, 0x9B, 0x9B); // Gray
    public static readonly Color XmlTagColor = Color.FromRgb(0x56, 0x9C, 0xD6);       // Light blue
    public static readonly Color XmlAttributeColor = Color.FromRgb(0x9C, 0xDC, 0xFE); // Lighter blue
    public static readonly Color XmlValueColor = Color.FromRgb(0xCE, 0x91, 0x78);     // Orange/salmon

    private static readonly Dictionary<string, Color> ColorMap = new(StringComparer.OrdinalIgnoreCase)
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

    public static void ApplyDarkThemeColors(IHighlightingDefinition highlighting)
    {
        foreach (var namedColor in highlighting.NamedHighlightingColors)
        {
            if (ColorMap.TryGetValue(namedColor.Name, out var color))
            {
                namedColor.Foreground = new SimpleHighlightingBrush(color);
            }
            else
            {
                FixDarkColor(namedColor);
            }
        }

        // Also process main colors if present
        foreach (var rule in highlighting.MainRuleSet?.Rules ?? [])
        {
            if (rule.Color != null)
            {
                if (rule.Color.Name != null && ColorMap.TryGetValue(rule.Color.Name, out var mappedColor))
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
        ProcessRuleSet(highlighting.MainRuleSet, visited);
    }

    private static void ProcessRuleSet(HighlightingRuleSet? ruleSet, HashSet<HighlightingRuleSet> visited)
    {
        if (ruleSet == null || !visited.Add(ruleSet)) return;

        foreach (var rule in ruleSet.Rules)
        {
            if (rule.Color != null)
            {
                if (rule.Color.Name != null && ColorMap.TryGetValue(rule.Color.Name, out var mappedColor))
                {
                    rule.Color.Foreground = new SimpleHighlightingBrush(mappedColor);
                }
                else
                {
                    FixDarkColor(rule.Color);
                }
            }
        }

        foreach (var span in ruleSet.Spans)
        {
            if (span.SpanColor != null)
            {
                if (span.SpanColor.Name != null && ColorMap.TryGetValue(span.SpanColor.Name, out var color))
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

            ProcessRuleSet(span.RuleSet, visited);
        }
    }

    public static void FixDarkColor(HighlightingColor highlightingColor)
    {
        if (highlightingColor.Foreground is SimpleHighlightingBrush brush)
        {
            var wpfBrush = brush.GetBrush(null);
            if (wpfBrush is SolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                var brightness = (color.R + color.G + color.B) / 3.0;

                // Dark blue detection
                if (color.B > color.R && color.B > color.G && brightness < 180)
                {
                    highlightingColor.Foreground = new SimpleHighlightingBrush(KeywordColor);
                }
                // Dark red detection
                else if (color.R > color.B && color.R > color.G && color.R > 100 && brightness < 180)
                {
                    highlightingColor.Foreground = new SimpleHighlightingBrush(TypeColor);
                }
                // Very dark colors
                else if (brightness < 100)
                {
                    highlightingColor.Foreground = new SimpleHighlightingBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
                }
            }
        }
    }
}
