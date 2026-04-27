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
    private readonly Func<MergeDocument?> _getDocument;
    private readonly Func<ICommand?> _getAcceptOurs;
    private readonly Func<ICommand?> _getAcceptTheirs;
    private readonly Func<ICommand?> _getAcceptBoth;
    private readonly Func<ICommand?> _getCompare;

    public ConflictMarkerInlineGenerator(
        Func<MergeDocument?> getDocument,
        Func<ICommand?> getAcceptOurs,
        Func<ICommand?> getAcceptTheirs,
        Func<ICommand?> getAcceptBoth,
        Func<ICommand?> getCompare)
    {
        _getDocument = getDocument;
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
        UIElement element = kind switch
        {
            MarkerKind.Open => BuildOpenerToolbar(rangeIndex),
            MarkerKind.Base => BuildSeparator("BASE"),
            MarkerKind.Equals => BuildSeparator("THEIRS"),
            MarkerKind.Close => BuildSeparator(null),
            _ => BuildSeparator(null),
        };

        // Force a measure pass so the element's DesiredSize is populated
        // before AvalonEdit asks for it via TextEmbeddedObjectMetrics.Format.
        // Without this the inline run reports 0×0 and the editor lays the
        // line out as if the marker text were still present.
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Document length = full marker line content. The editor renders
        // the inline element in place of the line's text.
        return new InlineObjectElement(line.Length, element);
    }

    private int FindRangeIndexForLine(int lineNumber)
    {
        var mergeDoc = _getDocument();
        if (mergeDoc is null) return -1;
        for (int i = 0; i < mergeDoc.Ranges.Count; i++)
        {
            var range = mergeDoc.Ranges[i];
            if (!range.IsConflicting) continue;
            if (lineNumber >= range.ResultMarkedRange.StartLine
                && lineNumber < range.ResultMarkedRange.EndLineExclusive)
            {
                return i;
            }
        }
        return -1;
    }

    private FrameworkElement BuildOpenerToolbar(int rangeIndex)
    {
        // Toolbar = surface-3 pill containing 3 side-tinted accept buttons
        // (Ours blue / Theirs green / Both amber) and a compact text link
        // for Compare. Mirrors VS Code's merge-editor pattern of giving the
        // primary actions clear chip-shaped affordances and the secondary
        // action (Compare) less visual weight.
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        stack.Children.Add(BuildAcceptPill(
            "Accept Ours", _getAcceptOurs(), rangeIndex,
            "Merge.Ours.BgSubtle.Color",
            "Merge.Ours.BgStrong.Color",
            "Merge.Ours.Border.Color",
            "Merge.Ours.Text.Color",
            "Merge.CodeLens.Inline.AcceptOurs"));
        stack.Children.Add(BuildAcceptPill(
            "Accept Theirs", _getAcceptTheirs(), rangeIndex,
            "Merge.Theirs.BgSubtle.Color",
            "Merge.Theirs.BgStrong.Color",
            "Merge.Theirs.Border.Color",
            "Merge.Theirs.Text.Color",
            "Merge.CodeLens.Inline.AcceptTheirs"));
        stack.Children.Add(BuildAcceptPill(
            "Accept Both", _getAcceptBoth(), rangeIndex,
            "Merge.State.Manual.Color",
            "Merge.State.Manual.Color",
            "Merge.State.Manual.Color",
            "Merge.Text.OnAccent.Color",
            "Merge.CodeLens.Inline.AcceptBoth",
            // "Both" pill carries solid manual-amber bg straight away —
            // it's the merge-flavoured action so the visual weight matches
            // its semantic seriousness.
            solidFill: true));
        stack.Children.Add(BuildCompareLink(rangeIndex));

        // Outer toolbar: surface-3 pill with subtle 1 px hairline border
        // matching the existing card / pane chrome treatment.
        var border = new Border
        {
            Background = MergePaletteResources.ResolveFrozenBrush("Merge.Surface.3.Color"),
            BorderBrush = MergePaletteResources.ResolveFrozenBrush("Merge.Border.Subtle.Color"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 2, 0, 2),
            Child = stack,
        };
        return border;
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
        string automationId, bool solidFill = false)
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
            Background = solidFill ? hoverBg : normalBg,
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
        // Hover swap: solidFill pills already start at the strong colour,
        // so they get a slight Surface.4 hover overlay instead of the
        // normal-to-strong swap.
        button.MouseEnter += (_, _) =>
        {
            button.Background = hoverBg;
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = solidFill ? hoverBg : normalBg;
        };
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
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (!string.IsNullOrEmpty(caption))
        {
            var label = new TextBlock
            {
                Text = caption,
                FontSize = MergePaletteResources.Resolve<double>("Merge.Type.Caption.Size"),
                FontFamily = MergePaletteResources.Resolve<FontFamily>("Merge.FontFamily.Chrome"),
                Foreground = MergePaletteResources.ResolveFrozenBrush("Merge.Text.Tertiary.Color"),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);
        }

        var rule = new Border
        {
            Height = 1,
            Background = MergePaletteResources.ResolveFrozenBrush("Merge.Border.Subtle.Color"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(rule, 1);
        grid.Children.Add(rule);
        return grid;
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
