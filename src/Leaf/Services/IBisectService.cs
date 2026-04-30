using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for driving <c>git bisect</c> from the UI. Mirrors the CLI
/// verb set so the user's mental model maps 1:1 to behaviour. All
/// operations route through <see cref="IGitCommandRunner"/> — bisect
/// invokes hooks (post-checkout) on every step, so LibGit2Sharp is not
/// an option.
/// </summary>
public interface IBisectService
{
    /// <summary>
    /// Begin a bisect session: <c>git bisect start &lt;bad&gt; &lt;good&gt;</c>.
    /// Git checks out a midpoint commit and the caller surfaces the
    /// returned state to the bisect banner.
    /// </summary>
    Task<BisectResult> StartAsync(
        IRepositorySession session,
        string badCommitSha,
        string goodCommitSha,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply a verdict to the current bisect commit and advance the
    /// search. On the converging step git prints
    /// "&lt;sha&gt; is the first bad commit" — the result then has
    /// <see cref="BisectResult.IsTerminating"/> set and the caller stops
    /// asking for verdicts.
    /// </summary>
    Task<BisectResult> MarkAsync(
        IRepositorySession session,
        BisectVerdict verdict,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Abort/reset the bisect: <c>git bisect reset</c>. Restores the
    /// pre-bisect HEAD and clears <c>.git/BISECT_*</c> sentinels.
    /// </summary>
    Task<BisectResult> ResetAsync(
        IRepositorySession session,
        CancellationToken cancellationToken = default);

    /// <summary>True when <c>.git/BISECT_LOG</c> exists, i.e. a bisect is in progress.</summary>
    Task<bool> IsBisectInProgressAsync(
        IRepositorySession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the current bisect state without mutating it. Returns an
    /// inactive <see cref="BisectState"/> when no bisect is in progress.
    /// </summary>
    Task<BisectState> GetStateAsync(
        IRepositorySession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the user-driven verdict history (<c>git bisect log</c>),
    /// most-recent first. Empty list when no bisect is active or no
    /// verdicts have been issued yet (just past the bookends).
    /// </summary>
    Task<IReadOnlyList<BisectLogEntry>> GetLogAsync(
        IRepositorySession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Roll back the most recent verdict. Implementation: capture the
    /// current bisect log, drop the last <c>git bisect good/bad/skip</c>
    /// command line, <c>git bisect reset</c>, then <c>git bisect replay</c>
    /// the truncated log. The bisect emerges in the state it was in
    /// just before that verdict — same checked-out commit as right
    /// before the click, ready for a different verdict.
    /// </summary>
    Task<BisectResult> UndoLastVerdictAsync(
        IRepositorySession session,
        CancellationToken cancellationToken = default);
}
