using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Drives <c>git rebase -i</c> via the <c>Leaf.SequenceEditor.exe</c> helper.
/// The service is the single point that builds a rebase plan from a list of
/// <see cref="RebaseTodoItem"/>s, materialises it onto disk in git's todo
/// grammar, configures the editor environment, runs the rebase, and cleans
/// up afterwards. The ViewModel layer never touches files or env vars.
/// </summary>
public interface IInteractiveRebaseService
{
    /// <summary>
    /// List the commits an interactive rebase would touch when started from
    /// <paramref name="fromCommitSha"/>. The first item in the returned list
    /// is the oldest commit (matches git's todo order), every item is
    /// pre-set to <see cref="RebaseTodoAction.Pick"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the commit cannot be resolved or is the repository root
    /// (root rebases need <c>--root</c>; v1 of the editor doesn't expose it).
    /// </exception>
    Task<IReadOnlyList<RebaseTodoItem>> LoadPlanAsync(
        IRepositorySession session,
        string fromCommitSha,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run the rebase. Returns when git exits — either with a clean rebase
    /// (<see cref="MergeResult.Success"/>), with conflicts pending the user's
    /// resolution (<see cref="MergeResult.HasConflicts"/>), or with an error
    /// message git wrote to stderr.
    /// </summary>
    Task<MergeResult> StartAsync(
        IRepositorySession session,
        string fromCommitSha,
        IReadOnlyList<RebaseTodoItem> plan,
        CancellationToken cancellationToken = default);
}
