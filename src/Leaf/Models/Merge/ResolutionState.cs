namespace Leaf.Models.Merge;

/// <summary>
/// Discriminated union describing how a single conflict region has been resolved.
/// Immutable — each user action produces a new value rather than mutating shared state.
/// </summary>
public abstract record ResolutionState
{
    private protected ResolutionState() { }

    /// <summary>Conflict is still open. The result pane shows conflict markers for this region.</summary>
    public sealed record Unresolved : ResolutionState
    {
        public static readonly Unresolved Instance = new();
    }

    /// <summary>User accepted the Ours side verbatim.</summary>
    public sealed record AcceptOurs : ResolutionState
    {
        public static readonly AcceptOurs Instance = new();
    }

    /// <summary>User accepted the Theirs side verbatim.</summary>
    public sealed record AcceptTheirs : ResolutionState
    {
        public static readonly AcceptTheirs Instance = new();
    }

    /// <summary>
    /// User accepted both sides.
    /// <paramref name="FirstOurs"/> controls ordering (<c>true</c> = ours-then-theirs).
    /// <paramref name="SmartCombine"/> selects interleaved combine (<c>true</c>) vs dumb concatenation (<c>false</c>).
    /// </summary>
    public sealed record AcceptBoth(bool FirstOurs, bool SmartCombine) : ResolutionState;

    /// <summary>
    /// User typed a custom resolution. <paramref name="Text"/> is the final rendered form for this region,
    /// including any trailing newline; the composer emits it verbatim.
    /// </summary>
    public sealed record Manual(string Text) : ResolutionState;
}
