using System.IO;
using Leaf.Core.InteractiveRebase;

namespace Leaf.Services;

/// <summary>
/// Single source of truth for resolving the <c>Leaf.SequenceEditor.exe</c>
/// helper path and building the env-var dictionary that drives it. Used by
/// <see cref="InteractiveRebaseService"/> when it kicks off a rebase, and
/// by <see cref="Git.Operations.RebaseOperations"/> when a paused
/// interactive rebase is resumed via <c>git rebase --continue</c> — the
/// continue path needs the same env so reword/squash entries that follow
/// the conflict point still get their custom messages.
/// </summary>
internal static class RebaseHelperResolver
{
    public const string SequenceEditorExecutable = "Leaf.SequenceEditor.exe";

    /// <summary>Marker file the service drops inside <c>.git/rebase-merge/</c> when a Leaf-driven interactive rebase pauses. Holds the absolute path to the temp dir so a subsequent continue can re-find the materialised plan.</summary>
    public const string LeafTempMarkerName = "leaf-rebase-temp";

    /// <summary>
    /// Resolve the helper exe location next to <c>Leaf.exe</c>. Returns null
    /// when the helper is missing — callers fail loudly rather than running
    /// git without the override and silently dropping reword/squash messages.
    /// </summary>
    public static string? FindHelperPath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, SequenceEditorExecutable);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Format an absolute Windows executable path for git's MSYS-bash editor
    /// invocation: forward slashes survive shell quoting, the surrounding
    /// double quotes protect against spaces in <c>%LOCALAPPDATA%</c> /
    /// <c>Program Files</c> install paths. <c>GIT_ASKPASS</c> doesn't need
    /// this treatment because git invokes askpass directly without a shell —
    /// the contracts differ.
    /// </summary>
    public static string ToShellEditorPath(string path) =>
        $"\"{path.Replace('\\', '/')}\"";

    /// <summary>Path of the marker file inside <paramref name="gitDirectory"/>.</summary>
    public static string LeafTempMarkerPath(string gitDirectory) =>
        Path.Combine(gitDirectory, "rebase-merge", LeafTempMarkerName);

    /// <summary>
    /// Build the helper env-var dictionary used at rebase start.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildLaunchEnvironment(
        string helperPath, string todoFile, string messagesDir, string cursorFile, bool overrideGitEditor)
    {
        var helperShell = ToShellEditorPath(helperPath);
        var env = new Dictionary<string, string>
        {
            [RebaseEditorRunner.TodoSourceEnv] = todoFile,
            [RebaseEditorRunner.MessagesDirEnv] = messagesDir,
            [RebaseEditorRunner.MessageCursorEnv] = cursorFile,
            ["GIT_SEQUENCE_EDITOR"] = helperShell,
        };
        if (overrideGitEditor)
        {
            env["GIT_EDITOR"] = helperShell;
        }
        return env;
    }

    /// <summary>
    /// Reconstruct the helper env-var dictionary for <c>git rebase --continue</c>
    /// when a Leaf-driven interactive rebase is paused. Returns null when no
    /// marker is found — the continue still runs, just without an editor
    /// override (correct: a non-Leaf rebase shouldn't be hijacked).
    /// </summary>
    public static IReadOnlyDictionary<string, string>? BuildContinuationEnvironment(string gitDirectory)
    {
        var marker = LeafTempMarkerPath(gitDirectory);
        if (!File.Exists(marker)) return null;

        string tempDir;
        try
        {
            tempDir = File.ReadAllText(marker).Trim();
        }
        catch (IOException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(tempDir) || !Directory.Exists(tempDir)) return null;

        var helper = FindHelperPath();
        if (string.IsNullOrEmpty(helper)) return null;

        return BuildLaunchEnvironment(
            helper,
            todoFile: Path.Combine(tempDir, "git-rebase-todo"),
            messagesDir: Path.Combine(tempDir, "messages"),
            cursorFile: Path.Combine(tempDir, "cursor"),
            // Continuation always overrides GIT_EDITOR — by the time we
            // get here, the only reason git would invoke an editor is for
            // a reword/squash entry that comes after the conflict point.
            overrideGitEditor: true);
    }
}
