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

    /// <summary>Prefix for temp directories the service spools per rebase. Surfaced for the housekeeping pass that cleans up dirs left over from paused rebases that the user later aborted outside Leaf.</summary>
    internal const string TempDirPrefix = "leaf-rebase-";

    /// <summary>Stale-temp threshold for the housekeeping sweep. Any prefix-matched dir older than this gets reaped on the next StartAsync call.</summary>
    internal static readonly TimeSpan StaleTempThreshold = TimeSpan.FromHours(24);

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
        if (session == null) throw new ArgumentNullException(nameof(session));
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
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(fromCommitSha))
            throw new ArgumentException("fromCommitSha is required.", nameof(fromCommitSha));
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

        // Reap any leaked temp dirs from prior paused rebases the user
        // aborted outside Leaf. Cheap and bounded — Path.GetTempPath()
        // typically holds a handful of entries and the prefix filter
        // narrows it further.
        SweepStaleTempDirs();

        // Spool the plan + reword/squash messages onto disk before launching
        // git. We deliberately do NOT clean the temp dir on a paused rebase:
        // git re-invokes our helper for any reword/squash rows that come
        // after the conflict point, and those invocations need the messages
        // and cursor still on disk. Cleanup happens on success or on hard
        // failure, plus the housekeeping sweep above for the abort-outside-
        // Leaf case.
        var temp = Directory.CreateTempSubdirectory(TempDirPrefix);
        var paused = false;
        try
        {
            var todoFile = Path.Combine(temp.FullName, "git-rebase-todo");
            var messagesDir = Path.Combine(temp.FullName, "messages");
            var cursorFile = Path.Combine(temp.FullName, "cursor");
            Directory.CreateDirectory(messagesDir);
            await File.WriteAllTextAsync(cursorFile, "0", cancellationToken);

            var (todoContent, messageCount) = MaterialisePlan(plan, messagesDir);
            await File.WriteAllTextAsync(todoFile, todoContent, new UTF8Encoding(false), cancellationToken);

            // Git on Windows runs the editor via a shell (MSYS bash for
            // GIT_SEQUENCE_EDITOR / GIT_EDITOR), so a Windows path with
            // backslashes and spaces gets mangled by the shell unless we
            // pre-format it: forward-slash separators, double-quoted to
            // protect spaces. (GIT_ASKPASS gets run directly without a
            // shell, which is why the AskPass helper works as a raw
            // backslash path — the contracts differ here.)
            var helperForShell = ToShellEditorPath(helper);

            var env = new Dictionary<string, string>
            {
                [RebaseEditorRunner.TodoSourceEnv] = todoFile,
                [RebaseEditorRunner.MessagesDirEnv] = messagesDir,
                [RebaseEditorRunner.MessageCursorEnv] = cursorFile,
                ["GIT_SEQUENCE_EDITOR"] = helperForShell,
            };

            // Only override GIT_EDITOR when the plan actually rewrites
            // commit messages. Otherwise let git fall back to its default
            // editor path so unexpected editor invocations (rare, but e.g.
            // when a hook bumps something) hit Notepad rather than our
            // helper, which would refuse to write a message it doesn't have.
            if (messageCount > 0)
            {
                env["GIT_EDITOR"] = helperForShell;
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
            // that needs the user. The presence of rebase-merge / rebase-apply
            // under .git tells us the rebase is paused rather than failed
            // outright. Worktrees route their git directory through
            // session.GitDirectory, not RepositoryPath/.git, so we use
            // that — the bare RepositoryPath/.git assumption would miss
            // paused rebases inside linked worktrees.
            paused = Directory.Exists(Path.Combine(session.GitDirectory, "rebase-merge")) ||
                     Directory.Exists(Path.Combine(session.GitDirectory, "rebase-apply"));

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
            // Only delete the temp dir when the rebase is fully done. On a
            // paused rebase git will re-invoke our helper later (after the
            // user runs continue/skip) and needs the messages + cursor
            // files still on disk; the next StartAsync's housekeeping
            // sweep cleans these up if the user abandons the rebase.
            if (!paused)
            {
                TryDelete(temp.FullName);
            }
        }
    }

    private static void SweepStaleTempDirs()
    {
        try
        {
            var tempRoot = Path.GetTempPath();
            var cutoff = DateTime.UtcNow - StaleTempThreshold;
            foreach (var dir in Directory.EnumerateDirectories(tempRoot, $"{TempDirPrefix}*"))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                    {
                        Directory.Delete(dir, recursive: true);
                        Log.Info("Rebase", $"Reaped stale temp dir '{dir}'.");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Info("Rebase", $"Stale temp '{dir}' could not be reaped: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort housekeeping — never fail StartAsync because the
            // sweep tripped on filesystem permissions.
            Log.Info("Rebase", $"Stale temp sweep failed: {ex.Message}");
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
            DateTimeOffset date;
            if (DateTimeOffset.TryParse(fields[3], out var parsedDate))
            {
                date = parsedDate;
            }
            else
            {
                // %aI emits strict ISO-8601 — a parse failure here implies
                // corrupt git output or a locale we haven't seen. The row
                // still loads (date is display-only, missing it doesn't
                // break the rebase plan) but we log so the regression is
                // visible.
                date = DateTimeOffset.MinValue;
                Log.Warn("Rebase", $"Could not parse author date '{fields[3]}' for {shortSha}; tooltip date will be blank.");
            }
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
                    // Git pre-populates COMMIT_EDITMSG with the combined
                    // messages of the squashed commits. When the user
                    // supplied an explicit replacement we honour it;
                    // otherwise we queue an EMPTY file as the helper's
                    // pass-through signal so git's combined default
                    // survives. Writing the squashed commit's own
                    // OriginalMessage here would silently delete the
                    // preceding commit's message from the merged result.
                    WriteMessage(messagesDir, messageIndex,
                        string.IsNullOrEmpty(item.NewMessage) ? string.Empty : item.NewMessage);
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

    /// <summary>
    /// Format an absolute Windows executable path so git's shell-based
    /// editor invocation can run it. Forward slashes survive the MSYS
    /// shell verbatim, and the surrounding double quotes protect against
    /// spaces in <c>%LOCALAPPDATA%</c> or <c>Program Files</c> install
    /// locations.
    /// </summary>
    internal static string ToShellEditorPath(string path)
    {
        var slashed = path.Replace('\\', '/');
        return $"\"{slashed}\"";
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
