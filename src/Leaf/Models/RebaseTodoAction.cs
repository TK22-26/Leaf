namespace Leaf.Models;

/// <summary>
/// One row of git's interactive-rebase todo language. Names match the verbs
/// emitted by <c>git rebase -i</c> (<c>pick</c>, <c>reword</c>, &#8230;) so we can
/// serialise the plan back out without translation. <see cref="Exec"/> is
/// supported because git treats it as a first-class entry; the tooling-only
/// verbs (<c>label</c>, <c>reset</c>, <c>merge</c>) used by <c>--rebase-merges</c>
/// are intentionally omitted from v1 — Leaf's UI only exposes the linear
/// flow.
/// </summary>
public enum RebaseTodoAction
{
    /// <summary>Use the commit unchanged.</summary>
    Pick,

    /// <summary>Use the commit but rewrite its message.</summary>
    Reword,

    /// <summary>Stop after applying so the user can amend the commit.</summary>
    Edit,

    /// <summary>Combine into the previous commit, prompting for the merged message.</summary>
    Squash,

    /// <summary>Combine into the previous commit, discarding this commit's message.</summary>
    Fixup,

    /// <summary>Skip the commit entirely.</summary>
    Drop,

    /// <summary>Run a shell command at this point in the rebase.</summary>
    Exec,
}
