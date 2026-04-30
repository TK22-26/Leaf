using System.Globalization;
using System.IO;
using System.Text;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Default <see cref="IPatchService"/>. Talks to git purely through
/// <see cref="IGitCommandRunner"/> — no LibGit2Sharp. <c>git am</c>
/// invokes commit hooks and (on resolve) the editor; LibGit2Sharp's
/// equivalents bypass both, which would silently break repos that rely
/// on commit-msg / pre-commit hooks. The CLI path keeps Leaf a good
/// citizen alongside other git tooling — see
/// <c>feedback_libgit2sharp_vs_cli.md</c>.
/// </summary>
public class PatchService : IPatchService
{
    private readonly IGitCommandRunner _commandRunner;
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public PatchService(IGitCommandRunner commandRunner, IGitService gitService, IRepositoryEventHub eventHub)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<CreatePatchResult> CreateAsync(
        IRepositorySession session,
        IReadOnlyList<string> commitShas,
        string outputDirectory,
        CreatePatchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (commitShas == null || commitShas.Count == 0)
            throw new ArgumentException("At least one commit is required.", nameof(commitShas));
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        if (options == null) throw new ArgumentNullException(nameof(options));

        Directory.CreateDirectory(outputDirectory);

        var args = new List<string> { "format-patch", "-o", outputDirectory };
        if (options.IncludeBinary) args.Add("--binary");
        // Pass --signoff / --no-signoff explicitly so the dialog state
        // wins over any `format.signoff` value the user has set in
        // their global git config. Without --no-signoff, a global
        // `format.signoff = true` would silently override an unchecked
        // dialog option.
        args.Add(options.SignOff ? "--signoff" : "--no-signoff");
        if (!string.IsNullOrWhiteSpace(options.SubjectPrefix))
            args.Add($"--subject-prefix={options.SubjectPrefix}");

        // Pass commits as explicit positional args. Git format-patch with
        // multiple revs writes them in the order they apply (oldest first),
        // numbered 0001, 0002, ... and prints one path per line on stdout —
        // exactly what we promise callers in the result's Files order.
        args.AddRange(commitShas);

        Log.Info("Patch", $"format-patch: {commitShas.Count} commit(s) -> {outputDirectory}");
        var result = await _commandRunner.RunAsync(
            session.RepositoryPath, args, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            Log.Error("Patch", $"format-patch failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
            return new CreatePatchResult
            {
                Success = false,
                OutputDirectory = outputDirectory,
                ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"git format-patch exited with code {result.ExitCode}."
                    : result.StandardError.Trim(),
            };
        }

        var files = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        Log.Info("Patch", $"format-patch wrote {files.Count} file(s).");
        return new CreatePatchResult
        {
            Success = true,
            OutputDirectory = outputDirectory,
            Files = files,
        };
    }

    /// <inheritdoc />
    public async Task<string> ExportToTextAsync(
        IRepositorySession session,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrWhiteSpace(commitSha))
            throw new ArgumentException("commitSha is required.", nameof(commitSha));

        Log.Info("Patch", $"format-patch --stdout: {commitSha}");
        var result = await _commandRunner.RunAsync(
            session.RepositoryPath,
            ["format-patch", "-1", "--stdout", commitSha],
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            Log.Error("Patch", $"format-patch --stdout failed: {result.StandardError.Trim()}");
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"git format-patch exited with code {result.ExitCode}."
                    : result.StandardError.Trim());
        }

        return result.StandardOutput;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchPreviewItem>> PreviewAsync(
        IReadOnlyList<string> patchFiles,
        CancellationToken cancellationToken = default)
    {
        if (patchFiles == null) throw new ArgumentNullException(nameof(patchFiles));

        var items = new List<PatchPreviewItem>(patchFiles.Count);
        foreach (var path in patchFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(ParseHeaders(path));
        }
        return Task.FromResult<IReadOnlyList<PatchPreviewItem>>(items);
    }

    /// <inheritdoc />
    public async Task<ApplyPatchResult> ApplyAsync(
        IRepositorySession session,
        IReadOnlyList<string> patchFiles,
        ApplyPatchStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (patchFiles == null || patchFiles.Count == 0)
            throw new ArgumentException("At least one patch file is required.", nameof(patchFiles));

        // Refuse if a prior am is paused — running another am on top would
        // either fail in a confusing way or silently merge state. Surface
        // the situation as a hard error so the caller drives the existing
        // continue/skip/abort flow first.
        if (await IsAmInProgressAsync(session, cancellationToken))
        {
            Log.Warn("Patch", "Apply refused: another git am is already in progress.");
            return new ApplyPatchResult
            {
                Success = false,
                ErrorMessage =
                    "A previous 'git am' is still in progress. Resolve it (continue, skip, or abort) before applying new patches.",
            };
        }

        var verb = strategy == ApplyPatchStrategy.Am ? "am" : "apply";
        var args = new List<string> { verb };
        args.AddRange(patchFiles);

        Log.Info("Patch", $"{verb}: {patchFiles.Count} patch(es)");
        var result = await _commandRunner.RunAsync(
            session.RepositoryPath, args, cancellationToken: cancellationToken);

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyBranchesChanged();

        if (result.Success)
        {
            Log.Info("Patch", $"{verb} completed cleanly.");
            return new ApplyPatchResult { Success = true };
        }

        // git am pauses on conflict by leaving .git/rebase-apply behind.
        // git apply is one-shot — it never pauses; a non-zero exit there
        // is a hard failure.
        if (strategy == ApplyPatchStrategy.Am &&
            await IsAmInProgressAsync(session, cancellationToken))
        {
            var conflictSha = TryReadConflictingSha(session.GitDirectory);
            Log.Info("Patch",
                $"am paused on conflict (sha={conflictSha ?? "?"}); user must resolve through the merge editor.");
            _eventHub.NotifyConflictStateChanged();
            return new ApplyPatchResult
            {
                Success = false,
                HasConflicts = true,
                ConflictAtSha = conflictSha ?? string.Empty,
                ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim(),
            };
        }

        Log.Error("Patch", $"{verb} failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        return new ApplyPatchResult
        {
            Success = false,
            ErrorMessage = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"git {verb} exited with code {result.ExitCode}."
                : result.StandardError.Trim(),
        };
    }

    /// <inheritdoc />
    public Task<ApplyPatchResult> ContinueAsync(IRepositorySession session, CancellationToken cancellationToken = default) =>
        RunAmControlVerbAsync(session, _gitService.ContinueAmAsync, cancellationToken);

    /// <inheritdoc />
    public Task<ApplyPatchResult> SkipAsync(IRepositorySession session, CancellationToken cancellationToken = default) =>
        RunAmControlVerbAsync(session, _gitService.SkipAmAsync, cancellationToken);

    /// <inheritdoc />
    public async Task AbortAsync(IRepositorySession session, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));

        try
        {
            await _gitService.AbortAmAsync(session.RepositoryPath, cancellationToken);
        }
        finally
        {
            _eventHub.NotifyCommitHistoryChanged();
            _eventHub.NotifyWorkingDirectoryChanged();
            _eventHub.NotifyBranchesChanged();
            _eventHub.NotifyConflictStateChanged();
        }
    }

    /// <inheritdoc />
    public Task<bool> IsAmInProgressAsync(IRepositorySession session, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        // Single source of truth: IGitService probes .git/rebase-apply/applying,
        // the marker file that distinguishes a paused `git am` from a paused
        // rebase (both backends share rebase-apply/). Forwarding here keeps
        // PatchService and the merge-editor / abort paths in lockstep.
        return _gitService.IsAmInProgressAsync(session.RepositoryPath, cancellationToken);
    }

    private async Task<ApplyPatchResult> RunAmControlVerbAsync(
        IRepositorySession session,
        Func<string, CancellationToken, Task<MergeResult>> op,
        CancellationToken cancellationToken)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));

        var result = await op(session.RepositoryPath, cancellationToken);

        _eventHub.NotifyCommitHistoryChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyBranchesChanged();

        if (result.Success)
        {
            _eventHub.NotifyConflictStateChanged();
            return new ApplyPatchResult { Success = true };
        }

        if (result.HasConflicts)
        {
            var conflictSha = TryReadConflictingSha(session.GitDirectory);
            _eventHub.NotifyConflictStateChanged();
            return new ApplyPatchResult
            {
                Success = false,
                HasConflicts = true,
                ConflictAtSha = conflictSha ?? string.Empty,
            };
        }

        return new ApplyPatchResult
        {
            Success = false,
            ErrorMessage = result.ErrorMessage,
        };
    }

    /// <summary>
    /// Best-effort read of the SHA of the patch <c>git am</c> is currently
    /// stuck on. Reads <c>.git/rebase-apply/next</c> to find the patch
    /// number, then parses the <c>From &lt;sha&gt; …</c> mbox-from line of
    /// the matching <c>0001</c>-style file. Returns null when anything is
    /// unreadable — the caller treats that as "unknown SHA" and the UI
    /// falls back to a generic "conflict in patch" label.
    /// </summary>
    private static string? TryReadConflictingSha(string gitDirectory)
    {
        try
        {
            var rebaseApply = Path.Combine(gitDirectory, "rebase-apply");
            var nextPath = Path.Combine(rebaseApply, "next");
            if (!File.Exists(nextPath)) return null;

            var nextText = File.ReadAllText(nextPath).Trim();
            if (!int.TryParse(nextText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var patchNum)
                || patchNum <= 0)
            {
                return null;
            }

            var patchFile = Path.Combine(rebaseApply, patchNum.ToString("0000", CultureInfo.InvariantCulture));
            if (!File.Exists(patchFile)) return null;

            using var reader = OpenPatchReader(patchFile);
            var firstLine = reader.ReadLine();
            // mbox From line: "From <sha> <date>"
            if (firstLine != null && firstLine.StartsWith("From ", StringComparison.Ordinal))
            {
                var rest = firstLine[5..];
                var sp = rest.IndexOf(' ');
                if (sp > 0) return rest[..sp];
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info("Patch", $"Could not read conflicting-patch SHA: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse the mail-style headers of a <c>format-patch</c> file into a
    /// preview row. <c>From:</c>, <c>Subject:</c>, and <c>Date:</c> are
    /// supported including RFC 2822 continuation lines. Anything else is
    /// flagged with <see cref="PatchPreviewItem.HasParseError"/> so the
    /// UI can warn before applying.
    /// </summary>
    internal static PatchPreviewItem ParseHeaders(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new PatchPreviewItem(
                FilePath: filePath,
                Subject: Path.GetFileName(filePath),
                Author: string.Empty,
                AuthoredWhen: DateTimeOffset.MinValue,
                HasParseError: true);
        }

        try
        {
            using var reader = OpenPatchReader(filePath);
            string? from = null;
            string? subjectRaw = null;
            string? date = null;

            // Header block ends at the first blank line. format-patch always
            // emits a leading "From <sha> <date>" mbox-from line; we treat
            // its absence as a parse error rather than silently surfacing
            // a stripped diff as a "patch".
            var firstLine = reader.ReadLine();
            if (firstLine == null || !firstLine.StartsWith("From ", StringComparison.Ordinal))
            {
                return new PatchPreviewItem(filePath, Path.GetFileName(filePath), string.Empty, DateTimeOffset.MinValue, true);
            }

            string? line;
            string? lastHeader = null;
            var fromBuilder = new StringBuilder();
            var subjBuilder = new StringBuilder();
            var dateBuilder = new StringBuilder();

            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) break;

                // RFC 2822 continuation: a header line that begins with
                // whitespace continues the previous one. Subject often
                // wraps across multiple lines for long messages.
                if ((line[0] == ' ' || line[0] == '\t') && lastHeader != null)
                {
                    var continued = line.TrimStart();
                    switch (lastHeader)
                    {
                        case "from": fromBuilder.Append(' ').Append(continued); break;
                        case "subject": subjBuilder.Append(' ').Append(continued); break;
                        case "date": dateBuilder.Append(' ').Append(continued); break;
                    }
                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon <= 0) { lastHeader = null; continue; }
                var name = line[..colon].Trim().ToLowerInvariant();
                var value = colon + 1 < line.Length ? line[(colon + 1)..].TrimStart() : string.Empty;

                switch (name)
                {
                    case "from":
                        fromBuilder.Clear().Append(value);
                        lastHeader = "from";
                        break;
                    case "subject":
                        subjBuilder.Clear().Append(value);
                        lastHeader = "subject";
                        break;
                    case "date":
                        dateBuilder.Clear().Append(value);
                        lastHeader = "date";
                        break;
                    default:
                        lastHeader = null;
                        break;
                }
            }

            from = fromBuilder.Length > 0 ? fromBuilder.ToString() : null;
            subjectRaw = subjBuilder.Length > 0 ? subjBuilder.ToString() : null;
            date = dateBuilder.Length > 0 ? dateBuilder.ToString() : null;

            if (from == null || subjectRaw == null || date == null)
            {
                return new PatchPreviewItem(filePath, Path.GetFileName(filePath), from ?? string.Empty, DateTimeOffset.MinValue, true);
            }

            var subject = StripPatchPrefix(subjectRaw);
            var when = TryParseRfc2822(date) ?? DateTimeOffset.MinValue;
            var parseError = when == DateTimeOffset.MinValue;

            return new PatchPreviewItem(filePath, subject, from, when, parseError);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Patch", $"Header parse failed for '{filePath}': {ex.Message}");
            return new PatchPreviewItem(filePath, Path.GetFileName(filePath), string.Empty, DateTimeOffset.MinValue, true);
        }
    }

    /// <summary>
    /// Strip a leading <c>[PATCH]</c> / <c>[PATCH 1/3]</c> / <c>[RFC PATCH]</c>
    /// bracketed prefix so the UI shows the real subject. Anything that
    /// doesn't start with <c>[</c> is returned unchanged.
    /// </summary>
    internal static string StripPatchPrefix(string subject)
    {
        var s = subject.TrimStart();
        if (s.Length == 0 || s[0] != '[') return subject.Trim();
        var close = s.IndexOf(']');
        if (close < 0) return subject.Trim();
        return s[(close + 1)..].TrimStart();
    }

    /// <summary>
    /// Parse the RFC 2822 dates that <c>format-patch</c> emits in the
    /// <c>Date:</c> header (e.g. <c>Tue, 29 Apr 2026 10:00:00 +0000</c>).
    /// .NET's <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// rejects the no-colon offset that RFC 2822 mandates, so we hand-roll
    /// the format set: with-day-name, without-day-name, and with-colon-offset
    /// for tools that emit non-strict RFC 2822.
    /// </summary>
    internal static DateTimeOffset? TryParseRfc2822(string text)
    {
        // We strip a "Xxx, " day-of-week prefix before parsing rather
        // than matching it strictly with `ddd`. A wrong day-of-week
        // (rare but seen in hand-edited patches and a few non-git
        // tools) shouldn't fail the parse — the date itself is what
        // matters.
        string[] formats =
        [
            "d MMM yyyy HH:mm:ss zzz",        // colon offset
            "d MMM yyyy HH:mm:ss zz",
        ];

        var normalised = NormaliseOffset(StripDayOfWeek(text));
        if (DateTimeOffset.TryParseExact(
                normalised, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var when))
        {
            return when;
        }

        // Fall back to the lenient parser for anything weirder. This
        // accepts ISO-8601 and a handful of locale-specific shapes.
        if (DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out when))
        {
            return when;
        }
        return null;
    }

    /// <summary>
    /// Drop a leading <c>Xxx, </c> day-of-week prefix if present. We don't
    /// validate it — getting it wrong is a non-fatal diagnostic, not a
    /// reason to refuse the date.
    /// </summary>
    private static string StripDayOfWeek(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length >= 5 && trimmed[3] == ',' && trimmed[4] == ' '
            && char.IsLetter(trimmed[0]) && char.IsLetter(trimmed[1]) && char.IsLetter(trimmed[2]))
        {
            return trimmed[5..];
        }
        return trimmed;
    }

    /// <summary>
    /// Convert a trailing <c>+0000</c> / <c>-0500</c> RFC 2822 offset into
    /// the colon form (<c>+00:00</c> / <c>-05:00</c>) that
    /// <see cref="DateTimeOffset.TryParseExact(string, string[], IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// accepts. Anything that isn't shaped like a four-digit numeric
    /// offset passes through unchanged.
    /// </summary>
    private static string NormaliseOffset(string text)
    {
        if (text.Length < 5) return text;
        var sign = text[^5];
        if (sign != '+' && sign != '-') return text;
        for (var i = text.Length - 4; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i])) return text;
        }
        return string.Concat(text.AsSpan(0, text.Length - 2), ":", text.AsSpan(text.Length - 2));
    }

    /// <summary>
    /// Coerce a raw <c>git config</c> string into a boolean using git's
    /// own rules (see <c>git-config(1)</c>): <c>true/yes/on/1</c> are
    /// truthy, <c>false/no/off/0</c> and explicit empty are falsy. Unknown
    /// values return <c>null</c> so the caller can fall back to its own
    /// default rather than guessing. Null input also returns <c>null</c>
    /// (config key not set) so the call site doesn't have to bifurcate
    /// "unset" from "set to a recognised value".
    /// </summary>
    /// <remarks>
    /// We do this rather than calling <c>git config --bool</c> because
    /// the canonical-bool form of <c>git config --get --bool</c>
    /// returns exit code 1 on missing keys *and* on non-bool values,
    /// making the two failure modes indistinguishable. Parsing on our
    /// side keeps the wire format simple (plain <c>--get</c>) and lets
    /// us preserve "value not set" semantics.
    /// </remarks>
    internal static bool? ParseGitConfigBool(string? raw)
    {
        if (raw is null) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "1" => true,
            "false" or "no" or "off" or "0" or "" => false,
            _ => null,
        };
    }

    private static StreamReader OpenPatchReader(string path)
    {
        // Patches are ASCII / UTF-8 in practice. Some editors prepend a
        // UTF-8 BOM, which would land as a leading U+FEFF on the first
        // line and make the `From `-prefix probe fail. We pass a default
        // encoding of UTF-8 plus detectEncodingFromByteOrderMarks=true so
        // the reader auto-strips a BOM if present and otherwise treats
        // the file as plain UTF-8.
        return new StreamReader(
            path,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
            detectEncodingFromByteOrderMarks: true);
    }
}
