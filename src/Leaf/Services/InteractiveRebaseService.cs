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
/// <remarks>
/// <para>When a rebase pauses on a conflict, the service writes a marker
/// file at <c>.git/rebase-merge/leaf-rebase-temp</c> pointing at the
/// per-rebase temp dir. <see cref="Git.Operations.RebaseOperations.ContinueRebaseAsync"/>
/// reads that marker on the subsequent <c>git rebase --continue</c> and
/// re-establishes the helper env so reword / squash entries that follow
/// the conflict point still get their custom messages applied. The
/// continue path uses the git CLI (not LibGit2Sharp) so the editor
/// invocation actually fires.</para>
/// </remarks>
public class InteractiveRebaseService : IInteractiveRebaseService
{
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

    public InteractiveRebaseService(IGitCommandRunner commandRunner, IRepositoryEventHub eventHub)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RebaseTodoItem>> LoadPlanAsync(
        IRepositorySession session,
        string fromCommitSha,
        string? upstreamRef = null,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(fromCommitSha))
            throw new ArgumentException("fromCommitSha is required.", nameof(fromCommitSha));

        // The "upstream" is the ref git rebases onto and uses to delimit
        // the range. If the caller supplied one (branch-onto-branch entry
        // point) we use it directly. Otherwise we synthesise it as
        // <fromCommit>^ — the parent of the oldest commit to rewrite —
        // which preserves the "edit these commits in place" semantics of
        // the commit-graph right-click entry point.
        var upstream = string.IsNullOrWhiteSpace(upstreamRef)
            ? $"{fromCommitSha}^"
            : upstreamRef!;

        Log.Info("Rebase", $"LoadPlan: from={fromCommitSha} upstream={upstream}");

        // Validate the upstream resolves (catches root-commit rebases when
        // upstream is `<sha>^` and catches a typo'd branch name when an
        // explicit upstreamRef is in play).
        var probe = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["rev-parse", "--verify", "--quiet", upstream],
            cancellationToken: cancellationToken);
        if (!probe.Success)
        {
            Log.Warn("Rebase", $"LoadPlan refused: upstream '{upstream}' could not be resolved.");
            throw new InvalidOperationException(
                upstreamRef == null
                    ? $"Cannot rebase from {fromCommitSha}: it has no parent (root commit) or doesn't exist. " +
                      "Interactive rebase from the root commit isn't supported in this version."
                    : $"Cannot resolve rebase upstream '{upstreamRef}'.");
        }

        var range = $"{upstream}..HEAD";
        var log = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["log", "--reverse", $"--pretty=format:{LogFormat}", range],
            cancellationToken: cancellationToken);
        if (!log.Success)
        {
            Log.Error("Rebase", $"LoadPlan: git log {range} failed: {log.StandardError.Trim()}");
            throw new InvalidOperationException(
                $"git log {range} failed: {log.StandardError.Trim()}");
        }

        var items = ParseLogRecords(log.StandardOutput);
        Log.Info("Rebase", $"LoadPlan: parsed {items.Count} commit(s) from {range}");
        return items;
    }

    /// <inheritdoc />
    public async Task<MergeResult> StartAsync(
        IRepositorySession session,
        string fromCommitSha,
        IReadOnlyList<RebaseTodoItem> plan,
        string? upstreamRef = null,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(fromCommitSha))
            throw new ArgumentException("fromCommitSha is required.", nameof(fromCommitSha));
        if (plan == null || plan.Count == 0)
            throw new ArgumentException("Plan must contain at least one item.", nameof(plan));

        // Same upstream-resolution policy as LoadPlanAsync — explicit
        // <see langword="ref"/> wins, otherwise fall back to <fromCommit>^.
        var upstream = string.IsNullOrWhiteSpace(upstreamRef)
            ? $"{fromCommitSha}^"
            : upstreamRef!;

        var helper = RebaseHelperResolver.FindHelperPath();
        if (string.IsNullOrEmpty(helper))
        {
            Log.Error("Rebase", $"Start refused: {RebaseHelperResolver.SequenceEditorExecutable} not on disk; helper exe is required.");
            return new MergeResult
            {
                Success = false,
                ErrorMessage =
                    $"{RebaseHelperResolver.SequenceEditorExecutable} is missing from the install directory; " +
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
            // All three artefacts use UTF-8 without BOM, matching git's
            // own preference for these files. File.ReadAllText
            // auto-detects and strips a BOM if present, so the cursor
            // file would survive a BOM-prefixed write — but writing
            // consistently keeps the on-disk format predictable.
            var noBomUtf8 = new UTF8Encoding(false);
            await File.WriteAllTextAsync(cursorFile, "0", noBomUtf8, cancellationToken);

            var (todoContent, messageCount) = MaterialisePlan(plan, messagesDir);
            await File.WriteAllTextAsync(todoFile, todoContent, noBomUtf8, cancellationToken);

            // GIT_EDITOR is overridden only when the plan rewrites commit
            // messages. For pure pick/drop/exec plans we leave git's
            // default editor untouched so an unexpected editor invocation
            // (a hook firing during exec, etc.) hits Notepad rather than
            // our helper — which would refuse to write a message it
            // doesn't have queued.
            var env = RebaseHelperResolver.BuildLaunchEnvironment(
                helper, todoFile, messagesDir, cursorFile,
                overrideGitEditor: messageCount > 0);

            var rebaseArgs = new[] { "rebase", "--interactive", upstream };
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
                Log.Info("Rebase", "Start: rebase completed cleanly.");
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
                // Drop a marker file inside .git/rebase-merge so the
                // shared ContinueRebaseAsync path can re-establish the
                // helper env when the user resolves the conflict and the
                // merge editor calls continue. Without this, reword /
                // squash entries that follow the conflict point would
                // commit with their original messages.
                await WritePauseMarkerAsync(session.GitDirectory, temp.FullName, cancellationToken);

                Log.Info("Rebase", $"Start: rebase paused (exit {result.ExitCode}); conflict or edit-stop requires user action.");
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

            Log.Error("Rebase", $"Start: rebase failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
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

    private static async Task WritePauseMarkerAsync(string gitDirectory, string tempDir, CancellationToken cancellationToken)
    {
        try
        {
            var marker = RebaseHelperResolver.LeafTempMarkerPath(gitDirectory);
            await File.WriteAllTextAsync(marker, tempDir, new UTF8Encoding(false), cancellationToken);
            Log.Info("Rebase", $"Pause marker written: {marker} -> {tempDir}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort. The continue path will fall through to
            // running git without the helper env if the marker is
            // missing — same outcome as a non-Leaf rebase pause.
            Log.Warn("Rebase", $"Failed to write pause marker: {ex.Message}");
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
