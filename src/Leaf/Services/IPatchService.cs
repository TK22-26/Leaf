using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for creating and applying <c>.patch</c> files via
/// <c>git format-patch</c> / <c>git am</c> / <c>git apply</c>. Mirrors
/// the CLI verb set so the user's mental model maps 1:1 to behaviour.
/// </summary>
public interface IPatchService
{
    /// <summary>
    /// Run <c>git format-patch</c> for <paramref name="commitShas"/> and
    /// write one <c>.patch</c> file per commit into
    /// <paramref name="outputDirectory"/>. Files are returned in
    /// commit-application order (oldest first) — the same order
    /// <see cref="ApplyAsync"/> expects.
    /// </summary>
    Task<CreatePatchResult> CreateAsync(
        IRepositorySession session,
        IReadOnlyList<string> commitShas,
        string outputDirectory,
        CreatePatchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run <c>git format-patch -1 --stdout</c> for one commit and
    /// return the resulting patch text. Used for "Export to clipboard"
    /// where the user wants the patch body without writing a file.
    /// </summary>
    Task<string> ExportToTextAsync(
        IRepositorySession session,
        string commitSha,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parse the mail-style headers of <paramref name="patchFiles"/>
    /// into preview items. Pure I/O — does not invoke git. Files that
    /// don't look like format-patch output get <see cref="PatchPreviewItem.HasParseError"/>=true
    /// so the UI can flag them before the user clicks Apply.
    /// </summary>
    Task<IReadOnlyList<PatchPreviewItem>> PreviewAsync(
        IReadOnlyList<string> patchFiles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply <paramref name="patchFiles"/> using the chosen
    /// <paramref name="strategy"/>. On <c>am</c> conflict the operation
    /// pauses (returns <see cref="ApplyPatchResult.HasConflicts"/>) and
    /// the caller drives the resolution through the merge editor +
    /// <see cref="ContinueAsync"/> / <see cref="SkipAsync"/> /
    /// <see cref="AbortAsync"/>.
    /// </summary>
    Task<ApplyPatchResult> ApplyAsync(
        IRepositorySession session,
        IReadOnlyList<string> patchFiles,
        ApplyPatchStrategy strategy,
        CancellationToken cancellationToken = default);

    /// <summary>Continue an in-progress <c>git am</c> after the user resolved conflicts.</summary>
    Task<ApplyPatchResult> ContinueAsync(IRepositorySession session, CancellationToken cancellationToken = default);

    /// <summary>Skip the current patch in an in-progress <c>git am</c>.</summary>
    Task<ApplyPatchResult> SkipAsync(IRepositorySession session, CancellationToken cancellationToken = default);

    /// <summary>Abort an in-progress <c>git am</c> and restore the pre-apply HEAD.</summary>
    Task AbortAsync(IRepositorySession session, CancellationToken cancellationToken = default);

    /// <summary>True when <c>.git/rebase-apply</c> exists, i.e. a paused <c>git am</c>.</summary>
    Task<bool> IsAmInProgressAsync(IRepositorySession session, CancellationToken cancellationToken = default);
}
