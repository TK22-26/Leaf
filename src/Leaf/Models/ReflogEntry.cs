namespace Leaf.Models;

/// <summary>
/// Classification of a reflog entry derived from the message prefix
/// (e.g. <c>commit:</c>, <c>reset:</c>, <c>rebase (pick):</c>). Drives
/// the filter dropdown on the reflog view and the icon column.
/// </summary>
public enum ReflogOperationType
{
    /// <summary>Everything whose prefix we don't recognize — fallback bucket.</summary>
    Other,

    /// <summary>Regular commit (includes initial commit).</summary>
    Commit,

    /// <summary>Amended commit (<c>commit (amend):</c>).</summary>
    Amend,

    /// <summary>Branch / HEAD checkout.</summary>
    Checkout,

    /// <summary>Reset to a ref or commit.</summary>
    Reset,

    /// <summary>Merge of another ref into the current one.</summary>
    Merge,

    /// <summary>Rebase — any sub-operation (start / pick / finish / abort etc.).</summary>
    Rebase,

    /// <summary>Cherry-pick.</summary>
    CherryPick,

    /// <summary>Revert.</summary>
    Revert,

    /// <summary>Pull (reflog records the combined fetch+merge/rebase).</summary>
    Pull,

    /// <summary>Push.</summary>
    Push,

    /// <summary>Branch created / renamed / deleted.</summary>
    Branch,

    /// <summary>Clone initial.</summary>
    Clone,

    /// <summary>Stash push / pop / apply.</summary>
    Stash,
}

/// <summary>
/// One row in <c>git reflog --all</c> output — the state a ref pointed
/// at, who moved it, and the message git recorded for the move.
/// </summary>
public sealed class ReflogEntry
{
    /// <summary>Full 40-char commit SHA the ref pointed at after this operation.</summary>
    public required string Sha { get; init; }

    /// <summary>Abbreviated SHA for display (first 7 characters of <see cref="Sha"/>).</summary>
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    /// <summary>
    /// Ref whose reflog this entry belongs to — <c>HEAD</c>,
    /// <c>refs/heads/main</c>, <c>refs/remotes/origin/main</c>, etc.
    /// Surfaced in the sidebar's Ref column and used for filtering.
    /// </summary>
    public required string Ref { get; init; }

    /// <summary>Best-effort classification of the operation.</summary>
    public required ReflogOperationType OperationType { get; init; }

    /// <summary>
    /// Full subject line git recorded (e.g. <c>"commit: Fix the thing"</c>,
    /// <c>"reset: moving to HEAD~3"</c>). Includes the prefix used to
    /// derive <see cref="OperationType"/>.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>Timestamp of the operation, with the offset git
    /// emitted it in (usually the user's local TZ at the moment of
    /// the op, since reflog is a local file).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The same moment as <see cref="Timestamp"/>, rendered in the
    /// user's *current* local timezone. This is the value the
    /// sidebar/view bind to — a user who moves timezones still sees
    /// historical entries in their own clock, rather than the
    /// timezone recorded when the op happened.
    /// </summary>
    public DateTime LocalTime => Timestamp.LocalDateTime;
}
