namespace Leaf.Models;

/// <summary>
/// How to incorporate a <c>.patch</c> file into the working tree.
/// Mirrors git's two ingestion paths exactly so the user can pick the
/// behaviour they want without having to know git internals.
/// </summary>
public enum ApplyPatchStrategy
{
    /// <summary>
    /// <c>git am</c> — applies as a series of new commits, preserving
    /// the author / date / message recorded in the patch headers. The
    /// patches must be in <c>format-patch</c> format (which is what
    /// Leaf produces). On conflict, the operation pauses and the user
    /// resolves through the existing merge editor; <c>am --continue</c>
    /// / <c>--skip</c> / <c>--abort</c> drive the rest.
    /// </summary>
    Am,

    /// <summary>
    /// <c>git apply</c> — applies the diff text only, leaving the
    /// staged changes in the working tree without committing. Use this
    /// when re-attributing the change to the current author or when
    /// only a subset of the patch is interesting.
    /// </summary>
    Apply,
}
