using System.IO;

namespace Leaf.Core.InteractiveRebase;

/// <summary>
/// Pure logic behind <c>Leaf.SequenceEditor.exe</c>. Git invokes the helper
/// exe with a single argument — the path to the file it wants edited. Two
/// kinds of files reach us during an interactive rebase:
/// <list type="bullet">
///   <item><c>.git/rebase-merge/git-rebase-todo</c> — the todo list. Written
///         once when the rebase starts (and again on <c>--edit-todo</c>).</item>
///   <item><c>.git/COMMIT_EDITMSG</c> — the commit-message buffer for
///         <c>reword</c>, <c>squash</c>, and <c>fixup -c</c> entries. Written
///         once per such todo entry, in todo order.</item>
/// </list>
/// Leaf pre-computes the todo content and the messages list before launching
/// git, drops them into a temp directory, and points the helper at them via
/// environment variables. The helper is therefore a deterministic file copy:
/// no IPC with the parent process, no interactive prompting, no fallbacks.
/// </summary>
/// <remarks>
/// Per Leaf's Engineering Software Policy this class fails loudly when state
/// is inconsistent (env var present but the file it points to is missing,
/// cursor outranges the messages list, etc.). Silently leaving git's buffer
/// untouched would commit the wrong text or stall the rebase — both worse
/// than aborting with a clear error.
/// </remarks>
public static class RebaseEditorRunner
{
    /// <summary>Path to a file whose contents replace <c>git-rebase-todo</c>.</summary>
    public const string TodoSourceEnv = "LEAF_REBASE_TODO_FILE";

    /// <summary>Directory containing <c>0001.msg</c>, <c>0002.msg</c>, &#8230; in todo order.</summary>
    public const string MessagesDirEnv = "LEAF_REBASE_MESSAGES_DIR";

    /// <summary>Single-line counter file the helper increments per message invocation.</summary>
    public const string MessageCursorEnv = "LEAF_REBASE_MESSAGE_CURSOR";

    /// <summary>Diagnostic-only: path the helper appends a one-line trace to per call.</summary>
    public const string TraceFileEnv = "LEAF_REBASE_TRACE_FILE";

    /// <summary>Result codes mirror Unix process conventions: 0 success, non-zero abort.</summary>
    public enum Outcome
    {
        Success = 0,
        MissingArgument = 64,
        TodoSourceMissing = 65,
        MessagesDirMissing = 66,
        CursorOutOfRange = 67,
        CursorCorrupted = 68,
    }

    /// <summary>
    /// Decide what to do based on the file path git handed us and the
    /// environment Leaf set before launching. Returns the exit code the
    /// helper exe should propagate. <paramref name="diagnostic"/> receives
    /// a short message on every non-success result (also written to stderr
    /// by the caller).
    /// </summary>
    public static Outcome Run(IReadOnlyList<string> args, IFileSystem fs, IEnvironment env, out string diagnostic)
    {
        diagnostic = string.Empty;

        if (args.Count < 1 || string.IsNullOrEmpty(args[0]))
        {
            diagnostic = "Leaf.SequenceEditor: no file path argument supplied by git.";
            return Outcome.MissingArgument;
        }

        var targetPath = args[0];
        var fileName = Path.GetFileName(targetPath);
        Trace(env, fs, $"invoke target='{targetPath}'");

        if (IsTodoFile(fileName))
        {
            return HandleTodo(targetPath, fs, env, out diagnostic);
        }

        if (IsCommitMessageFile(fileName))
        {
            return HandleCommitMessage(targetPath, fs, env, out diagnostic);
        }

        // Anything else (e.g. git asks the editor for a tag annotation while
        // a rebase happens to be running) is outside our contract. Leaving
        // the file untouched and exiting 0 lets git use whatever default
        // content is already there. We log it so a real misconfiguration
        // doesn't get silently absorbed.
        Trace(env, fs, $"unrecognised file '{fileName}', leaving untouched");
        return Outcome.Success;
    }

    private static Outcome HandleTodo(string targetPath, IFileSystem fs, IEnvironment env, out string diagnostic)
    {
        diagnostic = string.Empty;
        var source = env.GetVariable(TodoSourceEnv);
        if (string.IsNullOrEmpty(source))
        {
            // No source means Leaf didn't intend to drive this rebase. Git
            // will keep whatever it already wrote — that is git's default
            // editor behaviour, equivalent to opening Notepad and saving.
            // We still log it so a misconfigured launch surfaces.
            Trace(env, fs, "todo invoked without LEAF_REBASE_TODO_FILE; leaving git's default");
            return Outcome.Success;
        }

        if (!fs.FileExists(source))
        {
            diagnostic = $"Leaf.SequenceEditor: todo source '{source}' is missing.";
            return Outcome.TodoSourceMissing;
        }

        var content = fs.ReadAllText(source);
        fs.WriteAllText(targetPath, content);
        Trace(env, fs, $"todo replaced from '{source}' ({content.Length} chars)");
        return Outcome.Success;
    }

    private static Outcome HandleCommitMessage(string targetPath, IFileSystem fs, IEnvironment env, out string diagnostic)
    {
        diagnostic = string.Empty;
        var dir = env.GetVariable(MessagesDirEnv);
        if (string.IsNullOrEmpty(dir))
        {
            // Same rationale as the todo branch: no env, no Leaf intent.
            Trace(env, fs, "commit-msg invoked without LEAF_REBASE_MESSAGES_DIR; leaving git's default");
            return Outcome.Success;
        }

        if (!fs.DirectoryExists(dir))
        {
            diagnostic = $"Leaf.SequenceEditor: messages directory '{dir}' is missing.";
            return Outcome.MessagesDirMissing;
        }

        var cursorFile = env.GetVariable(MessageCursorEnv);
        if (string.IsNullOrEmpty(cursorFile))
        {
            diagnostic = $"Leaf.SequenceEditor: {MessageCursorEnv} environment variable is not set.";
            return Outcome.CursorOutOfRange;
        }

        // Atomic-ish increment: read, bump, write back. Git serialises todo
        // execution (a single commit at a time), so we never see concurrent
        // helper invocations against the same cursor.
        if (!TryReadCursor(fs, cursorFile, out var previous, out diagnostic))
        {
            return Outcome.CursorCorrupted;
        }

        var index = previous + 1;
        var messageFile = Path.Combine(dir, $"{index:0000}.msg");
        if (!fs.FileExists(messageFile))
        {
            diagnostic = $"Leaf.SequenceEditor: expected message file '{messageFile}' is missing.";
            return Outcome.CursorOutOfRange;
        }

        var content = fs.ReadAllText(messageFile);

        // Empty message file is the explicit "pass through" signal Leaf
        // uses for Squash actions where the user did not type a custom
        // message. Git pre-populates COMMIT_EDITMSG with the combined
        // messages of the squashed commits — leaving that buffer untouched
        // is the correct behaviour, and any rewrite (even to the
        // squashed commit's own original message) would lose the
        // preceding commit's text. The cursor still increments so later
        // messages line up with the right todo entries.
        if (content.Length == 0)
        {
            WriteCursor(fs, cursorFile, index);
            Trace(env, fs, $"commit-msg #{index} pass-through (empty queue file '{messageFile}')");
            return Outcome.Success;
        }

        fs.WriteAllText(targetPath, content);
        WriteCursor(fs, cursorFile, index);
        Trace(env, fs, $"commit-msg #{index} replaced from '{messageFile}' ({content.Length} chars)");
        return Outcome.Success;
    }

    /// <summary>
    /// Read the cursor file. Returns false (and a diagnostic) when the
    /// file is present but its contents aren't a valid integer — silently
    /// resetting to 0 would re-apply the first queued message to the wrong
    /// commit, which is the kind of failure Leaf's Engineering Software
    /// Policy explicitly demands we surface rather than mask.
    /// </summary>
    private static bool TryReadCursor(IFileSystem fs, string path, out int value, out string diagnostic)
    {
        diagnostic = string.Empty;
        if (!fs.FileExists(path))
        {
            value = 0;
            return true;
        }

        var raw = fs.ReadAllText(path).Trim();
        if (int.TryParse(raw, out value)) return true;

        diagnostic = $"Leaf.SequenceEditor: cursor file '{path}' contains '{raw}', expected an integer.";
        value = 0;
        return false;
    }

    private static void WriteCursor(IFileSystem fs, string path, int value)
    {
        fs.WriteAllText(path, value.ToString());
    }

    private static bool IsTodoFile(string fileName) =>
        string.Equals(fileName, "git-rebase-todo", StringComparison.Ordinal) ||
        string.Equals(fileName, "git-rebase-todo.backup", StringComparison.Ordinal);

    private static bool IsCommitMessageFile(string fileName) =>
        string.Equals(fileName, "COMMIT_EDITMSG", StringComparison.Ordinal) ||
        string.Equals(fileName, "MERGE_MSG", StringComparison.Ordinal) ||
        string.Equals(fileName, "SQUASH_MSG", StringComparison.Ordinal);

    private static void Trace(IEnvironment env, IFileSystem fs, string line)
    {
        var trace = env.GetVariable(TraceFileEnv);
        if (string.IsNullOrEmpty(trace)) return;
        try
        {
            fs.AppendAllText(trace, $"[{DateTime.UtcNow:O}] {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Trace is diagnostic-only — never fail the rebase because the
            // log file was busy or moved.
        }
    }
}
