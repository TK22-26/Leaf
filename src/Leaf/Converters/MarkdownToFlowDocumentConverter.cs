using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace Leaf.Converters;

/// <summary>
/// Converts a markdown string to a FlowDocument for display in a RichTextBox.
/// Supports headers, bold, italic, inline code, code fences, links, and bullet/numbered lists.
/// </summary>
public partial class MarkdownToFlowDocumentConverter : IValueConverter
{
    private static readonly FontFamily MonospaceFont = new("Cascadia Mono, Consolas, Courier New");
    private const double BaseSize = 13.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = BaseSize,
            PagePadding = new Thickness(0)
        };

        if (value is not string markdown || string.IsNullOrWhiteSpace(markdown))
            return doc;

        var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            // Code fence: ```
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var codeLines = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                i++; // consume closing ```

                var codePara = new Paragraph
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 4, 0, 4),
                    FontFamily = MonospaceFont,
                    FontSize = BaseSize - 1
                };
                codePara.Inlines.Add(new Run(string.Join("\n", codeLines))
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCE, 0xCE, 0xCE))
                });
                doc.Blocks.Add(codePara);
                continue;
            }

            // Headers
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                doc.Blocks.Add(MakeHeaderParagraph(line[4..], BaseSize + 1));
                i++;
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                doc.Blocks.Add(MakeHeaderParagraph(line[3..], BaseSize + 4));
                i++;
                continue;
            }
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                doc.Blocks.Add(MakeHeaderParagraph(line[2..], BaseSize + 8));
                i++;
                continue;
            }

            // Bullet list items
            if (line.Length >= 2 && (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)))
            {
                var listBlock = new List
                {
                    MarkerStyle = TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(20, 0, 0, 0)
                };

                while (i < lines.Length && lines[i].Length >= 2 &&
                       (lines[i].StartsWith("- ", StringComparison.Ordinal) || lines[i].StartsWith("* ", StringComparison.Ordinal)))
                {
                    var item = new ListItem(MakeInlineParagraph(lines[i][2..], noMargin: true));
                    listBlock.ListItems.Add(item);
                    i++;
                }
                doc.Blocks.Add(listBlock);
                continue;
            }

            // Numbered list items
            if (NumberedListItemPattern().IsMatch(line))
            {
                var listBlock = new List
                {
                    MarkerStyle = TextMarkerStyle.Decimal,
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(20, 0, 0, 0)
                };

                while (i < lines.Length && NumberedListItemPattern().IsMatch(lines[i]))
                {
                    var match = NumberedListItemPattern().Match(lines[i]);
                    var item = new ListItem(MakeInlineParagraph(match.Groups[1].Value, noMargin: true));
                    listBlock.ListItems.Add(item);
                    i++;
                }
                doc.Blocks.Add(listBlock);
                continue;
            }

            // Blank line — emit small spacing paragraph
            if (string.IsNullOrWhiteSpace(line))
            {
                // Collapse consecutive blank lines
                while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                    i++;
                // Only add spacing if more content follows
                if (i < lines.Length)
                {
                    doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 4, 0, 0) });
                }
                continue;
            }

            // Regular paragraph
            doc.Blocks.Add(MakeInlineParagraph(line));
            i++;
        }

        return doc;
    }

    private static Paragraph MakeHeaderParagraph(string text, double fontSize)
    {
        var para = new Paragraph
        {
            Margin = new Thickness(0, 8, 0, 2)
        };
        var bold = new Bold(new Run(text))
        {
            FontSize = fontSize
        };
        para.Inlines.Add(bold);
        return para;
    }

    /// <summary>
    /// Parses inline markdown (bold, italic, inline code, links) into a Paragraph.
    /// </summary>
    private static Paragraph MakeInlineParagraph(string text, bool noMargin = false)
    {
        var para = new Paragraph
        {
            Margin = noMargin ? new Thickness(0) : new Thickness(0, 1, 0, 1)
        };
        ParseInlines(text, para.Inlines);
        return para;
    }

    /// <summary>
    /// Tokenises inline markdown and appends WPF Inline elements to the given collection.
    /// Handles bold (**text**), italic (*text*), inline code (`code`), and links [text](url).
    /// </summary>
    private static void ParseInlines(string text, InlineCollection inlines)
    {
        // Pattern order matters: code > bold > italic > link > plain
        // We scan left-to-right for the earliest opening token.
        int pos = 0;
        while (pos < text.Length)
        {
            // Find next special token
            int boldIdx   = text.IndexOf("**", pos, StringComparison.Ordinal);
            int italicIdx = FindStandaloneAsterisk(text, pos);
            int codeIdx   = text.IndexOf('`', pos);
            int linkIdx   = text.IndexOf('[', pos);

            // Pick the earliest (ignoring -1)
            int earliest = EarliestNonNeg(boldIdx, italicIdx, codeIdx, linkIdx);

            if (earliest == -1)
            {
                // No more tokens — append the rest as plain text
                inlines.Add(new Run(text[pos..]));
                break;
            }

            // Append plain text before this token
            if (earliest > pos)
                inlines.Add(new Run(text[pos..earliest]));

            if (earliest == boldIdx)
            {
                int end = text.IndexOf("**", earliest + 2, StringComparison.Ordinal);
                if (end == -1) { inlines.Add(new Run(text[earliest..])); break; }
                var inner = text[(earliest + 2)..end];
                var bold = new Bold();
                ParseInlines(inner, bold.Inlines);
                inlines.Add(bold);
                pos = end + 2;
            }
            else if (earliest == codeIdx)
            {
                int end = text.IndexOf('`', earliest + 1);
                if (end == -1) { inlines.Add(new Run(text[earliest..])); break; }
                var code = new Run(text[(earliest + 1)..end])
                {
                    FontFamily = MonospaceFont,
                    Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCE, 0xCE, 0xCE))
                };
                inlines.Add(code);
                pos = end + 1;
            }
            else if (earliest == italicIdx)
            {
                int end = FindStandaloneAsterisk(text, earliest + 1);
                if (end == -1) { inlines.Add(new Run(text[earliest..])); break; }
                var inner = text[(earliest + 1)..end];
                var italic = new Italic();
                ParseInlines(inner, italic.Inlines);
                inlines.Add(italic);
                pos = end + 1;
            }
            else // linkIdx
            {
                // Match [text](url)
                int closeBracket = text.IndexOf(']', earliest + 1);
                if (closeBracket == -1 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
                {
                    inlines.Add(new Run("["));
                    pos = earliest + 1;
                    continue;
                }
                int openParen = closeBracket + 1;
                int closeParen = text.IndexOf(')', openParen + 1);
                if (closeParen == -1)
                {
                    inlines.Add(new Run("["));
                    pos = earliest + 1;
                    continue;
                }
                var linkText = text[(earliest + 1)..closeBracket];
                var underline = new Underline();
                ParseInlines(linkText, underline.Inlines);
                inlines.Add(underline);
                pos = closeParen + 1;
            }
        }
    }

    /// <summary>
    /// Finds the index of a lone '*' (not part of '**') at or after <paramref name="startIndex"/>.
    /// Returns -1 if none found.
    /// </summary>
    private static int FindStandaloneAsterisk(string text, int startIndex)
    {
        int i = startIndex;
        while (i < text.Length)
        {
            int idx = text.IndexOf('*', i);
            if (idx == -1) return -1;

            // Skip '**'
            if (idx + 1 < text.Length && text[idx + 1] == '*')
            {
                i = idx + 2;
                continue;
            }
            // Also skip if preceded by another '*'
            if (idx > 0 && text[idx - 1] == '*')
            {
                i = idx + 1;
                continue;
            }

            return idx;
        }
        return -1;
    }

    private static int EarliestNonNeg(params int[] values)
    {
        int min = -1;
        foreach (var v in values)
        {
            if (v < 0) continue;
            if (min < 0 || v < min) min = v;
        }
        return min;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    [GeneratedRegex(@"^\d+\.\s+(.+)$")]
    private static partial Regex NumberedListItemPattern();
}
