using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Graph;
using Leaf.Models;

namespace Leaf.Controls.GitGraph;

public partial class GitGraphCanvas
{
    // Popup for showing branch names tooltip
    private System.Windows.Controls.Primitives.Popup? _branchTooltipPopup;
    private StackPanel? _branchTooltipPanel;

    // §5.8 signature tooltip — reuses the branch-tooltip's dark Border
    // styling but is its own Popup so the two can coexist (hovering a
    // signature badge while a branch overflow indicator is also under
    // the cursor doesn't replace one with the other).
    private System.Windows.Controls.Primitives.Popup? _signatureTooltipPopup;
    private StackPanel? _signatureTooltipPanel;
    private string? _signatureTooltipSha;

    // §5.17 tag tooltip — same pattern as signature tooltip, dedicated
    // popup so a tag chip hover doesn't replace a concurrent branch /
    // signature tooltip.
    private System.Windows.Controls.Primitives.Popup? _tagTooltipPopup;
    private StackPanel? _tagTooltipPanel;
    private string? _tagTooltipName;

    /// <summary>
    /// Show the §5.8 signature tooltip near the badge. Idempotent for
    /// the same SHA — hovering the same badge across frames is the
    /// common case and we don't want to rebuild children every time.
    /// </summary>
    private void ShowSignatureTooltip(GitTreeNode node, Point anchor)
    {
        if (string.Equals(_signatureTooltipSha, node.Sha, StringComparison.Ordinal)
            && _signatureTooltipPopup is { IsOpen: true })
        {
            // Already visible for this node — leave it where it first
            // popped up. Following the cursor causes the tooltip to
            // jitter on every mouse-move frame within the badge area
            // and is uncomfortable to read.
            return;
        }

        if (_signatureTooltipPopup == null)
        {
            _signatureTooltipPanel = new StackPanel { Orientation = Orientation.Vertical };
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Child = _signatureTooltipPanel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 12, ShadowDepth = 4, Opacity = 0.4
                },
            };
            _signatureTooltipPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = border,
                AllowsTransparency = true,
                PlacementTarget = this,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                StaysOpen = true,
            };
        }

        _signatureTooltipPanel!.Children.Clear();
        _signatureTooltipPanel.Children.Add(BuildSignatureLine(
            FontWeights.SemiBold, 12, SignatureSummaryFormatter.Format(node.SignatureStatus)));

        if (!string.IsNullOrWhiteSpace(node.SignerName) || !string.IsNullOrWhiteSpace(node.SignerEmail))
        {
            var ident = string.IsNullOrWhiteSpace(node.SignerEmail)
                ? node.SignerName
                : (string.IsNullOrWhiteSpace(node.SignerName)
                    ? node.SignerEmail
                    : $"{node.SignerName} <{node.SignerEmail}>");
            _signatureTooltipPanel.Children.Add(BuildSignatureLine(FontWeights.Normal, 11, ident));
        }
        if (!string.IsNullOrWhiteSpace(node.SignerKeyFingerprint))
        {
            _signatureTooltipPanel.Children.Add(BuildSignatureLine(
                FontWeights.Normal, 10, FormatFingerprint(node.SignerKeyFingerprint)));
        }

        _signatureTooltipPopup.HorizontalOffset = anchor.X + 14;
        _signatureTooltipPopup.VerticalOffset = anchor.Y + 14;
        _signatureTooltipPopup.IsOpen = true;
        _signatureTooltipSha = node.Sha;
    }

    private void HideSignatureTooltip()
    {
        if (_signatureTooltipPopup is { IsOpen: true })
            _signatureTooltipPopup.IsOpen = false;
        _signatureTooltipSha = null;
    }

    /// <summary>
    /// §5.17 — show a tag tooltip near the chip at <paramref name="anchor"/>.
    /// Idempotent for the same tag name (no jitter while the cursor is
    /// inside the chip).
    /// </summary>
    private void ShowTagTooltip(TagInfo tag, Point anchor)
    {
        if (string.Equals(_tagTooltipName, tag.Name, StringComparison.Ordinal)
            && _tagTooltipPopup is { IsOpen: true })
        {
            return;
        }

        if (_tagTooltipPopup == null)
        {
            _tagTooltipPanel = new StackPanel { Orientation = Orientation.Vertical };
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Child = _tagTooltipPanel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 12, ShadowDepth = 4, Opacity = 0.4,
                },
            };
            _tagTooltipPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = border,
                AllowsTransparency = true,
                PlacementTarget = this,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                StaysOpen = true,
            };
        }

        _tagTooltipPanel!.Children.Clear();

        // Header line: tag name (bold) + small kind indicator.
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock
        {
            Text = tag.Name,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        var kind = tag.IsSigned ? "signed" : (tag.IsAnnotated ? "annotated" : "lightweight");
        header.Children.Add(new TextBlock
        {
            Text = $"   {kind}",
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _tagTooltipPanel.Children.Add(header);

        // Annotation message preview (first non-empty line, truncated).
        if (!string.IsNullOrWhiteSpace(tag.Message))
        {
            var preview = FirstMessageLine(tag.Message);
            if (!string.IsNullOrEmpty(preview))
            {
                _tagTooltipPanel.Children.Add(new TextBlock
                {
                    Text = preview,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 4),
                    MaxWidth = 380,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
        }

        // Tagger / date row when annotated.
        if (tag.IsAnnotated)
        {
            var taggerLine = !string.IsNullOrWhiteSpace(tag.TaggerEmail)
                ? $"{tag.TaggerName} <{tag.TaggerEmail}>"
                : (tag.TaggerName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(taggerLine))
            {
                _tagTooltipPanel.Children.Add(new TextBlock
                {
                    Text = taggerLine,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 10,
                });
            }
            if (tag.TaggedAt is { } tagged)
            {
                _tagTooltipPanel.Children.Add(new TextBlock
                {
                    Text = tagged.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                        System.Globalization.CultureInfo.InvariantCulture),
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 10,
                });
            }
        }

        // Signature info when present — same wording vocabulary as the
        // commit signature tooltip so users learn one mental model.
        if (tag.IsSigned)
        {
            _tagTooltipPanel.Children.Add(BuildSignatureLine(
                FontWeights.SemiBold, 11, SignatureSummaryFormatter.Format(tag.SignatureStatus)));
            if (!string.IsNullOrWhiteSpace(tag.SignerKeyFingerprint))
            {
                _tagTooltipPanel.Children.Add(BuildSignatureLine(
                    FontWeights.Normal, 10, FormatFingerprint(tag.SignerKeyFingerprint)));
            }
        }

        _tagTooltipPopup.HorizontalOffset = anchor.X + 14;
        _tagTooltipPopup.VerticalOffset = anchor.Y + 14;
        _tagTooltipPopup.IsOpen = true;
        _tagTooltipName = tag.Name;
    }

    private void HideTagTooltip()
    {
        if (_tagTooltipPopup is { IsOpen: true })
            _tagTooltipPopup.IsOpen = false;
        _tagTooltipName = null;
    }

    /// <summary>
    /// Return the first non-empty, non-comment line of a tag's annotation
    /// message, capped at 120 chars. Mirrors what the picker preview does
    /// for commit templates. Uses the shared signature-block stripper so
    /// "first line" doesn't accidentally fall onto the PGP block when the
    /// message body is empty.
    /// </summary>
    private static string FirstMessageLine(string message)
    {
        var body = SignatureSummaryFormatter.StripSignatureBlock(message);
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith('#')) continue;
            return trimmed.Length > 120 ? trimmed[..120] + "…" : trimmed;
        }
        return string.Empty;
    }

    /// <summary>
    /// Build a single text line for the signature tooltip. White on the
    /// dark background — matches the branch-overflow tooltip's typography
    /// so the two feel consistent when both are visible briefly.
    /// </summary>
    private static TextBlock BuildSignatureLine(FontWeight weight, double size, string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = size,
        FontWeight = weight,
        Margin = new Thickness(0, 1, 0, 1),
    };

    /// <summary>
    /// Format a key fingerprint as <c>XXXX XXXX XXXX XXXX</c> blocks of
    /// four hex chars for readability. SSH fingerprints use a different
    /// shape (algorithm prefix + base64) and are returned unchanged.
    /// </summary>
    private static string FormatFingerprint(string fingerprint)
    {
        var trimmed = fingerprint?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return string.Empty;
        // SSH fingerprint: "SHA256:abcd..." — leave as-is.
        if (trimmed.Contains(':')) return trimmed;
        // GPG: 40-char hex. Group into blocks of 4.
        if (trimmed.All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f')))
        {
            var groups = new List<string>(trimmed.Length / 4 + 1);
            for (var i = 0; i < trimmed.Length; i += 4)
                groups.Add(trimmed.Substring(i, Math.Min(4, trimmed.Length - i)));
            return string.Join(' ', groups).ToUpperInvariant();
        }
        return trimmed;
    }

    private void ShowBranchTooltip(List<BranchLabel> branches, Rect tagRect)
    {
        if (_branchTooltipPopup == null)
        {
            _branchTooltipPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6),
                Child = _branchTooltipPanel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 12,
                    ShadowDepth = 4,
                    Opacity = 0.4
                }
            };

            _branchTooltipPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = border,
                AllowsTransparency = true,
                PlacementTarget = this,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                StaysOpen = true
            };
        }

        // Clear and rebuild branch items
        _branchTooltipPanel!.Children.Clear();

        // Measure to align icons to the right edge of the tooltip
        var tooltipDpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double circleSize = 10;
        const double circleRightMargin = 8;
        const double nameRightMargin = 8;
        double maxNameWidth = 0;
        double maxIconWidth = 0;

        foreach (var branch in branches)
        {
            var nameFormatted = new FormattedText(
                branch.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                12,
                Brushes.White,
                tooltipDpi);
            nameFormatted.SetFontWeight(branch.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal);
            maxNameWidth = Math.Max(maxNameWidth, nameFormatted.Width);

            var iconTextMeasure = "";
            if (branch.IsLocal) iconTextMeasure += ComputerIcon;
            if (branch.IsLocal && branch.IsRemote) iconTextMeasure += " ";
            if (branch.IsRemote) iconTextMeasure += CloudIcon;

            if (!string.IsNullOrEmpty(iconTextMeasure))
            {
                var iconFormatted = new FormattedText(
                    iconTextMeasure,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    IconTypeface,
                    11,
                    Brushes.White,
                    tooltipDpi);
                maxIconWidth = Math.Max(maxIconWidth, iconFormatted.Width + nameRightMargin);
            }
        }

        double rowWidth = circleSize + circleRightMargin + maxNameWidth + maxIconWidth;

        foreach (var branch in branches)
        {
            var branchBrush = ResolveBranchColor(branch.Name);

            // Create a row: colored circle + name (left) + icons (right)
            var row = new Grid
            {
                Margin = new Thickness(4, 3, 4, 3),
                Width = rowWidth
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Colored circle
            var circle = new System.Windows.Shapes.Ellipse
            {
                Width = circleSize,
                Height = circleSize,
                Fill = branchBrush,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(circle, 0);
            row.Children.Add(circle);

            // Branch name
            var nameText = new TextBlock
            {
                Text = branch.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = branch.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, nameRightMargin, 0)
            };
            Grid.SetColumn(nameText, 1);
            row.Children.Add(nameText);

            // Icons (local/remote)
            var iconText = "";
            if (branch.IsLocal) iconText += ComputerIcon;
            if (branch.IsLocal && branch.IsRemote) iconText += " ";
            if (branch.IsRemote) iconText += CloudIcon;

            if (!string.IsNullOrEmpty(iconText))
            {
                var icons = new TextBlock
                {
                    Text = iconText,
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(icons, 2);
                row.Children.Add(icons);
            }

            _branchTooltipPanel.Children.Add(row);
        }

        _branchTooltipPopup.HorizontalOffset = tagRect.Right + 10;
        _branchTooltipPopup.VerticalOffset = tagRect.Top - 4;
        _branchTooltipPopup.IsOpen = true;
    }

    private void HideBranchTooltip()
    {
        if (_branchTooltipPopup != null)
        {
            _branchTooltipPopup.IsOpen = false;
        }
    }
}
