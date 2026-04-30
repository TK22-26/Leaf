namespace Leaf.Models;

/// <summary>
/// One verdict in a bisect session, parsed from <c>git bisect log</c>.
/// Used by the right-pane bisect detail view to show the user-driven
/// history of good/bad/skip clicks; the per-entry SHA + subject lets
/// the UI render each row without a follow-up commit lookup.
/// </summary>
/// <remarks>
/// <para>git's <c>bisect log</c> output looks like:</para>
/// <code>
/// # bad: [&lt;sha&gt;] &lt;subject&gt;          ← bad bookend (from start)
/// # good: [&lt;sha&gt;] &lt;subject&gt;         ← good bookend (from start)
/// git bisect start 'HEAD' '&lt;goodsha&gt;' ← the start command
/// # good: [&lt;sha&gt;] &lt;subject&gt;         ← user verdict comment
/// git bisect good &lt;sha&gt;             ← the verdict command
/// </code>
/// <para>We surface only the <i>verdict</i> entries (the comment + command
/// pair after the start line). The bookends are conveyed elsewhere as
/// <c>BisectState.GoodRef</c> / <c>BadRef</c>.</para>
/// </remarks>
public sealed record BisectLogEntry(
    BisectVerdict Verdict,
    string Sha,
    string ShortSha,
    string Subject);
