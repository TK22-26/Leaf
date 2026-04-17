using System.Collections.ObjectModel;
using Leaf.Models;
using Leaf.Models.Merge;
using Leaf.Services.Merge;

namespace Leaf.Services;

/// <summary>
/// Phase 1 adapter — bridges the legacy <see cref="IThreeWayMergeService"/> surface to
/// the new <see cref="IMergeEngine"/> pipeline (<c>git merge-file</c>-backed).
/// Produces <see cref="FileMergeResult"/> / <see cref="MergeRegion"/> shaped output so
/// the existing <c>ConflictResolutionViewModel</c> continues to work until Phase 2c
/// replaces it with the new merge editor that consumes
/// <see cref="MergeDocument"/> directly.
/// </summary>
/// <remarks>
/// The old custom line-by-line merger (384 lines of Myers-diff three-way logic) has
/// been entirely removed. Byte-for-byte parity with <c>git merge</c> now comes from
/// the engine; this class exclusively shapes the result for legacy consumers.
/// </remarks>
public class ThreeWayMergeService : IThreeWayMergeService
{
    private readonly IMergeEngine _engine;

    /// <summary>
    /// Default constructor used when dependency injection is unavailable (convenience
    /// constructor of <c>ConflictResolutionViewModel</c>). Constructs a fresh engine
    /// backed by a fresh <see cref="GitCommandRunner"/>. Production code goes through DI.
    /// </summary>
    public ThreeWayMergeService() : this(new GitMergeFileEngine(new GitCommandRunner()))
    {
    }

    public ThreeWayMergeService(IMergeEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Task<FileMergeResult> PerformMergeAsync(
        string baseContent,
        string oursContent,
        string theirsContent,
        bool ignoreWhitespace = false,
        CancellationToken cancellationToken = default)
        => PerformMergeAsync("<merge>", baseContent, oursContent, theirsContent, ignoreWhitespace, cancellationToken);

    public async Task<FileMergeResult> PerformMergeAsync(
        string filePath,
        string baseContent,
        string oursContent,
        string theirsContent,
        bool ignoreWhitespace = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(baseContent);
        ArgumentNullException.ThrowIfNull(oursContent);
        ArgumentNullException.ThrowIfNull(theirsContent);

        var document = await _engine.MergeAsync(
            filePath,
            baseContent,
            oursContent,
            theirsContent,
            ignoreWhitespace: ignoreWhitespace,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ConvertToLegacy(document);
    }

    /// <summary>
    /// Convert a <see cref="MergeDocument"/> (engine output) into the legacy
    /// <see cref="FileMergeResult"/> shape expected by the Phase 1 UI. Auto-merged
    /// content becomes <see cref="MergeRegionType.Unchanged"/> regions; conflicting
    /// content becomes <see cref="MergeRegionType.Conflict"/> regions carrying the
    /// ours/theirs lines verbatim.
    /// </summary>
    /// <remarks>
    /// Phase 1 intentionally collapses <see cref="MergeRegionType.OursOnly"/> and
    /// <see cref="MergeRegionType.TheirsOnly"/> into <see cref="MergeRegionType.Unchanged"/> —
    /// git merge-file auto-resolves those internally, so they appear as regular auto-merged
    /// content in the output. The legacy UI's <c>HasAutoMergedChanges</c> flag therefore
    /// degrades to <c>false</c> even when such changes did occur upstream. This is acceptable
    /// because the legacy UI is being replaced in Phase 2c; the indicator is informational only.
    /// </remarks>
    internal static FileMergeResult ConvertToLegacy(MergeDocument document)
    {
        var result = new FileMergeResult
        {
            FilePath = document.FilePath,
            Regions = new ObservableCollection<MergeRegion>(),
        };

        var conflictRanges = document.Ranges
            .Where(r => r.IsConflicting)
            .OrderBy(r => r.ResultMarkedRange.StartLine)
            .ToList();

        var mergedLines = document.InitialMergedLines;
        int outputCursor = 0; // 0-based index into mergedLines for the next non-conflict output line.
        int conflictCounter = 0;
        int regionIndex = 0;

        foreach (var range in conflictRanges)
        {
            var markedStart = range.ResultMarkedRange.StartLine - 1; // 0-based
            var markedEndExclusive = range.ResultMarkedRange.EndLineExclusive - 1;

            if (markedStart > outputCursor)
            {
                var slice = new List<string>(markedStart - outputCursor);
                for (int i = outputCursor; i < markedStart; i++)
                {
                    slice.Add(mergedLines[i]);
                }
                result.Regions.Add(new MergeRegion
                {
                    Index = regionIndex++,
                    Type = MergeRegionType.Unchanged,
                    Content = string.Join("\n", slice),
                });
            }

            conflictCounter++;
            result.Regions.Add(new MergeRegion
            {
                Index = regionIndex++,
                ConflictNumber = conflictCounter,
                Type = MergeRegionType.Conflict,
                OursLines = range.OursLines.ToList(),
                TheirsLines = range.TheirsLines.ToList(),
                OursStartLineNumber = range.Ours.IsEmpty ? 1 : range.Ours.StartLine,
                TheirsStartLineNumber = range.Theirs.IsEmpty ? 1 : range.Theirs.StartLine,
            });

            outputCursor = markedEndExclusive;
        }

        if (outputCursor < mergedLines.Count)
        {
            var slice = new List<string>(mergedLines.Count - outputCursor);
            for (int i = outputCursor; i < mergedLines.Count; i++)
            {
                slice.Add(mergedLines[i]);
            }
            result.Regions.Add(new MergeRegion
            {
                Index = regionIndex,
                Type = MergeRegionType.Unchanged,
                Content = string.Join("\n", slice),
            });
        }

        result.CalculateLineNumbers();
        return result;
    }
}
