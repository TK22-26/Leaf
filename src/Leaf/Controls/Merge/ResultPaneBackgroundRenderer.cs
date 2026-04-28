#nullable enable
using System.Windows;
using System.Windows.Media;
using Leaf.Models.Merge;
using Leaf.TextEdit.Rendering;

namespace Leaf.Controls.Merge;

/// <summary>
/// AvalonEdit background renderer for the merge editor's Result pane.
/// Reads its per-line classification from a shared <see cref="MergeDisplayMap"/>
/// built once on <see cref="MergeDocument"/>, then maps each
/// <see cref="MergeLineKind"/> to a palette brush. Same map drives the
/// gutter margin and inline-element generator — single source of truth.
/// </summary>
/// <remarks>
/// Tinting strategy per line kind:
/// <list type="bullet">
/// <item><b>Marker rows</b> get section-tinted chrome painted by Draw
///   itself (Open=neutral toolbar + ours-strong OURS strip, Base=base-strong,
///   Equals=theirs-strong, Close=neutral). Marker chrome is rendered from
///   the LIVE displayed text (a literal <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>
///   typed into Manual text would otherwise paint a fake toolbar).</item>
/// <item><b>Unresolved content</b> uses the side's BgSubtle tint per
///   <see cref="MergeLineKind"/>: ours=blue, base=grey, theirs=green.</item>
/// <item><b>Resolved content</b> uses the side's BgSubtle tint matching
///   the kind chosen — AcceptOurs blue, AcceptTheirs green, AcceptBoth
///   per-line by section, Manual gets the generic resolved-overlay.</item>
/// </list>
/// </remarks>
public sealed class ResultPaneBackgroundRenderer : IBackgroundRenderer
{
    private readonly Func<MergeDocument?> _getDocument;
    private readonly Func<IReadOnlyDictionary<int, ResolutionState>?> _getRangeStates;

    private readonly Brush _oursBg;
    private readonly Brush _theirsBg;
    private readonly Brush _baseBg;
    private readonly Brush _resolvedBg;
    // Marker-row chrome. Open ("toolbar") and Close ("END") rows paint
    // NEUTRAL (Surface-3) — toolbar is a command surface, END just closes
    // the conflict. Only Base and Equals carry their section's tint.
    private readonly Brush _neutralMarkerBg;
    private readonly Brush _theirsMarkerBg;
    private readonly Brush _baseMarkerBg;
    private readonly Brush _oursStrongBg;
    private readonly Brush _markerBorder;

    public ResultPaneBackgroundRenderer(
        Func<MergeDocument?> getDocument,
        Func<IReadOnlyDictionary<int, ResolutionState>?> getRangeStates)
    {
        _getDocument = getDocument;
        _getRangeStates = getRangeStates;
        _oursBg = MergePaletteResources.ResolveFrozenBrush("Merge.Ours.BgSubtle.Color");
        _theirsBg = MergePaletteResources.ResolveFrozenBrush("Merge.Theirs.BgSubtle.Color");
        _baseBg = MergePaletteResources.ResolveFrozenBrush("Merge.Base.BgSubtle.Color");
        _resolvedBg = MergePaletteResources.ResolveFrozenBrush("Merge.State.Resolved.Overlay.Color");
        _neutralMarkerBg = MergePaletteResources.ResolveFrozenBrush("Merge.Surface.3.Color");
        _theirsMarkerBg = MergePaletteResources.ResolveFrozenBrush("Merge.Theirs.BgStrong.Color");
        _baseMarkerBg = MergePaletteResources.ResolveFrozenBrush("Merge.Base.BgStrong.Color");
        _oursStrongBg = MergePaletteResources.ResolveFrozenBrush("Merge.Ours.BgStrong.Color");
        _markerBorder = MergePaletteResources.ResolveFrozenBrush("Merge.Border.Subtle.Color");
    }

    public KnownLayer Layer => KnownLayer.Background;

    // Cached display map keyed by document version. Avoids the per-Draw
    // O(N) walk + O(N) Brush?[] allocation that would otherwise occur on
    // every scroll tick. The cache invalidates whenever Document.Version
    // changes (text mutation) or the bound MergeDocument reference flips.
    private MergeDisplayMap? _cachedMap;
    private object? _cachedVersion;
    private MergeDocument? _cachedDocument;
    private IReadOnlyDictionary<int, ResolutionState>? _cachedStates;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var mergeDoc = _getDocument();
        if (mergeDoc is null) return;
        var docModel = textView.Document;
        if (docModel is null) return;
        textView.EnsureVisualLines();
        if (!textView.VisualLinesValid) return;

        var states = _getRangeStates();
        var map = GetOrBuildDisplayMap(docModel, mergeDoc, states);

        var width = textView.ActualWidth;
        // Paint past the visible viewport edges so a partially-scrolled
        // strip doesn't show a visible seam at the right margin.
        var paintWidth = Math.Max(width, textView.RenderSize.Width);

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            var y = visualLine.VisualTop - textView.VerticalOffset;
            var rect = new Rect(0, y, paintWidth, visualLine.Height);

            var displayLine = map.GetLine(lineNumber);

            // Marker rows: paint chrome via the dedicated path. Empty-base
            // BASE markers (next line is `=======`) render as a 1 px collapsed
            // strip via the inline generator; skip painting any chrome here
            // so ours- and theirs-content blocks visually meet across the
            // hairline.
            if (IsMarkerKind(displayLine.Kind))
            {
                if (displayLine.Kind == MergeLineKind.BaseMarker
                    && IsBaseEmptyAtLine(map, lineNumber))
                {
                    continue;
                }
                PaintMarkerChrome(drawingContext, displayLine.Kind, y, paintWidth, visualLine.Height, rect);
                continue;
            }

            var brush = BrushForKind(displayLine.Kind);
            if (brush is null) continue;
            drawingContext.DrawRectangle(brush, pen: null, rect);
        }
    }

    private MergeDisplayMap GetOrBuildDisplayMap(
        Leaf.TextEdit.Document.TextDocument docModel,
        MergeDocument mergeDoc,
        IReadOnlyDictionary<int, ResolutionState>? states)
    {
        // Cache on (Document.Version, MergeDocument identity, RangeStates
        // identity). Document.Version is reference-equal until the next
        // text mutation; MergeDocument and RangeStates are reassigned
        // whenever the bound file or VM changes. RangeStates can also be
        // mutated in place — those mutations route through ResultPane's
        // explicit InvalidateLayer call after Refresh(), which doesn't
        // invalidate this cache by itself, so additionally bump on
        // Document.Version which Document.Text writes always change.
        var version = docModel.Version;
        if (_cachedMap is not null
            && ReferenceEquals(version, _cachedVersion)
            && ReferenceEquals(mergeDoc, _cachedDocument)
            && ReferenceEquals(states, _cachedStates)
            && _cachedMap.LineCount == docModel.LineCount)
        {
            return _cachedMap;
        }
        _cachedMap = mergeDoc.BuildDisplayMap(docModel.LineCount, states);
        _cachedVersion = version;
        _cachedDocument = mergeDoc;
        _cachedStates = states;
        return _cachedMap;
    }

    private static bool IsMarkerKind(MergeLineKind kind) => kind switch
    {
        MergeLineKind.OpenMarker or MergeLineKind.BaseMarker
            or MergeLineKind.EqualsMarker or MergeLineKind.CloseMarker => true,
        _ => false,
    };

    private static bool IsBaseEmptyAtLine(MergeDisplayMap map, int lineNumber)
    {
        // The inline generator collapses an empty-base marker (the row
        // followed immediately by `=======`) to a 1 px strip. Detect the
        // same condition by reading the NEXT line's kind from the map.
        var next = map.GetLine(lineNumber + 1);
        return next.Kind == MergeLineKind.EqualsMarker;
    }

    private void PaintMarkerChrome(
        DrawingContext drawingContext,
        MergeLineKind kind,
        double y,
        double paintWidth,
        double height,
        Rect fullRect)
    {
        if (kind == MergeLineKind.OpenMarker)
        {
            // Two-band chrome: top portion neutral (toolbar surface),
            // bottom OursRowHeight slice ours-strong-tinted (full-width
            // OURS section header — the inline element only spans the
            // toolbar's natural width, so the ours-tint must come from
            // the renderer to span the whole row).
            double oursStripHeight = Math.Min(ConflictMarkerInlineGenerator.OursRowHeight, height);
            double topPortion = height - oursStripHeight;
            if (topPortion > 0)
            {
                drawingContext.DrawRectangle(_neutralMarkerBg, pen: null,
                    new Rect(0, y, paintWidth, topPortion));
            }
            drawingContext.DrawRectangle(_oursStrongBg, pen: null,
                new Rect(0, y + topPortion, paintWidth, oursStripHeight));
        }
        else
        {
            var markerBg = kind switch
            {
                MergeLineKind.BaseMarker => _baseMarkerBg,
                MergeLineKind.EqualsMarker => _theirsMarkerBg,
                MergeLineKind.CloseMarker => _neutralMarkerBg,
                _ => _neutralMarkerBg,
            };
            drawingContext.DrawRectangle(markerBg, pen: null, fullRect);
        }
        drawingContext.DrawRectangle(_markerBorder, pen: null,
            new Rect(0, y, paintWidth, 1));
        drawingContext.DrawRectangle(_markerBorder, pen: null,
            new Rect(0, y + height - 1, paintWidth, 1));
    }

    private Brush? BrushForKind(MergeLineKind kind) => kind switch
    {
        MergeLineKind.UnresolvedOurs or MergeLineKind.ResolvedOurs => _oursBg,
        MergeLineKind.UnresolvedBase => _baseBg,
        MergeLineKind.UnresolvedTheirs or MergeLineKind.ResolvedTheirs => _theirsBg,
        MergeLineKind.ResolvedManual => _resolvedBg,
        _ => null,
    };
}
