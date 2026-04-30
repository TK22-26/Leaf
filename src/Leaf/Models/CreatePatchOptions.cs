namespace Leaf.Models;

/// <summary>
/// User-tunable options for <c>git format-patch</c>. Defaults match the
/// most common case: text-only patches, no sign-off, one file per
/// commit numbered 0001, 0002, &#8230;
/// </summary>
public sealed class CreatePatchOptions
{
    /// <summary>
    /// Include binary diffs (<c>--binary</c>). Off by default because
    /// binary deltas balloon patch size and most patch-and-mail
    /// workflows refuse them; turning it on is opt-in.
    /// </summary>
    public bool IncludeBinary { get; set; } = false;

    /// <summary>
    /// Append a <c>Signed-off-by:</c> trailer using the user's
    /// <c>user.name</c> + <c>user.email</c> from git config. Required by
    /// some upstreams (Linux kernel, etc.). The flag is passed
    /// authoritatively (<c>--signoff</c> when true, <c>--no-signoff</c>
    /// when false) so that unchecking the option also wins over a
    /// <c>format.signoff = true</c> entry in the user's global git
    /// config — without that, the global setting would silently
    /// override the dialog state.
    /// </summary>
    public bool SignOff { get; set; } = false;

    /// <summary>
    /// Override the cover-letter subject (<c>--subject-prefix</c>). Null
    /// means use git's default of <c>"PATCH"</c>.
    /// </summary>
    public string? SubjectPrefix { get; set; }
}
