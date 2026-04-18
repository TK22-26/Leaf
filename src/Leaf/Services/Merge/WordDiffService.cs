#nullable enable
using DiffPlex;
using DiffPlex.Chunkers;
using Leaf.Models.Merge;

namespace Leaf.Services.Merge;

/// <summary>
/// Computes per-token (word-level) diffs inside a conflict region so the
/// custom panes can highlight which parts of a line actually changed.
/// Runs on top of DiffPlex's <see cref="Differ"/> with a
/// whitespace-aware tokenizer that matches Git's <c>--word-diff-regex</c>
/// default.
/// </summary>
/// <remarks>
/// <para>
/// Output shape: <see cref="TokenDiffResult"/>. Each side's lines map to a
/// list of <see cref="TokenSegment"/> describing <see cref="TokenKind.Unchanged"/>
/// / <see cref="TokenKind.Added"/> / <see cref="TokenKind.Removed"/> runs with
/// their 1-based character column range (half-open). Unchanged regions are
/// rendered dim; added/removed regions get the side's accent colour at full
/// intensity.
/// </para>
/// <para>
/// Scope: this service runs <em>only</em> inside conflict blocks (i.e. against
/// <see cref="ModifiedBaseRange.OursLines"/> vs <see cref="ModifiedBaseRange.TheirsLines"/>,
/// or each side vs <see cref="ModifiedBaseRange.BaseLines"/>). Non-conflict
/// regions have no token-level highlight — they're already auto-merged.
/// </para>
/// </remarks>
public sealed class WordDiffService : IWordDiffService
{
    private readonly Differ _differ = new();
    private readonly WordChunker _chunker = new();

    /// <summary>
    /// Compute a token-level diff between two single lines. Returns two lists
    /// of <see cref="TokenSegment"/>: one for the left side, one for the right
    /// side, each describing changed-vs-unchanged runs within the line.
    /// </summary>
    public (IReadOnlyList<TokenSegment> Left, IReadOnlyList<TokenSegment> Right) DiffLines(
        string leftLine, string rightLine)
    {
        ArgumentNullException.ThrowIfNull(leftLine);
        ArgumentNullException.ThrowIfNull(rightLine);

        if (leftLine == rightLine)
        {
            // Empty-both fast path: return empty segment lists rather than a
            // zero-width Unchanged segment, to keep the "EndColumn > StartColumn"
            // invariant consistent across the slow path.
            if (leftLine.Length == 0)
            {
                return (Array.Empty<TokenSegment>(), Array.Empty<TokenSegment>());
            }
            return (
                new[] { new TokenSegment(1, leftLine.Length + 1, TokenKind.Unchanged, leftLine) },
                new[] { new TokenSegment(1, rightLine.Length + 1, TokenKind.Unchanged, rightLine) });
        }

        var result = _differ.CreateDiffs(leftLine, rightLine, ignoreWhiteSpace: false, ignoreCase: false, _chunker);

        // DiffPlex gives us piece-by-piece arrays mirroring the chunker output
        // plus DiffBlocks describing the changes. Reconstruct per-side segment lists.
        var left = BuildSideSegments(result.PiecesOld, result.DiffBlocks, isLeft: true);
        var right = BuildSideSegments(result.PiecesNew, result.DiffBlocks, isLeft: false);
        return (left, right);
    }

    private static IReadOnlyList<TokenSegment> BuildSideSegments(
        IReadOnlyList<string> pieces, IList<DiffPlex.Model.DiffBlock> blocks, bool isLeft)
    {
        var segments = new List<TokenSegment>(pieces.Count);
        int col = 1; // 1-based character column cursor
        int pieceIdx = 0;

        // Convert the blocks into a sparse per-piece change map.
        var changedPieces = new HashSet<int>();
        foreach (var b in blocks)
        {
            var start = isLeft ? b.DeleteStartA : b.InsertStartB;
            var count = isLeft ? b.DeleteCountA : b.InsertCountB;
            for (int i = 0; i < count; i++) changedPieces.Add(start + i);
        }

        while (pieceIdx < pieces.Count)
        {
            var piece = pieces[pieceIdx];
            var kind = changedPieces.Contains(pieceIdx)
                ? (isLeft ? TokenKind.Removed : TokenKind.Added)
                : TokenKind.Unchanged;

            // Coalesce adjacent same-kind pieces into one segment for rendering efficiency.
            int runEnd = pieceIdx + 1;
            while (runEnd < pieces.Count)
            {
                var nextKind = changedPieces.Contains(runEnd)
                    ? (isLeft ? TokenKind.Removed : TokenKind.Added)
                    : TokenKind.Unchanged;
                if (nextKind != kind) break;
                runEnd++;
            }

            var sb = new System.Text.StringBuilder();
            for (int k = pieceIdx; k < runEnd; k++) sb.Append(pieces[k]);
            var text = sb.ToString();
            if (text.Length > 0)
            {
                segments.Add(new TokenSegment(col, col + text.Length, kind, text));
                col += text.Length;
            }
            pieceIdx = runEnd;
        }
        return segments;
    }

    /// <summary>
    /// Splits a line into tokens: runs of word-characters, runs of non-word
    /// characters, and punctuation singletons. Matches Git's default
    /// <c>--word-diff-regex="[A-Za-z_][A-Za-z_0-9]*"</c> philosophy —
    /// identifier-like tokens get a distinct cluster, everything else is
    /// tokenised aggressively so small edits produce small diff changes.
    /// </summary>
    internal sealed class WordChunker : IChunker
    {
        public IReadOnlyList<string> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var tokens = new List<string>(text.Length / 4 + 1);
            int i = 0;
            while (i < text.Length)
            {
                if (IsWordChar(text[i]))
                {
                    int start = i;
                    while (i < text.Length && IsWordChar(text[i])) i++;
                    tokens.Add(text.Substring(start, i - start));
                }
                else if (char.IsWhiteSpace(text[i]))
                {
                    int start = i;
                    while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                    tokens.Add(text.Substring(start, i - start));
                }
                else
                {
                    // Punctuation / symbols are emitted one-at-a-time so edits
                    // of a single bracket / comma surface as a narrow diff.
                    tokens.Add(text[i].ToString());
                    i++;
                }
            }
            return tokens;
        }

        private static bool IsWordChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_';
    }
}

/// <summary>A single line's token-diff segmentation.</summary>
public sealed record TokenLine(string Text, IReadOnlyList<TokenSegment> Segments);

/// <summary>
/// A run of contiguous tokens that share the same <see cref="TokenKind"/>.
/// Columns are 1-based, half-open (<c>[StartColumn, EndColumnExclusive)</c>).
/// </summary>
public sealed record TokenSegment(int StartColumn, int EndColumnExclusive, TokenKind Kind, string Text);

public enum TokenKind
{
    /// <summary>Token is identical in both sides.</summary>
    Unchanged,
    /// <summary>Token was added on this side (right of the diff).</summary>
    Added,
    /// <summary>Token was removed on this side (left of the diff).</summary>
    Removed,
}

/// <summary>Service contract for computing word-level diffs inside conflict regions.</summary>
public interface IWordDiffService
{
    /// <summary>Compute a token-level diff between two single lines.</summary>
    (IReadOnlyList<TokenSegment> Left, IReadOnlyList<TokenSegment> Right) DiffLines(string leftLine, string rightLine);
}
