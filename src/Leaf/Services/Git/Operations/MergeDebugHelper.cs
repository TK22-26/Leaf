using System.Diagnostics;
using System.IO;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Debug logging helper for merge/conflict/rebase/revert state tracking.
/// All output prefixed with [MERGE] for Stagehand debug capture filtering.
/// </summary>
internal static class MergeDebugHelper
{
    /// <summary>
    /// Log a canonical snapshot of all merge-related sentinel files.
    /// </summary>
    internal static void LogMergeState(string label, string repoPath)
    {
        var gitDir = Path.Combine(repoPath, ".git");
        Debug.WriteLine($"[MERGE][STATE] {label}: repo={Path.GetFileName(repoPath)}");
        Debug.WriteLine($"[MERGE][STATE]   MERGE_HEAD={File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))}");
        Debug.WriteLine($"[MERGE][STATE]   CHERRY_PICK_HEAD={File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))}");
        Debug.WriteLine($"[MERGE][STATE]   REVERT_HEAD={File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))}");
        Debug.WriteLine($"[MERGE][STATE]   rebase-merge={Directory.Exists(Path.Combine(gitDir, "rebase-merge"))}");
        Debug.WriteLine($"[MERGE][STATE]   rebase-apply={Directory.Exists(Path.Combine(gitDir, "rebase-apply"))}");
        Debug.WriteLine($"[MERGE][STATE]   leaf-merge-conflicts.txt={File.Exists(Path.Combine(gitDir, "leaf-merge-conflicts.txt"))}");
    }
}
