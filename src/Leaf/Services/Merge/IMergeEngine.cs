using Leaf.Models.Merge;

namespace Leaf.Services.Merge;

/// <summary>
/// Three-way merge engine. Implementations produce a <see cref="MergeDocument"/> from
/// three input texts (base ancestor, ours, theirs).
/// </summary>
/// <remarks>
/// The Phase 1 implementation (<c>GitMergeFileEngine</c>) shells out to
/// <c>git merge-file --diff-algorithm=histogram --zdiff3</c>, matching the algorithm
/// used by <c>git merge</c> itself.
/// </remarks>
public interface IMergeEngine
{
    /// <summary>
    /// Perform a three-way merge and return the structured document.
    /// </summary>
    /// <param name="filePath">Relative or absolute path of the file being merged. Used for diagnostics only.</param>
    /// <param name="baseText">The common ancestor text.</param>
    /// <param name="oursText">Content from the current branch (HEAD / "ours").</param>
    /// <param name="theirsText">Content from the incoming branch ("theirs").</param>
    /// <param name="ignoreWhitespace">When <c>true</c>, pass <c>--ignore-all-space</c> to the engine.</param>
    /// <param name="oursLabel">Optional label emitted in conflict markers for the ours side.</param>
    /// <param name="theirsLabel">Optional label emitted in conflict markers for the theirs side.</param>
    /// <param name="baseLabel">Optional label emitted in conflict markers for the base side.</param>
    /// <param name="cancellationToken">Cancels the merge. The underlying git process is killed on cancel.</param>
    /// <returns>Immutable <see cref="MergeDocument"/> ready for UI consumption.</returns>
    /// <exception cref="MergeEngineException">Thrown when the underlying tool fails unrecoverably.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    Task<MergeDocument> MergeAsync(
        string filePath,
        string baseText,
        string oursText,
        string theirsText,
        bool ignoreWhitespace = false,
        string? oursLabel = null,
        string? theirsLabel = null,
        string? baseLabel = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when the merge engine cannot produce a <see cref="MergeDocument"/> — e.g. the
/// underlying <c>git merge-file</c> process failed with a non-conflict error code or emitted
/// malformed output.
/// </summary>
public sealed class MergeEngineException : Exception
{
    public MergeEngineException(string message) : base(message) { }
    public MergeEngineException(string message, Exception innerException) : base(message, innerException) { }
}
