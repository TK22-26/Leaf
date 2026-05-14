using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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

    // Per-label branch tooltip — fires on hover over any individual branch
    // chip in the label gutter. Keyed by FullName so re-hovering the same
    // chip is a no-op (otherwise the popup would jitter on every mouse
    // move). Distinct from the overflow `_branchTooltipPopup` above: the
    // overflow case lists every branch on the row, while this one shows
    // a single branch with richer detail (full untruncated name + sync
    // status + checked-out marker).
    private System.Windows.Controls.Primitives.Popup? _singleBranchTooltipPopup;
    private StackPanel? _singleBranchTooltipPanel;
    private string? _singleBranchTooltipKey;

    // Hover-delay machinery for the per-label branch chip tooltip.
    // The popup itself opens instantly once we decide to show it, but the
    // decision is gated on a short hover dwell — matching WPF's standard
    // ToolTip behaviour so a mouse passing through a chip on its way
    // somewhere else doesn't flash the tooltip. The timer interval tracks
    // the OS hover threshold so it adapts to a user's accessibility
    // settings rather than baking in a magic number.
    private DispatcherTimer? _singleBranchTooltipHoverTimer;
    private BranchLabel? _pendingSingleBranchTooltipLabel;
    private Point _pendingSingleBranchTooltipCursor;

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

        // Layout (per design feedback):
        //   1. Title line — tag name, bold, primary text color
        //   2. Annotation message — italic preview, only if the tag has one
        //   3. Additional info — tagger / commit / date, in muted text
        //   4. Signature info — only if signed
        // Sections are separated by margin-bottom on the preceding block.

        // 1. Title
        var titleMargin = new Thickness(0, 0, 0, 6);
        _tagTooltipPanel.Children.Add(new TextBlock
        {
            Text = tag.Name,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = titleMargin,
        });

        // 2. Annotation (italic) — annotated tags only
        if (tag.IsAnnotated && !string.IsNullOrWhiteSpace(tag.Message))
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
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 6),
                    MaxWidth = 380,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
        }

        // 3. Additional info. Annotated tags carry their own tagger /
        // tag-date metadata; lightweight tags don't, so we substitute the
        // target commit's author / date — the closest analogue of "who
        // pinned this tag, when". The target-commit reference (short SHA
        // + subject) is shown for both kinds when we can find the commit
        // in the loaded node set.
        var targetNode = FindTargetNode(tag.TargetSha);
        var mutedBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        var moreMutedBrush = new SolidColorBrush(Color.FromRgb(140, 140, 140));

        if (tag.IsAnnotated)
        {
            var taggerLine = !string.IsNullOrWhiteSpace(tag.TaggerEmail)
                ? $"Tagged by {tag.TaggerName} <{tag.TaggerEmail}>"
                : (!string.IsNullOrWhiteSpace(tag.TaggerName) ? $"Tagged by {tag.TaggerName}" : null);
            if (taggerLine != null)
                _tagTooltipPanel.Children.Add(MakeInfoLine(taggerLine, mutedBrush, 11));

            if (tag.TaggedAt is { } tagged)
                _tagTooltipPanel.Children.Add(MakeInfoLine(FormatDateLine(tagged), moreMutedBrush, 10));

            if (targetNode != null)
                _tagTooltipPanel.Children.Add(MakeInfoLine(FormatTargetLine(targetNode), moreMutedBrush, 10));
        }
        else
        {
            // Lightweight: target commit first (it's the only data we
            // have), then commit author / date as the "who and when".
            if (targetNode != null)
            {
                _tagTooltipPanel.Children.Add(MakeInfoLine(FormatTargetLine(targetNode), mutedBrush, 11));

                var authorLine = !string.IsNullOrWhiteSpace(targetNode.AuthorEmail)
                    ? $"by {targetNode.Author} <{targetNode.AuthorEmail}>"
                    : (!string.IsNullOrWhiteSpace(targetNode.Author) ? $"by {targetNode.Author}" : null);
                if (authorLine != null)
                    _tagTooltipPanel.Children.Add(MakeInfoLine(authorLine, moreMutedBrush, 10));

                if (targetNode.Date != default)
                    _tagTooltipPanel.Children.Add(MakeInfoLine(FormatDateLine(targetNode.Date), moreMutedBrush, 10));
            }
        }

        // 4. Signature info — same wording as the commit signature tooltip
        // so users learn one mental model.
        if (tag.IsSigned)
        {
            // Add a top margin to the previous element so the signature
            // section is visually separated from the additional info.
            if (_tagTooltipPanel.Children.Count > 0
                && _tagTooltipPanel.Children[_tagTooltipPanel.Children.Count - 1] is TextBlock last)
            {
                last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 6);
            }
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
    /// Tooltip "additional info" line — uniform typography for tagger,
    /// target-commit, and date sub-rows.
    /// </summary>
    private static TextBlock MakeInfoLine(string text, Brush foreground, double size) => new()
    {
        Text = text,
        Foreground = foreground,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = size,
        Margin = new Thickness(0, 0, 0, 1),
        MaxWidth = 380,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// Look up the target commit of a tag in the canvas's loaded node set
    /// so the tooltip can show the short SHA + subject (and, for
    /// lightweight tags, the author / date that the tag itself doesn't
    /// carry). Returns null when the target commit hasn't been paginated
    /// in yet — the tooltip degrades gracefully in that case.
    /// </summary>
    private GitTreeNode? FindTargetNode(string targetSha)
    {
        if (string.IsNullOrWhiteSpace(targetSha)) return null;
        if (_segmentNodeLookup.TryGetValue(targetSha, out var node)) return node;
        // Fallback for the brief window after Nodes is set but
        // _segmentNodeLookup hasn't been rebuilt yet (extremely rare).
        var nodes = Nodes;
        if (nodes == null) return null;
        foreach (var n in nodes)
        {
            if (string.Equals(n.Sha, targetSha, StringComparison.OrdinalIgnoreCase))
                return n;
        }
        return null;
    }

    /// <summary>
    /// "→ abc1234  Subject of the commit". Format chosen to read as a
    /// pointer ("the tag points at this") and to keep the SHA visually
    /// scannable next to the ellipsised subject.
    /// </summary>
    private static string FormatTargetLine(GitTreeNode node)
    {
        var shortSha = node.Sha.Length >= 7 ? node.Sha[..7] : node.Sha;
        var subject = string.IsNullOrEmpty(node.MessageShort) ? string.Empty : "  " + node.MessageShort;
        return $"→ {shortSha}{subject}";
    }

    /// <summary>
    /// Combine an absolute local timestamp with a relative-time hint so the
    /// reader gets both "exactly when" and "at-a-glance how-old". Drops
    /// the relative hint for very recent (<60s) or future timestamps where
    /// "0s ago" / "in the future" would just be noise.
    /// </summary>
    private static string FormatDateLine(DateTimeOffset when)
    {
        var local = when.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture);
        var rel = FormatRelativeTime(when);
        return string.IsNullOrEmpty(rel) ? local : $"{local}  ({rel})";
    }

    /// <summary>
    /// Short relative-time label — "3d ago", "2mo ago", "1y ago". Tuned
    /// for a tooltip's small footprint; the absolute timestamp shown
    /// alongside it carries the precision so this side just needs to
    /// communicate "rough age".
    /// </summary>
    private static string FormatRelativeTime(DateTimeOffset when)
    {
        var diff = DateTimeOffset.Now - when;
        if (diff.TotalSeconds < 60) return string.Empty;
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 30) return $"{(int)(diff.TotalDays / 7)}w ago";
        if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)}mo ago";
        return $"{(int)(diff.TotalDays / 365)}y ago";
    }

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

    /// <summary>
    /// Show a rich tooltip for a single hovered branch chip. Used for the
    /// "long branch name" case where the chip itself has been truncated
    /// with an ellipsis — the tooltip presents the full name plus
    /// secondary metadata (sync status, current-branch marker, upstream
    /// reference). Idempotent for the same branch so the popup doesn't
    /// jitter on every mouse-move while the cursor is inside the chip.
    /// </summary>
    /// <remarks>
    /// Layout (all elements inside a Border styled the same as the
    /// signature / tag tooltips so the chrome reads as part of a single
    /// family of overlays):
    /// <list type="number">
    ///   <item><description>Title row — coloured dot keyed off the branch's
    ///     palette colour, then the full branch name in SemiBold.</description></item>
    ///   <item><description>Location row — "Local" / "Remote: origin" /
    ///     "Local · origin, upstream" with monochrome icons.</description></item>
    ///   <item><description>Current-branch marker, only when <see cref="BranchLabel.IsCurrent"/>
    ///     is true. Green accent so it reads at a glance.</description></item>
    /// </list>
    /// </remarks>
    private void ShowSingleBranchTooltip(BranchLabel label, Point cursor)
    {
        var key = label.FullName + "|" + label.IsCurrent;
        if (string.Equals(_singleBranchTooltipKey, key, StringComparison.Ordinal)
            && _singleBranchTooltipPopup is { IsOpen: true })
        {
            return;
        }

        if (_singleBranchTooltipPopup == null)
        {
            _singleBranchTooltipPanel = new StackPanel { Orientation = Orientation.Vertical };
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Child = _singleBranchTooltipPanel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 14,
                    ShadowDepth = 4,
                    Opacity = 0.45,
                },
            };
            _singleBranchTooltipPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = border,
                AllowsTransparency = true,
                PlacementTarget = this,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                StaysOpen = true,
            };
        }

        _singleBranchTooltipPanel!.Children.Clear();

        // 1. Title row — colour dot + full branch name.
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };

        titleRow.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = ResolveBranchColor(label.Name),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        titleRow.Children.Add(new TextBlock
        {
            Text = label.FullName,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = label.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        });

        _singleBranchTooltipPanel.Children.Add(titleRow);

        // 2. Location / sync row — describes whether the branch lives locally,
        // remotely, or on both sides. Multi-remote branches list each remote.
        var locationRow = BuildBranchLocationRow(label);
        if (locationRow != null)
        {
            locationRow.Margin = new Thickness(0, 6, 0, 0);
            _singleBranchTooltipPanel.Children.Add(locationRow);
        }

        // 3. "Current branch" marker — only when checked out. Green so it
        // reads at a glance independent of the rest of the tooltip palette.
        if (label.IsCurrent)
        {
            var currentRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0),
            };
            currentRow.Children.Add(new TextBlock
            {
                Text = "", // CheckMark glyph from Segoe Fluent Icons
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(127, 190, 127)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            currentRow.Children.Add(new TextBlock
            {
                Text = "Current branch",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(127, 190, 127)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _singleBranchTooltipPanel.Children.Add(currentRow);
        }

        _singleBranchTooltipPopup.HorizontalOffset = cursor.X + 14;
        _singleBranchTooltipPopup.VerticalOffset = cursor.Y + 18;
        _singleBranchTooltipPopup.IsOpen = true;
        _singleBranchTooltipKey = key;
    }

    private void HideSingleBranchTooltip()
    {
        // Cancel any pending dwell so a hover that was about to fire
        // doesn't pop the tooltip the frame after the cursor leaves.
        if (_singleBranchTooltipHoverTimer is { IsEnabled: true })
            _singleBranchTooltipHoverTimer.Stop();
        _pendingSingleBranchTooltipLabel = null;

        if (_singleBranchTooltipPopup is { IsOpen: true })
            _singleBranchTooltipPopup.IsOpen = false;
        _singleBranchTooltipKey = null;
    }

    /// <summary>
    /// Hover entry point for a branch chip. Adds a short OS-defined
    /// dwell before the tooltip actually opens (matches WPF's stock
    /// <c>ToolTipService</c> initial-show delay) so a cursor passing
    /// across the label gutter doesn't flash the popup. Already-open
    /// tooltips for the same chip stay put without restarting the
    /// timer; moving to a different chip resets the dwell.
    /// </summary>
    private void RequestSingleBranchTooltip(BranchLabel label, Point cursor)
    {
        var key = label.FullName + "|" + label.IsCurrent;

        // Same chip, popup already visible — nothing to do.
        if (string.Equals(_singleBranchTooltipKey, key, StringComparison.Ordinal)
            && _singleBranchTooltipPopup is { IsOpen: true })
        {
            return;
        }

        // Same chip, dwell already pending — keep the timer running.
        if (_pendingSingleBranchTooltipLabel is { } pending
            && string.Equals(pending.FullName + "|" + pending.IsCurrent, key, StringComparison.Ordinal))
        {
            _pendingSingleBranchTooltipCursor = cursor;
            return;
        }

        // New chip — close any visible tooltip and start a fresh dwell.
        if (_singleBranchTooltipPopup is { IsOpen: true })
        {
            _singleBranchTooltipPopup.IsOpen = false;
            _singleBranchTooltipKey = null;
        }

        _pendingSingleBranchTooltipLabel = label;
        _pendingSingleBranchTooltipCursor = cursor;

        if (_singleBranchTooltipHoverTimer == null)
        {
            _singleBranchTooltipHoverTimer = new DispatcherTimer
            {
                // SystemParameters.MouseHoverTime is the OS-wide
                // "considered hovering" threshold and is what stock
                // tooltips key off, so this matches the rest of the
                // shell's tooltip cadence on the user's machine.
                Interval = SystemParameters.MouseHoverTime,
            };
            _singleBranchTooltipHoverTimer.Tick += OnSingleBranchTooltipDwellElapsed;
        }
        _singleBranchTooltipHoverTimer.Stop();
        _singleBranchTooltipHoverTimer.Start();
    }

    private void OnSingleBranchTooltipDwellElapsed(object? sender, EventArgs e)
    {
        _singleBranchTooltipHoverTimer?.Stop();
        if (_pendingSingleBranchTooltipLabel is not { } label) return;
        var cursor = _pendingSingleBranchTooltipCursor;
        _pendingSingleBranchTooltipLabel = null;
        ShowSingleBranchTooltip(label, cursor);
    }

    /// <summary>
    /// Build the location row for the single-branch tooltip: icons paired
    /// with short text describing where the branch exists (local, on which
    /// remotes). Returns <c>null</c> when there's nothing meaningful to
    /// show — a label with neither <see cref="BranchLabel.IsLocal"/> nor
    /// <see cref="BranchLabel.IsRemote"/> is malformed but we don't crash.
    /// </summary>
    private StackPanel? BuildBranchLocationRow(BranchLabel label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var muted = new SolidColorBrush(Color.FromRgb(188, 188, 188));
        var added = false;

        if (label.IsLocal)
        {
            row.Children.Add(new TextBlock
            {
                Text = ComputerIcon,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 11,
                Foreground = muted,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(new TextBlock
            {
                Text = "Local",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = muted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            added = true;
        }

        if (label.IsRemote)
        {
            if (added)
            {
                row.Children.Add(new TextBlock
                {
                    Text = "  ·  ",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            row.Children.Add(new TextBlock
            {
                Text = CloudIcon,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 11,
                Foreground = muted,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            // List every remote the branch exists on; for a typical single-
            // remote branch this is just "origin". Multi-remote forks
            // show "origin, upstream" so the user can see at a glance
            // which remotes carry the branch.
            var remoteNames = string.Join(", ", label.Remotes.Select(r => r.RemoteName));
            row.Children.Add(new TextBlock
            {
                Text = remoteNames,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = muted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            added = true;
        }

        return added ? row : null;
    }
}
