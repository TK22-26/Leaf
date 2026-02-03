using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Performs three-way merges using DiffPlex for diff computation.
/// Identifies unchanged, auto-mergeable, and conflicting regions.
/// </summary>
public class ThreeWayMergeService : IThreeWayMergeService
{
    /// <inheritdoc/>
    public FileMergeResult PerformMerge(string baseContent, string oursContent, string theirsContent,
        bool ignoreWhitespace = false)
    {
        return PerformMerge(string.Empty, baseContent, oursContent, theirsContent, ignoreWhitespace);
    }

    /// <inheritdoc/>
    public FileMergeResult PerformMerge(string filePath, string baseContent, string oursContent,
        string theirsContent, bool ignoreWhitespace = false)
    {
        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] PerformMerge starting for {filePath}");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = new FileMergeResult { FilePath = filePath };

        // Normalize line endings
        baseContent = NormalizeLineEndings(baseContent);
        oursContent = NormalizeLineEndings(oursContent);
        theirsContent = NormalizeLineEndings(theirsContent);
        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] Normalized in {sw.ElapsedMilliseconds}ms");

        // Split into lines
        var baseLines = SplitLines(baseContent);
        var oursLines = SplitLines(oursContent);
        var theirsLines = SplitLines(theirsContent);
        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] Split: base={baseLines.Length}, ours={oursLines.Length}, theirs={theirsLines.Length} in {sw.ElapsedMilliseconds}ms");

        // Get diffs using DiffPlex
        var differ = new Differ();
        var oursDiff = InlineDiffBuilder.Diff(baseContent, oursContent, ignoreWhitespace);
        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] oursDiff in {sw.ElapsedMilliseconds}ms");
        var theirsDiff = InlineDiffBuilder.Diff(baseContent, theirsContent, ignoreWhitespace);
        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] theirsDiff in {sw.ElapsedMilliseconds}ms");

        // Build change maps: baseLineIndex -> what happened
        var oursChanges = BuildChangeMap(oursDiff);
        var theirsChanges = BuildChangeMap(theirsDiff);
        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] ChangeMaps in {sw.ElapsedMilliseconds}ms");

        // Walk through and build merge regions
        var regions = BuildMergeRegions(baseLines, oursLines, theirsLines, oursChanges, theirsChanges);
        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] BuildMergeRegions returned {regions.Count} regions in {sw.ElapsedMilliseconds}ms");

        foreach (var region in regions)
        {
            result.Regions.Add(region);
        }

        result.CalculateLineNumbers();
        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] PerformMerge complete in {sw.ElapsedMilliseconds}ms, {result.Regions.Count} regions");
        return result;
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string[] SplitLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        return content.Split('\n');
    }

    /// <summary>
    /// Build a map of line changes from a diff result.
    /// Key: line index in the "new" (modified) version
    /// Value: (ChangeType, correspondingBaseLine or -1)
    /// </summary>
    private static Dictionary<int, LineChange> BuildChangeMap(DiffPaneModel diff)
    {
        var changes = new Dictionary<int, LineChange>();
        int lineIndex = 0;

        foreach (var line in diff.Lines)
        {
            changes[lineIndex] = new LineChange
            {
                Type = line.Type,
                Text = line.Text ?? string.Empty,
                Position = line.Position
            };
            lineIndex++;
        }

        return changes;
    }

    private static List<MergeRegion> BuildMergeRegions(
        string[] baseLines,
        string[] oursLines,
        string[] theirsLines,
        Dictionary<int, LineChange> oursChanges,
        Dictionary<int, LineChange> theirsChanges)
    {
        var regions = new List<MergeRegion>();
        int regionIndex = 0;

        // Use DiffPlex's line-by-line diff for accurate three-way merge
        var differ = new Differ();
        var lineChunker = new LineChunker();
        var oursResult = differ.CreateDiffs(string.Join("\n", baseLines), string.Join("\n", oursLines), false, false, lineChunker);
        var theirsResult = differ.CreateDiffs(string.Join("\n", baseLines), string.Join("\n", theirsLines), false, false, lineChunker);

        // Build position-based change tracking
        var oursBlockMap = BuildBlockMap(oursResult.DiffBlocks, baseLines.Length);
        var theirsBlockMap = BuildBlockMap(theirsResult.DiffBlocks, baseLines.Length);

        int baseIdx = 0;
        int oursIdx = 0;
        int theirsIdx = 0;
        int loopCount = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (baseIdx < baseLines.Length || oursIdx < oursLines.Length || theirsIdx < theirsLines.Length)
        {
            loopCount++;
            if (loopCount % 1000 == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] BuildMergeRegions loop {loopCount}: baseIdx={baseIdx}/{baseLines.Length}, oursIdx={oursIdx}/{oursLines.Length}, theirsIdx={theirsIdx}/{theirsLines.Length}, regions={regions.Count}, elapsed={sw.ElapsedMilliseconds}ms");
            }

            // If we've exhausted base lines but still have remaining lines in ours/theirs, break to handle them
            if (baseIdx >= baseLines.Length)
            {
                break;
            }

            // Check if either side has changes at this position
            var oursBlock = oursBlockMap.GetValueOrDefault(baseIdx);
            var theirsBlock = theirsBlockMap.GetValueOrDefault(baseIdx); // BUG FIX: was oursBlockMap

            bool oursChanged = oursIdx < oursLines.Length && oursBlockMap.ContainsKey(baseIdx);
            bool theirsChanged = theirsIdx < theirsLines.Length && theirsBlockMap.ContainsKey(baseIdx);

            if (!oursChanged && !theirsChanged)
            {
                // Unchanged - collect consecutive unchanged lines
                var unchangedLines = new List<string>();
                while (baseIdx < baseLines.Length &&
                       !oursBlockMap.ContainsKey(baseIdx) &&
                       !theirsBlockMap.ContainsKey(baseIdx))
                {
                    if (oursIdx < oursLines.Length)
                        unchangedLines.Add(oursLines[oursIdx]);
                    baseIdx++;
                    oursIdx++;
                    theirsIdx++;
                }

                if (unchangedLines.Count > 0)
                {
                    regions.Add(new MergeRegion
                    {
                        Index = regionIndex++,
                        Type = MergeRegionType.Unchanged,
                        Content = string.Join("\n", unchangedLines)
                    });
                }
            }
            else
            {
                // At least one side has changes
                var (oursChunk, oursDeleted, oursAdvance) = ExtractChunk(oursBlockMap, baseIdx, oursLines, oursIdx);
                var (theirsChunk, theirsDeleted, theirsAdvance) = ExtractChunk(theirsBlockMap, baseIdx, theirsLines, theirsIdx);

                // Get the base content being replaced
                int deletedCount = Math.Max(oursDeleted, theirsDeleted);
                var baseChunk = baseLines.Skip(baseIdx).Take(deletedCount).ToList();

                MergeRegion region;

                if (oursChunk.SequenceEqual(theirsChunk))
                {
                    // False conflict - both sides made identical changes
                    region = new MergeRegion
                    {
                        Index = regionIndex++,
                        Type = MergeRegionType.Unchanged,
                        Content = string.Join("\n", oursChunk)
                    };
                }
                else if (oursDeleted == 0 && oursChunk.Count == 0 && theirsChunk.Count > 0)
                {
                    // Only theirs changed (addition)
                    region = new MergeRegion
                    {
                        Index = regionIndex++,
                        Type = MergeRegionType.TheirsOnly,
                        Content = string.Join("\n", theirsChunk)
                    };
                }
                else if (theirsDeleted == 0 && theirsChunk.Count == 0 && oursChunk.Count > 0)
                {
                    // Only ours changed (addition)
                    region = new MergeRegion
                    {
                        Index = regionIndex++,
                        Type = MergeRegionType.OursOnly,
                        Content = string.Join("\n", oursChunk)
                    };
                }
                else if (!oursBlockMap.ContainsKey(baseIdx) && theirsBlockMap.ContainsKey(baseIdx))
                {
                    // Only theirs has changes at this position
                    region = new MergeRegion
                    {
                        Index = regionIndex++,
                        Type = MergeRegionType.TheirsOnly,
                        Content = string.Join("\n", theirsChunk.Count > 0 ? theirsChunk : baseChunk)
                    };
                }
                else if (oursBlockMap.ContainsKey(baseIdx) && !theirsBlockMap.ContainsKey(baseIdx))
                {
                    // Only ours has changes at this position
                    region = new MergeRegion
                    {
                        Index = regionIndex++,
                        Type = MergeRegionType.OursOnly,
                        Content = string.Join("\n", oursChunk.Count > 0 ? oursChunk : baseChunk)
                    };
                }
                else
                {
                    // True conflict - both sides changed differently
                    region = new MergeRegion
                    {
                        Index = regionIndex++,
                        Type = MergeRegionType.Conflict,
                        OursLines = oursChunk.Count > 0 ? oursChunk : [.. baseChunk],
                        TheirsLines = theirsChunk.Count > 0 ? theirsChunk : [.. baseChunk]
                    };
                }

                regions.Add(region);

                baseIdx += deletedCount > 0 ? deletedCount : 1;
                oursIdx += oursAdvance > 0 ? oursAdvance : 1;
                theirsIdx += theirsAdvance > 0 ? theirsAdvance : 1;
            }

            // Safety check to prevent infinite loop
            if (baseIdx >= baseLines.Length && oursIdx >= oursLines.Length && theirsIdx >= theirsLines.Length)
                break;
        }

        // Handle any remaining lines
        var oursRemaining = oursIdx < oursLines.Length ? oursLines.Skip(oursIdx).ToList() : [];
        var theirsRemaining = theirsIdx < theirsLines.Length ? theirsLines.Skip(theirsIdx).ToList() : [];

        System.Diagnostics.Debug.WriteLine($"[ThreeWayMerge] Remaining lines: ours={oursRemaining.Count}, theirs={theirsRemaining.Count}");

        if (oursRemaining.Count > 0 && theirsRemaining.Count > 0)
        {
            // Both have remaining - check if they're the same
            if (oursRemaining.SequenceEqual(theirsRemaining))
            {
                regions.Add(new MergeRegion
                {
                    Index = regionIndex++,
                    Type = MergeRegionType.Unchanged,
                    Content = string.Join("\n", oursRemaining)
                });
            }
            else
            {
                // Different remaining content - conflict
                regions.Add(new MergeRegion
                {
                    Index = regionIndex++,
                    Type = MergeRegionType.Conflict,
                    OursLines = oursRemaining,
                    TheirsLines = theirsRemaining
                });
            }
        }
        else if (oursRemaining.Count > 0)
        {
            regions.Add(new MergeRegion
            {
                Index = regionIndex++,
                Type = MergeRegionType.OursOnly,
                Content = string.Join("\n", oursRemaining)
            });
        }
        else if (theirsRemaining.Count > 0)
        {
            regions.Add(new MergeRegion
            {
                Index = regionIndex++,
                Type = MergeRegionType.TheirsOnly,
                Content = string.Join("\n", theirsRemaining)
            });
        }

        return MergeConsecutiveRegions(regions);
    }

    private static Dictionary<int, DiffBlock> BuildBlockMap(IList<DiffPlex.Model.DiffBlock> blocks, int baseLength)
    {
        var map = new Dictionary<int, DiffBlock>();
        foreach (var block in blocks)
        {
            // DeleteStartA is the position in the old (base) text
            if (block.DeleteStartA >= 0)
            {
                map[block.DeleteStartA] = new DiffBlock
                {
                    DeleteStart = block.DeleteStartA,
                    DeleteCount = block.DeleteCountA,
                    InsertStart = block.InsertStartB,
                    InsertCount = block.InsertCountB
                };
            }
        }
        return map;
    }

    private static (List<string> chunk, int deleted, int inserted) ExtractChunk(
        Dictionary<int, DiffBlock> blockMap,
        int basePos,
        string[] modifiedLines,
        int modifiedPos)
    {
        if (!blockMap.TryGetValue(basePos, out var block))
            return ([], 0, 0);

        var chunk = new List<string>();
        for (int i = 0; i < block.InsertCount && block.InsertStart + i < modifiedLines.Length; i++)
        {
            chunk.Add(modifiedLines[block.InsertStart + i]);
        }

        return (chunk, block.DeleteCount, block.InsertCount);
    }

    /// <summary>
    /// Merge consecutive regions of the same type to reduce region count.
    /// </summary>
    private static List<MergeRegion> MergeConsecutiveRegions(List<MergeRegion> regions)
    {
        if (regions.Count <= 1)
            return regions;

        var merged = new List<MergeRegion>();
        MergeRegion? current = null;

        foreach (var region in regions)
        {
            if (current == null)
            {
                current = region;
                continue;
            }

            // Only merge non-conflict regions of the same type
            if (current.Type == region.Type && !current.IsConflict && !region.IsConflict)
            {
                // Merge content
                if (!string.IsNullOrEmpty(current.Content) && !string.IsNullOrEmpty(region.Content))
                {
                    current.Content = current.Content + "\n" + region.Content;
                }
                else if (!string.IsNullOrEmpty(region.Content))
                {
                    current.Content = region.Content;
                }
            }
            else
            {
                merged.Add(current);
                current = region;
            }
        }

        if (current != null)
            merged.Add(current);

        // Re-index
        for (int i = 0; i < merged.Count; i++)
            merged[i].Index = i;

        return merged;
    }

    private class LineChange
    {
        public ChangeType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public int? Position { get; set; }
    }

    private class DiffBlock
    {
        public int DeleteStart { get; set; }
        public int DeleteCount { get; set; }
        public int InsertStart { get; set; }
        public int InsertCount { get; set; }
    }
}
