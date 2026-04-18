#nullable enable
using System.Diagnostics;
using FluentAssertions;
using Leaf.Models.Merge;
using Xunit;
using Xunit.Abstractions;

namespace Leaf.Tests.Models.Merge;

/// <summary>
/// Smoke tests for the composition hot path. The full engine is shell-out
/// based so its cost is dominated by <c>git merge-file</c>; what we exercise
/// here is the in-memory walker — the part that runs on every resolution-state
/// change. A slow walker means sluggish UI on every click. Budget: 20 ms for
/// 500 conflicts in a 5 000-line document. Plenty of headroom above that is
/// fine; we just want to catch pathological O(n²) regressions.
/// </summary>
public class MergeDocumentPerfSmokeTests
{
    private readonly ITestOutputHelper _output;

    public MergeDocumentPerfSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static (MergeDocument doc, Dictionary<int, ResolutionState> states)
        BuildLargeDocument(int conflictCount, int linesPerFiller = 8)
    {
        // Alternate `linesPerFiller` context lines with a 5-line conflict triad.
        // Per-conflict: 1 opener + 1 ours line + 1 separator + 1 theirs line + 1 closer = 5 lines.
        // Plus `linesPerFiller` context lines before each conflict.
        var textBuilder = new System.Text.StringBuilder();
        var ranges = new List<ModifiedBaseRange>(conflictCount);
        var states = new Dictionary<int, ResolutionState>(conflictCount);

        int lineCursor = 1; // 1-based
        for (int i = 0; i < conflictCount; i++)
        {
            for (int j = 0; j < linesPerFiller; j++)
            {
                textBuilder.Append($"ctx-{i}-{j}\n");
                lineCursor++;
            }

            var start = lineCursor;
            textBuilder.Append($"<<<<<<< HEAD\n");
            textBuilder.Append($"ours-{i}\n");
            textBuilder.Append($"=======\n");
            textBuilder.Append($"theirs-{i}\n");
            textBuilder.Append($">>>>>>> incoming\n");
            lineCursor += 5;

            ranges.Add(new ModifiedBaseRange(
                Index: i,
                Base: new LineRange(start, start + 1),
                Ours: new LineRange(start, start + 1),
                Theirs: new LineRange(start, start + 1),
                ResultMarkedRange: new LineRange(start, start + 5),
                BaseLines: Array.Empty<string>(),
                OursLines: new[] { $"ours-{i}" },
                TheirsLines: new[] { $"theirs-{i}" },
                OursDiffs: Array.Empty<DetailedLineRangeMapping>(),
                TheirsDiffs: Array.Empty<DetailedLineRangeMapping>(),
                IsConflicting: true,
                IsOrderRelevant: true,
                OursLabel: "HEAD",
                BaseLabel: null,
                TheirsLabel: "incoming"));

            // Every other range resolved to alternating sides — exercises the walker
            // branch that substitutes lines rather than passes through the initial
            // text. Leaving half unresolved exercises the unresolved/marker path.
            if (i % 2 == 0)
            {
                states[i] = ResolutionState.AcceptOurs.Instance;
            }
            else if (i % 3 == 0)
            {
                states[i] = ResolutionState.AcceptTheirs.Instance;
            }
            // Others left unresolved.
        }
        var initial = textBuilder.ToString();
        var doc = new MergeDocument(
            filePath: "perf.cs",
            baseText: string.Empty,
            oursText: string.Empty,
            theirsText: string.Empty,
            initialMergedText: initial,
            baseLines: Array.Empty<string>(),
            oursLines: Array.Empty<string>(),
            theirsLines: Array.Empty<string>(),
            initialMergedLines: initial.TrimEnd('\n').Split('\n'),
            ranges: ranges,
            lineEnding: "\n",
            hasTrailingNewline: true);
        return (doc, states);
    }

    [Fact]
    public void Compose_500ConflictsUnder_ReasonableBudget()
    {
        var (doc, states) = BuildLargeDocument(conflictCount: 500);

        // Warm the JIT with one call.
        _ = doc.ComposeResolvedText(states);

        var sw = Stopwatch.StartNew();
        const int iterations = 10;
        string? last = null;
        for (int i = 0; i < iterations; i++)
        {
            last = doc.ComposeResolvedText(states);
        }
        sw.Stop();

        var perCallMs = sw.Elapsed.TotalMilliseconds / iterations;
        _output.WriteLine($"Compose average: {perCallMs:F2} ms/call over {iterations} iterations " +
                         $"(conflicts=500, total length={last?.Length:N0})");

        // Plenty of headroom — 50 ms per call on a 500-conflict file means
        // the UI can re-render after every click without perceptible lag.
        // If this ever regresses past 50 ms, investigate before merging.
        perCallMs.Should().BeLessThan(50,
            "composition runs on every RangeStates mutation and must stay interactive");
    }

    [Fact]
    public void Compose_SmallDocument_IsEssentiallyInstant()
    {
        var (doc, states) = BuildLargeDocument(conflictCount: 10);
        _ = doc.ComposeResolvedText(states); // warm

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            _ = doc.ComposeResolvedText(states);
        }
        sw.Stop();

        var perCallMs = sw.Elapsed.TotalMilliseconds / 1000;
        _output.WriteLine($"Small-doc average: {perCallMs:F3} ms/call over 1000 iterations");
        perCallMs.Should().BeLessThan(1.0,
            "small documents should compose in well under a millisecond");
    }
}
