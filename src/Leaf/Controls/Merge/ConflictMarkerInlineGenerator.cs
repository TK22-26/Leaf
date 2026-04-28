#nullable enable
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Helpers;
using Leaf.Models.Merge;
using Leaf.TextEdit.Rendering;

namespace Leaf.Controls.Merge;

/// <summary>
/// AvalonEdit element generator that REPLACES each zdiff3 conflict-marker
/// line in the result pane with an inline UI affordance — matching VS Code's
/// merge editor where users see a CodeLens-style action toolbar in place of
/// the literal <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> / <c>=======</c> /
/// <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> text.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: previously a separate <c>CodeLensActionBar</c> Canvas
/// floated <em>above</em> the editor at <c>(StartLine - 1) * lineHeight - BarHeight</c>,
/// overlapping the surrounding code text. Phantom-line-style fidelity
/// requires the inline content to be PART of the editor's text-run
/// pipeline so the line itself grows to fit the toolbar; AvalonEdit's
/// <see cref="VisualLineElementGenerator"/> + <see cref="InlineObjectElement"/>
/// is the supported hook for exactly this.
/// </para>
/// <para>
/// Replacement strategy:
/// <list type="bullet">
/// <item><c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> opener → 4 link buttons
/// <em>Accept Ours · Accept Theirs · Accept Both · Compare</em>.</item>
/// <item><c>|||||||</c> base separator → thin horizontal divider with
/// <em>BASE</em> caption (zdiff3 only).</item>
/// <item><c>=======</c> separator → thin divider with
/// <em>THEIRS</em> caption.</item>
/// <item><c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> closer → thin divider; no caption.</item>
/// </list>
/// </para>
/// <para>
/// Locating which conflict a marker line belongs to: the generator holds a
/// reference to <see cref="MergeDocument"/> via the <see cref="Document"/>
/// dependency property and uses each <see cref="ModifiedBaseRange.ResultMarkedRange"/>
/// to map document line numbers to range indices. The opener is always at
/// <c>ResultMarkedRange.StartLine</c>; the closer at
/// <c>ResultMarkedRange.EndLineExclusive - 1</c>; the separator(s) somewhere
/// between, located by content prefix at generation time.
/// </para>
/// </remarks>
public sealed class ConflictMarkerInlineGenerator : VisualLineElementGenerator
{
    /// <summary>
    /// Exact height of the OURS section-header row stacked beneath the
    /// opener toolbar. Exposed so <see cref="ResultPaneBackgroundRenderer"/>
    /// can paint a full-width ours-tinted strip across that bottom slice
    /// of the Open-marker visual line — the inline element itself only
    /// spans the toolbar's natural width, so without a renderer-painted
    /// strip the OURS row only shows tinted up to the toolbar's right edge.
    /// </summary>
    internal const double OursRowHeight = 22.0;

    private readonly Func<MergeDocument?> _getDocument;
    private readonly Func<IReadOnlyDictionary<int, ResolutionState>?> _getStates;
    private readonly Func<ICommand?> _getAcceptOurs;
    private readonly Func<ICommand?> _getAcceptTheirs;
    private readonly Func<ICommand?> _getAcceptBoth;
    private readonly Func<ICommand?> _getCompare;

    public ConflictMarkerInlineGenerator(
        Func<MergeDocument?> getDocument,
        Func<IReadOnlyDictionary<int, ResolutionState>?> getStates,
        Func<ICommand?> getAcceptOurs,
        Func<ICommand?> getAcceptTheirs,
        Func<ICommand?> getAcceptBoth,
        Func<ICommand?> getCompare)
    {
        _getDocument = getDocument;
        _getStates = getStates;
        _getAcceptOurs = getAcceptOurs;
        _getAcceptTheirs = getAcceptTheirs;
        _getAcceptBoth = getAcceptBoth;
        _getCompare = getCompare;
    }

    /// <summary>
    /// Returns the offset of the next conflict-marker line at or after
    /// <paramref name="startOffset"/>, or -1 if none. Walks the document
    /// line-by-line scanning for the four marker prefixes.
    /// </summary>
    /// <remarks>
    /// AvalonEdit's contract requires the returned offset to be
    /// <c>&gt;= startOffset</c>. The whole-line replacement strategy means
    /// we can only act on a marker line when <paramref name="startOffset"/>
    /// is at or before that line's beginning — once the editor has walked
    /// past a line's start while constructing visual elements (e.g. after
    /// consuming our own InlineObjectElement) the same line is no longer
    /// eligible. Skip any line whose <c>Offset &lt; startOffset</c>.
    /// </remarks>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var doc = CurrentContext.Document;
        if (doc is null) return -1;
        var startLine = doc.GetLineByOffset(startOffset);
        for (var line = startLine; line is not null; line = line.NextLine)
        {
            if (line.Offset < startOffset)
            {
                // We've already walked past this line's start; can't insert
                // a whole-line element here. Continue searching forward.
                continue;
            }
            var text = doc.GetText(line.Offset, line.Length);
            if (IsConflictMarker(text))
            {
                // Whole-line replacement: signal interest at the line's start.
                return line.Offset;
            }
        }
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var doc = CurrentContext.Document;
        if (doc is null) return null;
        var line = doc.GetLineByOffset(offset);
        if (line is null) return null;
        // Only replace at the LINE start. If the editor split a previous
        // generator's interest mid-line, ignore.
        if (offset != line.Offset) return null;

        var text = doc.GetText(line.Offset, line.Length);
        var kind = ClassifyMarker(text);
        if (kind == MarkerKind.None) return null;

        var rangeIndex = FindRangeIndexForLine(line.LineNumber);
        // When the base section is empty (zdiff3 still emits a `||||||| base`
        // marker followed immediately by `=======`) the BASE caption row is
        // visual noise — it introduces a section that has no content. Drop
        // the caption to a 1 px collapsed strip so the user sees ours-content
        // flow straight into theirs-content with only a hairline divider.
        bool baseEmpty = kind == MarkerKind.Base
            && rangeIndex >= 0
            && IsBaseEmptyForRangeIndex(rangeIndex);
        UIElement element = kind switch
        {
            MarkerKind.Open => BuildOpenerToolbar(rangeIndex),
            MarkerKind.Base => baseEmpty ? BuildCollapsedMarker() : BuildSeparator("BASE"),
            MarkerKind.Equals => BuildSeparator("THEIRS"),
            // Without a caption the close marker collapses to a 1 px rule
            // inside an otherwise empty visual line — the user sees an
            // unexplained line-number with no content. "END" mirrors the
            // BASE / THEIRS captions so the affordance reads as a deliberate
            // section divider.
            MarkerKind.Close => BuildSeparator("END"),
            _ => BuildSeparator(null),
        };

        // Force a measure pass so the element's DesiredSize is populated
        // before AvalonEdit asks for it via TextEmbeddedObjectMetrics.Format.
        // Without this the inline run reports 0×0 and the editor lays the
        // line out as if the marker text were still present.
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Vertically center the line number in the gutter for this taller-
        // than-text-line visual line. AvalonEdit reads the baseline via
        // TextBlock.GetBaselineOffset(element); for arbitrary FrameworkElements
        // that returns NaN, falling back to desiredSize.Height (the bottom),
        // which makes the line number bottom-aligned and visually offset.
        // Setting the attached property explicitly to the element's mid-
        // height puts the baseline at the visual line's centre, which the
        // line-number margin then tracks.
        TextBlock.SetBaselineOffset(element, element.DesiredSize.Height * 0.7);

        // Document length = full marker line content. The editor renders
        // the inline element in place of the line's text.
        return new InlineObjectElement(line.Length, element);
    }

    // Cached MergeDisplayMap covering the full result-pane document. Built
    // on the first ConstructElement call after the document text changes
    // (detected by Document.Version reference identity). Reading the map
    // for marker→rangeIndex lookup avoids the per-call O(N) doc-text walk
    // + per-call List<ModifiedBaseRange> allocation that ConstructElement
    // would otherwise pay for every marker line on every visual-line
    // construction pass.
    private MergeDisplayMap? _cachedDisplayMap;
    private object? _cachedDocumentVersion;
    private int _cachedLineCount = -1;

    /// <summary>
    /// Map a marker-line's <em>displayed</em> document-line number to the
    /// underlying <see cref="ModifiedBaseRange.Index"/>. Reads from the
    /// shared <see cref="MergeDisplayMap"/> on
    /// <see cref="MergeDocument.BuildDisplayMap"/>, cached per
    /// <see cref="Leaf.TextEdit.Document.TextDocument.Version"/>.
    /// </summary>
    private int FindRangeIndexForLine(int lineNumber)
    {
        var doc = CurrentContext.Document;
        var mergeDoc = _getDocument();
        if (doc is null || mergeDoc is null) return -1;

        EnsureDisplayMapCached(doc, mergeDoc);
        if (_cachedDisplayMap is null) return -1;
        return _cachedDisplayMap.GetLine(lineNumber).RangeIndex;
    }

    private void EnsureDisplayMapCached(Leaf.TextEdit.Document.TextDocument doc, MergeDocument mergeDoc)
    {
        var version = doc.Version;
        if (_cachedDisplayMap is not null
            && ReferenceEquals(version, _cachedDocumentVersion)
            && _cachedLineCount == doc.LineCount)
        {
            return;
        }
        _cachedDisplayMap = mergeDoc.BuildDisplayMap(doc.LineCount, _getStates());
        _cachedDocumentVersion = version;
        _cachedLineCount = doc.LineCount;
    }

    private bool IsBaseEmptyForRangeIndex(int rangeIndex)
    {
        var mergeDoc = _getDocument();
        if (mergeDoc is null) return false;
        if (rangeIndex < 0) return false;
        foreach (var r in mergeDoc.Ranges)
        {
            if (r.Index == rangeIndex) return r.BaseLines.Count == 0;
        }
        return false;
    }

    /// <summary>
    /// Inline element for an empty-base marker line. Renders as a 1 px
    /// transparent strip — the doc line still exists (zdiff3 always emits
    /// the <c>|||||||</c> marker even when base is empty) but visually
    /// collapses so ours- and theirs-content blocks meet across a hairline.
    /// </summary>
    private static FrameworkElement BuildCollapsedMarker()
    {
        return new Border
        {
            Height = 1,
            Background = Brushes.Transparent,
        };
    }

    private FrameworkElement BuildOpenerToolbar(int rangeIndex)
    {
        // Defensive: a stale-display tick or a literal `<<<<<<<` typed into a
        // Manual resolution can land on a marker line that doesn't pair with
        // any unresolved range. Returning a fully-wired toolbar with
        // CommandParameter=-1 would route accept clicks against a phantom
        // range and downstream lookups would either no-op silently or throw.
        // Render an OURS placeholder with no buttons so the user sees the
        // chrome but can't fire stale commands.
        if (rangeIndex < 0)
        {
            return BuildPlaceholderOpener();
        }

        // Two-row layout:
        //   Row 1: [Accept Ours] [Accept Theirs] [Accept Both] [Compare]
        //   Row 2: OURS — section header with ours-tint chip background
        // Earlier the OURS caption sat inline with the buttons on a single
        // toolbar row, but the user expects the section header to look like
        // the BASE / THEIRS / END caption rows that already exist below —
        // those each occupy their own row and are tinted by section. Stacking
        // OURS underneath the toolbar keeps the four marker rows visually
        // consistent (toolbar=neutral, ours-row=blue, base=grey, theirs=green,
        // end=neutral) while preserving a single doc line for the opener so
        // line numbering doesn't drift.
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        toolbar.Children.Add(BuildAcceptPill(
            "Accept Ours", _getAcceptOurs(), rangeIndex,
            "Merge.Ours.BgSubtle.Color",
            "Merge.Ours.BgStrong.Color",
            "Merge.Ours.Border.Color",
            "Merge.Ours.Text.Color",
            "Merge.CodeLens.Inline.AcceptOurs"));
        toolbar.Children.Add(BuildAcceptPill(
            "Accept Theirs", _getAcceptTheirs(), rangeIndex,
            "Merge.Theirs.BgSubtle.Color",
            "Merge.Theirs.BgStrong.Color",
            "Merge.Theirs.Border.Color",
            "Merge.Theirs.Text.Color",
            "Merge.CodeLens.Inline.AcceptTheirs"));
        toolbar.Children.Add(BuildAcceptPill(
            "Accept Both", _getAcceptBoth(), rangeIndex,
            "Merge.Surface.4.Color",
            "Merge.Surface.5.Color",
            "Merge.Border.Strong.Color",
            "Merge.Text.Primary.Color",
            "Merge.CodeLens.Inline.AcceptBoth"));
        toolbar.Children.Add(BuildCompareLink(rangeIndex));

        var toolbarRow = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Child = toolbar,
            Background = Brushes.Transparent,
        };

        // Ours-tinted strip with the OURS caption. The Border carries an
        // explicit ours-strong background so the strip still reads as the
        // ours-section header even though the surrounding doc-line is
        // neutral (the BackgroundRenderer paints Open markers Surface-3 to
        // keep the toolbar surface neutral). Stretches to match the toolbar's
        // natural width via HorizontalAlignment.Stretch on the StackPanel.
        var oursLabel = new TextBlock
        {
            Text = "OURS",
            FontSize = MergePaletteResources.Resolve<double>("Merge.Type.Caption.Size"),
            FontFamily = MergePaletteResources.Resolve<FontFamily>("Merge.FontFamily.Chrome"),
            FontWeight = FontWeights.SemiBold,
            Foreground = MergePaletteResources.ResolveFrozenBrush("Merge.Ours.Text.Color"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The Border's background is intentionally Transparent — the
        // ResultPaneBackgroundRenderer paints a FULL-WIDTH ours-tinted strip
        // across the bottom OursRowHeight slice of the Open-marker visual
        // line, which extends past this inline element's natural width
        // (the user wants the OURS strip to span the entire row, not just
        // the width of the toolbar above it).
        var oursRow = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            Child = oursLabel,
            Height = OursRowHeight,
            Background = Brushes.Transparent,
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        stack.Children.Add(toolbarRow);
        stack.Children.Add(oursRow);

        return stack;
    }

    /// <summary>
    /// Inline element rendered when an Open marker line can't be paired with
    /// an unresolved conflict — typically a stale-render edge case (one
    /// dispatcher tick behind a state mutation) or a literal <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>
    /// inside a Manual resolution. Shows the OURS strip but no buttons, so
    /// the section reads as visible chrome without firing accept commands
    /// against an invalid range index.
    /// </summary>
    private static FrameworkElement BuildPlaceholderOpener()
    {
        var label = new TextBlock
        {
            Text = "OURS",
            FontSize = MergePaletteResources.Resolve<double>("Merge.Type.Caption.Size"),
            FontFamily = MergePaletteResources.Resolve<FontFamily>("Merge.FontFamily.Chrome"),
            FontWeight = FontWeights.SemiBold,
            Foreground = MergePaletteResources.ResolveFrozenBrush("Merge.Ours.Text.Color"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            Child = label,
            Height = OursRowHeight,
            Background = Brushes.Transparent,
        };
    }

    /// <summary>
    /// Render one accept-side pill button — rounded chip with side-tinted
    /// background, hover swap to a stronger fill, and underlined text gone
    /// (we want the chip shape to read as a button, not a hyperlink).
    /// Uses palette tokens exclusively so V8 light/dark theme swap and
    /// custom-palette overrides flow through automatically.
    /// </summary>
    private static FrameworkElement BuildAcceptPill(
        string label, ICommand? cmd, int rangeIndex,
        string normalBgKey, string hoverBgKey, string borderKey, string textKey,
        string automationId)
    {
        var normalBg = MergePaletteResources.ResolveFrozenBrush(normalBgKey);
        var hoverBg = MergePaletteResources.ResolveFrozenBrush(hoverBgKey);
        var borderBrush = MergePaletteResources.ResolveFrozenBrush(borderKey);
        var textBrush = MergePaletteResources.ResolveFrozenBrush(textKey);

        var text = new TextBlock
        {
            Text = label,
            FontSize = MergePaletteResources.Resolve<double>("Merge.Type.Caption.Size"),
            FontFamily = MergePaletteResources.Resolve<FontFamily>("Merge.FontFamily.Chrome"),
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var button = new Button
        {
            Content = text,
            Background = normalBg,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Focusable = false,
            VerticalContentAlignment = VerticalAlignment.Center,
            Command = cmd,
            CommandParameter = rangeIndex,
            // Strip Button's default Fluent template so our Background /
            // BorderBrush / CornerRadius are the visible pill chrome.
            // Without this, WPF's Fluent theme draws its own rounded-corner
            // background OVER our palette-token brush.
            Template = BuildPillTemplate(),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        button.MouseEnter += (_, _) => button.Background = hoverBg;
        button.MouseLeave += (_, _) => button.Background = normalBg;
        return button;
    }

    /// <summary>
    /// Bare-bones pill ControlTemplate so the Button's Background,
    /// BorderBrush, BorderThickness, and Padding are the only visible
    /// pieces of chrome. WPF's default Button template adds its own
    /// rounded-rectangle background and pressed-state overlay, which
    /// fights our palette-token styling and produces an inconsistent
    /// hover reaction.
    /// </summary>
    private static ControlTemplate BuildPillTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private FrameworkElement BuildCompareLink(int rangeIndex)
    {
        var hyperlink = new Hyperlink(new Run("Compare"))
        {
            Foreground = MergePaletteResources.ResolveFrozenBrush("Merge.Text.Secondary.Color"),
            TextDecorations = System.Windows.TextDecorations.Underline,
        };
        var compareCmd = _getCompare();
        if (compareCmd is not null)
        {
            hyperlink.Command = compareCmd;
            hyperlink.CommandParameter = rangeIndex;
        }
        var tb = new TextBlock(hyperlink)
        {
            FontSize = MergePaletteResources.Resolve<double>("Merge.Type.Caption.Size"),
            FontFamily = MergePaletteResources.Resolve<FontFamily>("Merge.FontFamily.Chrome"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        AutomationProperties.SetAutomationId(tb, "Merge.CodeLens.Inline.Compare");
        return tb;
    }

    private static FrameworkElement BuildSeparator(string? caption)
    {
        // Just the caption (no horizontal rule) — the rule is painted by
        // ResultPaneBackgroundRenderer.PaintMarkerLineChrome at full width
        // because an InlineObjectElement can't stretch beyond its DesiredSize.
        // Padding matches the opener toolbar so chrome lines up vertically.
        var label = new TextBlock
        {
            Text = caption ?? string.Empty,
            FontSize = MergePaletteResources.Resolve<double>("Merge.Type.Caption.Size"),
            FontFamily = MergePaletteResources.Resolve<FontFamily>("Merge.FontFamily.Chrome"),
            FontWeight = FontWeights.SemiBold,
            Foreground = MergePaletteResources.ResolveFrozenBrush("Merge.Text.Secondary.Color"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Child = label,
            Background = Brushes.Transparent,
        };
    }

    /// <summary>
    /// Marker-line categories. Exposed as <c>internal</c> so tests can pin
    /// the classification contract without standing up a visual tree.
    /// </summary>
    internal enum MarkerKind { None, Open, Base, Equals, Close }

    internal static MarkerKind ClassifyMarker(string text)
    {
        if (string.IsNullOrEmpty(text)) return MarkerKind.None;
        if (text.StartsWith("<<<<<<<", StringComparison.Ordinal)) return MarkerKind.Open;
        if (text.StartsWith(">>>>>>>", StringComparison.Ordinal)) return MarkerKind.Close;
        if (text.StartsWith("|||||||", StringComparison.Ordinal)) return MarkerKind.Base;
        // Equals separator is exactly seven `=` with no content after.
        if (text == "=======") return MarkerKind.Equals;
        return MarkerKind.None;
    }

    private static bool IsConflictMarker(string text) => ClassifyMarker(text) != MarkerKind.None;
}
