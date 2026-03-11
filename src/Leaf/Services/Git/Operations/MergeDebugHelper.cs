using System.IO;
using Leaf.Services;

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
        Log.Info("Merge", $"{label}: repo={Path.GetFileName(repoPath)}");
        Log.Info("Merge", $"  MERGE_HEAD={File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))}");
        Log.Info("Merge", $"  CHERRY_PICK_HEAD={File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))}");
        Log.Info("Merge", $"  REVERT_HEAD={File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))}");
        Log.Info("Merge", $"  rebase-merge={Directory.Exists(Path.Combine(gitDir, "rebase-merge"))}");
        Log.Info("Merge", $"  rebase-apply={Directory.Exists(Path.Combine(gitDir, "rebase-apply"))}");
        Log.Info("Merge", $"  leaf-merge-conflicts.txt={File.Exists(Path.Combine(gitDir, "leaf-merge-conflicts.txt"))}");
    }
}
