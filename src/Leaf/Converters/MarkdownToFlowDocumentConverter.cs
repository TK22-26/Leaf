using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Leaf.Converters;

/// <summary>
/// Converts a markdown string to a FlowDocument for display in a RichTextBox.
/// Supports headers, bold, italic, inline code, code fences, links, and bullet/numbered lists.
/// </summary>
public partial class MarkdownToFlowDocumentConverter : IValueConverter
{
    private static readonly FontFamily MonospaceFont = new("Cascadia Mono, Consolas, Courier New");
    private static readonly Color BadgeLeftColor = Color.FromRgb(0x4D, 0x4D, 0x4D);
    private static readonly HttpClient BadgeHttpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly ConcurrentDictionary<string, BadgeDefinition?> BadgeCache = new(StringComparer.OrdinalIgnoreCase);
    private const double BaseSize = 13.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = BaseSize,
            Foreground = ResolveBrush("TextFillColorTertiaryBrush", Color.FromRgb(0xA9, 0xB4, 0xC1)),
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

                doc.Blocks.Add(MakeCodeBlock(codeLines));
                continue;
            }

            var trimmedLine = line.Trim();

            if (TryParseMarkdownImage(trimmedLine, out var imageSpec))
            {
                var imageSpecs = new List<MarkdownImageSpec> { imageSpec };
                var nextIndex = i + 1;

                while (nextIndex < lines.Length && TryParseMarkdownImage(lines[nextIndex].Trim(), out var nextImageSpec))
                {
                    imageSpecs.Add(nextImageSpec);
                    nextIndex++;
                }

                doc.Blocks.Add(MakeImageRowBlock(imageSpecs));
                i = nextIndex;
                continue;
            }

            // Headers
            var headingMatch = HeadingPattern().Match(line);
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var headingText = headingMatch.Groups[2].Value.Trim();
                doc.Blocks.Add(MakeHeaderParagraph(headingText, GetHeaderFontSize(level)));
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
            Foreground = ResolveBrush("TextFillColorPrimaryBrush", Color.FromRgb(0xF3, 0xF4, 0xF6)),
            Margin = new Thickness(0, 8, 0, 2)
        };
        var bold = new Bold(new Run(text))
        {
            FontSize = fontSize
        };
        para.Inlines.Add(bold);
        return para;
    }

    private static Block MakeCodeBlock(IReadOnlyList<string> codeLines)
    {
        var textBlock = new TextBlock
        {
            FontFamily = MonospaceFont,
            FontSize = BaseSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0xF7, 0xFA)),
            Text = string.Join("\n", codeLines),
            TextWrapping = TextWrapping.NoWrap
        };

        var border = new Border
        {
            Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x19, 0x23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x5A, 0x6F)),
            BorderThickness = new Thickness(1.25),
            CornerRadius = new CornerRadius(6),
            Child = new Border
            {
                Padding = new Thickness(12, 9, 12, 9),
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1F, 0x2A)),
                CornerRadius = new CornerRadius(6),
                Child = textBlock
            }
        };

        return new BlockUIContainer(border);
    }

    private static Block MakeImageRowBlock(IReadOnlyList<MarkdownImageSpec> imageSpecs)
    {
        var wrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };

        foreach (var imageSpec in imageSpecs)
        {
            var element = CreateImageElement(imageSpec);
            element.Margin = new Thickness(0, 0, 6, 6);
            wrapPanel.Children.Add(element);
        }

        return new BlockUIContainer(wrapPanel)
        {
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private static FrameworkElement CreateImageElement(MarkdownImageSpec imageSpec)
    {
        if (TryCreateBadgeElement(imageSpec, out var badgeElement))
            return badgeElement;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imageSpec.ImageUrl, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnDemand;
            bitmap.EndInit();

            FrameworkElement imageElement = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxHeight = 56
            };

            if (!string.IsNullOrWhiteSpace(imageSpec.NavigateUrl))
                imageElement = WrapInLinkButton(imageElement, imageSpec.NavigateUrl);

            return imageElement;
        }
        catch
        {
            return CreateSimpleBadgeElement(string.IsNullOrWhiteSpace(imageSpec.AltText) ? imageSpec.ImageUrl : imageSpec.AltText);
        }
    }

    private static bool TryCreateBadgeElement(MarkdownImageSpec imageSpec, out FrameworkElement element)
    {
        element = null!;

        if (TryParseShieldsBadge(imageSpec.ImageUrl, out var shieldBadge) ||
            TryParseSvgBadge(imageSpec.ImageUrl, imageSpec.AltText, out shieldBadge))
        {
            element = CreateBadgeElement(shieldBadge);
            if (!string.IsNullOrWhiteSpace(imageSpec.NavigateUrl))
                element = WrapInLinkButton(element, imageSpec.NavigateUrl);

            return true;
        }

        return false;
    }

    private static FrameworkElement CreateBadgeElement(BadgeDefinition badge)
    {
        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            SnapsToDevicePixels = true
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(badge.LeftColor),
            Padding = new Thickness(10, 4, badge.RightText == null ? 10 : 8, 4),
            Child = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Text = badge.LeftText
            }
        });

        if (!string.IsNullOrWhiteSpace(badge.RightText))
        {
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(badge.RightColor),
                Padding = new Thickness(8, 4, 10, 4),
                Child = new TextBlock
                {
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Text = badge.RightText
                }
            });
        }

        border.Child = panel;
        return border;
    }

    private static FrameworkElement CreateSimpleBadgeElement(string text)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x34, 0x3F)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x5A, 0x6F)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4),
            Child = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Text = text
            }
        };
    }

    private static double GetHeaderFontSize(int level) => level switch
    {
        1 => BaseSize + 8,
        2 => BaseSize + 4,
        3 => BaseSize + 1,
        4 => BaseSize,
        5 => BaseSize - 1,
        _ => BaseSize - 2
    };

    private static bool LooksLikeBadgeOrSvg(string imageUrl)
        => imageUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
           || imageUrl.Contains("shields.io", StringComparison.OrdinalIgnoreCase)
           || imageUrl.Contains("/badge", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseMarkdownImage(string line, out MarkdownImageSpec spec)
    {
        var linkedImageMatch = LinkedImagePattern().Match(line);
        if (linkedImageMatch.Success)
        {
            spec = new MarkdownImageSpec(
                linkedImageMatch.Groups[1].Value,
                linkedImageMatch.Groups[2].Value,
                linkedImageMatch.Groups[3].Value);
            return true;
        }

        var imageMatch = ImagePattern().Match(line);
        if (imageMatch.Success)
        {
            spec = new MarkdownImageSpec(
                imageMatch.Groups[1].Value,
                imageMatch.Groups[2].Value,
                null);
            return true;
        }

        spec = default;
        return false;
    }

    private static bool TryParseShieldsBadge(string imageUrl, out BadgeDefinition badge)
    {
        badge = default;

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Contains("shields.io", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Contains("/badge/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segment = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf("/badge/", StringComparison.OrdinalIgnoreCase) + 7)..];
        if (segment.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            segment = segment[..^4];

        var lastDash = segment.LastIndexOf('-');
        if (lastDash <= 0)
            return false;

        var beforeColor = segment[..lastDash];
        var secondLastDash = beforeColor.LastIndexOf('-');
        if (secondLastDash <= 0)
            return false;

        var left = DecodeBadgeToken(beforeColor[..secondLastDash]);
        var right = DecodeBadgeToken(beforeColor[(secondLastDash + 1)..]);
        var colorToken = DecodeBadgeToken(segment[(lastDash + 1)..]);

        badge = new BadgeDefinition(left, right, BadgeLeftColor, ParseBadgeColor(colorToken, Color.FromRgb(0x28, 0xA7, 0x45)));
        return true;
    }

    private static bool TryParseSvgBadge(string imageUrl, string fallbackText, out BadgeDefinition badge)
    {
        badge = default;

        if (!LooksLikeBadgeOrSvg(imageUrl))
            return false;

        var parsed = BadgeCache.GetOrAdd(imageUrl, url => ParseSvgBadgeDefinition(url, fallbackText));
        if (parsed == null)
            return false;

        badge = parsed.Value;
        return true;
    }

    private static BadgeDefinition? ParseSvgBadgeDefinition(string imageUrl, string fallbackText)
    {
        try
        {
            var svg = BadgeHttpClient.GetStringAsync(imageUrl).GetAwaiter().GetResult();
            var document = XDocument.Parse(svg);

            var texts = document
                .Descendants()
                .Where(element => element.Name.LocalName == "text")
                .Select(element => element.Value.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var fills = document
                .Descendants()
                .Where(element => element.Name.LocalName == "rect")
                .Select(element => element.Attribute("fill")?.Value)
                .Where(fill => !string.IsNullOrWhiteSpace(fill) && !fill.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var leftText = texts.ElementAtOrDefault(0);
            var rightText = texts.ElementAtOrDefault(1);

            if (string.IsNullOrWhiteSpace(leftText) && string.IsNullOrWhiteSpace(rightText))
                return string.IsNullOrWhiteSpace(fallbackText)
                    ? null
                    : new BadgeDefinition(fallbackText, null, BadgeLeftColor, Color.FromRgb(0x28, 0xA7, 0x45));

            return new BadgeDefinition(
                string.IsNullOrWhiteSpace(leftText) ? fallbackText : leftText!,
                rightText,
                ParseBadgeColor(fills.ElementAtOrDefault(0), BadgeLeftColor),
                ParseBadgeColor(fills.ElementAtOrDefault(1), Color.FromRgb(0x28, 0xA7, 0x45)));
        }
        catch
        {
            return string.IsNullOrWhiteSpace(fallbackText)
                ? null
                : new BadgeDefinition(fallbackText, null, BadgeLeftColor, Color.FromRgb(0x28, 0xA7, 0x45));
        }
    }

    private static string DecodeBadgeToken(string token)
        => Uri.UnescapeDataString(token)
            .Replace("--", "-", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal);

    private static Color ParseBadgeColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        try
        {
            var normalized = value.StartsWith('#') ? value : $"#{value}";
            if (ColorConverter.ConvertFromString(normalized) is Color color)
                return color;
        }
        catch
        {
            try
            {
                if (ColorConverter.ConvertFromString(value) is Color namedColor)
                    return namedColor;
            }
            catch
            {
            }
        }

        return fallback;
    }

    private static FrameworkElement WrapInLinkButton(FrameworkElement content, string navigateUrl)
    {
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0),
            Content = content
        };
        button.Click += (_, _) => OpenUrl(navigateUrl);
        return button;
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
                inlines.Add(MakeInlineCodeInline(text[(earliest + 1)..end]));
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
                var url = text[(openParen + 1)..closeParen];
                var hyperlink = new Hyperlink
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45))
                };
                if (Uri.TryCreate(url, UriKind.Absolute, out var navigateUri))
                {
                    hyperlink.NavigateUri = navigateUri;
                }
                hyperlink.Click += (_, _) => OpenUrl(url);
                ParseInlines(linkText, hyperlink.Inlines);
                inlines.Add(hyperlink);
                pos = closeParen + 1;
            }
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore bad URLs so markdown rendering never crashes the UI.
        }
    }

    private static Brush ResolveBrush(string resourceKey, Color fallbackColor)
        => Application.Current?.TryFindResource(resourceKey) as Brush
           ?? new SolidColorBrush(fallbackColor);

    private static Inline MakeInlineCodeInline(string codeText)
    {
        var inlineBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x31, 0x3C)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4B, 0x5A, 0x6F)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Child = new TextBlock
            {
                FontFamily = MonospaceFont,
                FontSize = BaseSize - 1,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF6, 0xFC)),
                Text = codeText
            }
        };

        return new InlineUIContainer(inlineBorder)
        {
            BaselineAlignment = BaselineAlignment.Center
        };
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

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\[!\[(.*?)\]\((.*?)\)\]\((.*?)\)$")]
    private static partial Regex LinkedImagePattern();

    [GeneratedRegex(@"^!\[(.*?)\]\((.*?)\)$")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"^\d+\.\s+(.+)$")]
    private static partial Regex NumberedListItemPattern();

    private readonly record struct MarkdownImageSpec(string AltText, string ImageUrl, string? NavigateUrl);

    private readonly record struct BadgeDefinition(string LeftText, string? RightText, Color LeftColor, Color RightColor);
}
