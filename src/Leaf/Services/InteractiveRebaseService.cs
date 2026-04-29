using System.IO;
using System.Text;
using Leaf.Core.InteractiveRebase;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Default <see cref="IInteractiveRebaseService"/>. Communicates with git
/// purely through <see cref="IGitCommandRunner"/> + the
/// <c>Leaf.SequenceEditor.exe</c> helper — no LibGit2Sharp, no shell.
/// </summary>
public class InteractiveRebaseService : IInteractiveRebaseService
{
    private const string SequenceEditorExecutable = "Leaf.SequenceEditor.exe";

    // Git's plumbing log format: hash, short hash, "name <email>", strict
    // ISO-8601 author date, full message body, then the record terminator.
    // %x1F separates fields, %x1E separates records — both ASCII control
    // characters that cannot appear inside a commit message body, so the
    // parser is robust to subjects with embedded newlines, quotes, etc.
    private const string LogFormat = "%H%x1F%h%x1F%an <%ae>%x1F%aI%x1F%B%x1E";
    private const char FieldSep = (char)0x1F;
    private const char RecordSep = (char)0x1E;

    private readonly IGitCommandRunner _commandRunner;
    private readonly IRepositoryEventHub _eventHub;

    /// <summary>
    /// Cached path to <c>Leaf.SequenceEditor.exe</c>. Same pattern as
    /// <see cref="GitCommandRunner"/>'s AskPass resolution: evaluated once,
    /// returns null when the helper is missing so we can fail loudly with a
    /// clean error rather than producing a half-driven rebase.
    /// </summary>
    private static readonly Lazy<string?> _sequenceEditorPath = new(() =>
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, SequenceEditorExecutable);
        if (File.Exists(candidate)) return candidate;
        Log.Warn("Rebase", $"{SequenceEditorExecutable} not found at {candidate}; interactive rebase will not run.");
        return null;
    });

    public InteractiveRebaseService(IGitCommandRunner commandRunner, IRepositoryEventHub eventHub)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RebaseTodoItem>> LoadPlanAsync(
        IRepositorySession session,
        string fromCommitSha,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromCommitSha))
            throw new ArgumentException("fromCommitSha is required.", nameof(fromCommitSha));

        // Validate the commit exists and has a parent (root commits would
        // require --root; v1 doesn't expose that path).
        var parentProbe = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["rev-parse", "--verify", "--quiet", $"{fromCommitSha}^"],
            cancellationToken: cancellationToken);
        if (!parentProbe.Success)
        {
            throw new InvalidOperationException(
                $"Cannot rebase from {fromCommitSha}: it has no parent (root commit) or doesn't exist. " +
                "Interactive rebase from the root commit isn't supported in this version.");
        }

        var range = $"{fromCommitSha}^..HEAD";
        var log = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["log", "--reverse", $"--pretty=format:{LogFormat}", range],
            cancellationToken: cancellationToken);
        if (!log.Success)
        {
            throw new InvalidOperationException(
                $"git log {range} failed: {log.StandardError.Trim()}");
        }

        return ParseLogRecords(log.StandardOutput);
    }

    /// <inheritdoc />
    public async Task<MergeResult> StartAsync(
        IRepositorySession session,
        string fromCommitSha,
        IReadOnlyList<RebaseTodoItem> plan,
        CancellationToken cancellationToken = default)
    {
        if (plan == null || plan.Count == 0)
            throw new ArgumentException("Plan must contain at least one item.", nameof(plan));

        var helper = _sequenceEditorPath.Value;
        if (string.IsNullOrEmpty(helper))
        {
            return new MergeResult
            {
                Success = false,
                ErrorMessage =
                    $"{SequenceEditorExecutable} is missing from the install directory; " +
                    "interactive rebase requires the helper exe.",
            };
        }

        // Spool the plan + reword/squash messages onto disk before launching
        // git. The temp directory is per-rebase and cleaned up after the
        // helper has finished consuming it.
        var temp = Directory.CreateTempSubdirectory("leaf-rebase-");
        try
        {
            var todoFile = Path.Combine(temp.FullName, "git-rebase-todo");
            var messagesDir = Path.Combine(temp.FullName, "messages");
            var cursorFile = Path.Combine(temp.FullName, "cursor");
            Directory.CreateDirectory(messagesDir);
            await File.WriteAllTextAsync(cursorFile, "0", cancellationToken);

            var (todoContent, messageCount) = MaterialisePlan(plan, messagesDir);
            await File.WriteAllTextAsync(todoFile, todoContent, new UTF8Encoding(false), cancellationToken);

            var env = new Dictionary<string, string>
            {
                [RebaseEditorRunner.TodoSourceEnv] = todoFile,
                [RebaseEditorRunner.MessagesDirEnv] = messagesDir,
                [RebaseEditorRunner.MessageCursorEnv] = cursorFile,
                ["GIT_SEQUENCE_EDITOR"] = helper,
            };

            // Only override GIT_EDITOR when the plan actually rewrites
            // commit messages. Otherwise let git fall back to its default
            // editor path so unexpected editor invocations (rare, but e.g.
            // when a hook bumps something) hit Notepad rather than our
            // helper, which would refuse to write a message it doesn't have.
            if (messageCount > 0)
            {
                env["GIT_EDITOR"] = helper;
            }

            var rebaseArgs = new[] { "rebase", "--interactive", $"{fromCommitSha}^" };
            Log.Info("Rebase", $"Starting interactive rebase: {string.Join(" ", rebaseArgs)} (plan items={plan.Count}, messages={messageCount})");
            var result = await _commandRunner.RunAsync(
                session.RepositoryPath,
                rebaseArgs,
                input: null,
                credentialKey: null,
                extraEnvironment: env,
                cancellationToken: cancellationToken);

            _eventHub.NotifyCommitHistoryChanged();
            _eventHub.NotifyWorkingDirectoryChanged();
            _eventHub.NotifyBranchesChanged();

            if (result.Success)
            {
                return new MergeResult { Success = true };
            }

            // git rebase signals "stopped at <sha>" via stderr/stdout when
            // it hits a conflict, an `edit` instruction, or anything else
            // that needs the user. The presence of .git/rebase-merge tells
            // us the rebase is paused rather than failed outright.
            var rebaseDir = Path.Combine(session.RepositoryPath, ".git", "rebase-merge");
            var paused = Directory.Exists(rebaseDir) ||
                         Directory.Exists(Path.Combine(session.RepositoryPath, ".git", "rebase-apply"));

            if (paused)
            {
                _eventHub.NotifyConflictStateChanged();
                return new MergeResult
                {
                    Success = false,
                    HasConflicts = true,
                    ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                        ? result.StandardOutput.Trim()
                        : result.StandardError.Trim(),
                };
            }

            return new MergeResult
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"git rebase exited with code {result.ExitCode}."
                    : result.StandardError.Trim(),
            };
        }
        finally
        {
            // Best-effort cleanup. The helper exe may still be writing the
            // cursor file when we get here on a paused rebase, so we don't
            // delete during a successful start that left a paused state —
            // git reuses the helper across continue/skip. The temp dir is
            // just files; OS will GC on next reboot if we leak.
            TryDelete(temp.FullName);
        }
    }

    internal static IReadOnlyList<RebaseTodoItem> ParseLogRecords(string output)
    {
        var items = new List<RebaseTodoItem>();
        if (string.IsNullOrEmpty(output)) return items;

        // Trim trailing record separator + newline that git log emits.
        var span = output.AsSpan().TrimEnd('\n').TrimEnd(RecordSep);
        var records = span.ToString().Split(RecordSep, StringSplitOptions.RemoveEmptyEntries);

        foreach (var record in records)
        {
            var trimmed = record.TrimStart('\n');
            var fields = trimmed.Split(FieldSep);
            if (fields.Length < 5)
            {
                Log.Warn("Rebase", $"Skipping malformed log record (fields={fields.Length})");
                continue;
            }

            var sha = fields[0];
            var shortSha = fields[1];
            var author = fields[2];
            var date = DateTimeOffset.TryParse(fields[3], out var d) ? d : DateTimeOffset.MinValue;
            var fullMessage = fields[4].TrimEnd('\n');
            var subject = ExtractSubject(fullMessage);

            items.Add(new RebaseTodoItem
            {
                Sha = sha,
                ShortSha = shortSha,
                Author = author,
                AuthoredWhen = date,
                Subject = subject,
                OriginalMessage = fullMessage,
                Action = RebaseTodoAction.Pick,
            });
        }

        return items;
    }

    private static string ExtractSubject(string message)
    {
        var nl = message.IndexOf('\n');
        return nl < 0 ? message : message[..nl];
    }

    /// <summary>
    /// Render the plan to git's todo grammar and write reword/squash
    /// messages into <paramref name="messagesDir"/> as <c>0001.msg</c>,
    /// <c>0002.msg</c>, &#8230; in encounter order. Returns the todo content
    /// (UTF-8, LF line endings — git's preference on every platform) and
    /// the count of messages written.
    /// </summary>
    internal static (string todo, int messageCount) MaterialisePlan(
        IReadOnlyList<RebaseTodoItem> plan,
        string messagesDir)
    {
        var sb = new StringBuilder();
        var messageIndex = 0;

        foreach (var item in plan)
        {
            switch (item.Action)
            {
                case RebaseTodoAction.Pick:
                    sb.Append("pick ").Append(item.Sha).Append(' ').Append(item.Subject).Append('\n');
                    break;
                case RebaseTodoAction.Reword:
                    sb.Append("reword ").Append(item.Sha).Append(' ').Append(item.Subject).Append('\n');
                    messageIndex++;
                    WriteMessage(messagesDir, messageIndex, item.NewMessage ?? item.OriginalMessage);
                    break;
                case RebaseTodoAction.Edit:
                    sb.Append("edit ").Append(item.Sha).Append(' ').Append(item.Subject).Append('\n');
                    break;
                case RebaseTodoAction.Squash:
                    sb.Append("squash ").Append(item.Sha).Append(' ').Append(item.Subject).Append('\n');
                    messageIndex++;
                    // Git pre-populates the editor buffer with the previous
                    // commit's message + this commit's message joined. If
                    // the user supplied an explicit replacement we honour
                    // it; otherwise we fall through and let git's default
                    // (combined messages) reach the helper, which leaves
                    // the buffer untouched when no message file is queued
                    // for that index.
                    WriteMessage(messagesDir, messageIndex,
                        string.IsNullOrEmpty(item.NewMessage) ? item.OriginalMessage : item.NewMessage);
                    break;
                case RebaseTodoAction.Fixup:
                    sb.Append("fixup ").Append(item.Sha).Append(' ').Append(item.Subject).Append('\n');
                    break;
                case RebaseTodoAction.Drop:
                    // git accepts `drop` as a verb; commit subject is
                    // optional but improves the todo log when user runs
                    // `git rebase --edit-todo` mid-flight.
                    sb.Append("drop ").Append(item.Sha).Append(' ').Append(item.Subject).Append('\n');
                    break;
                case RebaseTodoAction.Exec:
                    if (string.IsNullOrWhiteSpace(item.ExecCommand))
                    {
                        throw new InvalidOperationException(
                            $"Rebase plan has an Exec entry with no command (after {item.ShortSha}).");
                    }
                    sb.Append("exec ").Append(item.ExecCommand).Append('\n');
                    break;
                default:
                    throw new InvalidOperationException($"Unknown rebase action: {item.Action}");
            }
        }

        return (sb.ToString(), messageIndex);
    }

    private static void WriteMessage(string dir, int index, string content)
    {
        var path = Path.Combine(dir, $"{index:0000}.msg");
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException ex)
        {
            Log.Info("Rebase", $"Temp cleanup deferred for '{path}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Info("Rebase", $"Temp cleanup denied for '{path}': {ex.Message}");
        }
    }
}
