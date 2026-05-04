using System.Diagnostics;
using Leaf.Core.InteractiveRebase;

namespace Leaf.SequenceEditor;

/// <summary>
/// GIT_SEQUENCE_EDITOR / GIT_EDITOR helper for Leaf's interactive rebase
/// flow. All real logic lives in <see cref="RebaseEditorRunner"/> so it can
/// be exercised without spawning a process. This file is the thin OS-process
/// wrapper that git can <c>CreateProcess</c>.
///
/// Contract (from <c>git-rebase(1)</c>):
///   - Git invokes us with the path to the file it wants edited as argv[0].
///   - The helper edits the file in place and exits 0 on success. Non-zero
///     aborts the rebase.
/// Leaf-specific environment is documented on <see cref="RebaseEditorRunner"/>.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var outcome = RebaseEditorRunner.Run(
            args,
            new RealFileSystem(),
            new RealEnvironment(),
            out var diagnostic);

        if (outcome != RebaseEditorRunner.Outcome.Success)
        {
            Console.Error.WriteLine(diagnostic);
            Debug.WriteLine($"[SequenceEditor] {outcome}: {diagnostic}");
        }

        return (int)outcome;
    }
}
