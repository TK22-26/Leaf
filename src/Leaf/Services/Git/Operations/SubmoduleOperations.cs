using System.IO;
using Leaf.Models;
using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Operations for reading git submodule state. Phase 1 is read-only —
/// listing, status detection, URL/branch lookup. Mutating operations
/// (init/update/add/remove) live on subsequent phases.
/// </summary>
internal class SubmoduleOperations
{
    private readonly IGitOperationContext _context;

    public SubmoduleOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// List all submodules registered in the parent repository. Returns
    /// an empty list when there is no <c>.gitmodules</c> file — that's
    /// the common case for repos without submodules and shouldn't
    /// surface as an error. Recursion is deliberately off: each nested
    /// level would require its own sidebar section, which Phase 1
    /// doesn't aim to render.
    /// </summary>
    public async Task<List<SubmoduleInfo>> GetSubmodulesAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path.Combine(repoPath, ".gitmodules")))
        {
            return [];
        }

        // Three parallel queries:
        //  * `git submodule status` — prefix + *working-tree* SHA +
        //    path + describe. For a `+` (out-of-sync) entry, the SHA
        //    is the submodule HEAD, *not* what the parent records.
        //  * `git submodule status --cached` — prefix + *recorded*
        //    SHA + path. Joining on path gives us both numbers so the
        //    sidebar can show accurate recorded-vs-working without a
        //    per-submodule extra call.
        //  * `config --file .gitmodules --list` — name/url/branch.
        var statusTask = _context.CommandRunner.RunAsync(
            repoPath,
            ["submodule", "status"],
            cancellationToken: cancellationToken);

        var cachedStatusTask = _context.CommandRunner.RunAsync(
            repoPath,
            ["submodule", "status", "--cached"],
            cancellationToken: cancellationToken);

        var configTask = _context.CommandRunner.RunAsync(
            repoPath,
            ["config", "--file", ".gitmodules", "--list"],
            cancellationToken: cancellationToken);

        await Task.WhenAll(statusTask, cachedStatusTask, configTask);

        var statusResult = await statusTask;
        var cachedResult = await cachedStatusTask;
        var configResult = await configTask;

        if (!statusResult.Success)
        {
            throw new InvalidOperationException(
                string.IsNullOrEmpty(statusResult.StandardError)
                    ? "Failed to list submodules"
                    : statusResult.StandardError);
        }

        // A missing .gitmodules was already handled above, so config
        // failure here is unusual — but not fatal: we can still report
        // the entries from `submodule status` with empty URLs rather
        // than throw and surface an error toast to the user.
        var moduleConfig = configResult.Success
            ? ParseGitmodulesConfig(configResult.StandardOutput)
            : new Dictionary<string, ModuleConfigEntry>(StringComparer.Ordinal);

        var recordedByPath = cachedResult.Success
            ? ParseRecordedShaByPath(cachedResult.StandardOutput)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var submodules = ParseSubmoduleStatusOutput(statusResult.StandardOutput, moduleConfig, recordedByPath);

        // Working-tree dirtiness fan-out: per-submodule `git status
        // --porcelain` to detect uncommitted edits / untracked files.
        // Only initialized entries have a working tree to check.
        // Parallel — typical cost is ~30–60 ms total for ~10 submodules
        // on an SSD, comfortably under the per-call cost of `submodule
        // status --cached` (~1.7 s here) we just paid above.
        var initialized = submodules.Where(s => s.IsInitialized).ToList();
        if (initialized.Count > 0)
        {
            await Task.WhenAll(initialized.Select(async s =>
            {
                try
                {
                    s.HasWorkingTreeChanges = await GetWorkingTreeDirtyAsync(repoPath, s.Path, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
                {
                    // Surface the real failure in the log — the per-submodule
                    // dirtiness probe failing isn't fatal to the sidebar load
                    // (the entry still appears with its pointer-side state),
                    // but the user needs a path to diagnose if every probe
                    // fails (e.g. broken git binary, permissions). Privacy:
                    // log includes submodule path, no file content.
                    Log.Warn("Submodule",
                        $"Working-tree dirtiness probe failed for '{s.Path}': {ex.Message}");
                }
            }));
        }

        return submodules;
    }

    /// <summary>
    /// Run <c>git status --porcelain</c> inside the submodule's working
    /// tree and return true when any output is produced (any modified
    /// tracked file, staged change, or untracked file). Throws
    /// <see cref="InvalidOperationException"/> on real failures (missing
    /// directory for an entry the parent thinks is initialised, git exit
    /// non-zero) — caller is responsible for catching + logging so the
    /// dirtiness fan-out doesn't fail the whole sidebar load over one
    /// busted submodule, but a silent "false" would hide actionable
    /// corruption from the user (engineering-software policy: fail
    /// loudly, don't substitute a clean signal).
    /// </summary>
    public async Task<bool> GetWorkingTreeDirtyAsync(string parentRepoPath, string submodulePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(submodulePath))
            throw new ArgumentException("Submodule path required.", nameof(submodulePath));

        var fullPath = Path.GetFullPath(Path.Combine(parentRepoPath, submodulePath));
        if (!Directory.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Submodule '{submodulePath}' is registered as initialised but its working " +
                $"directory '{fullPath}' is missing on disk.");
        }

        var result = await _context.CommandRunner.RunAsync(
            fullPath,
            ["status", "--porcelain", "--untracked-files=normal"],
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            var detail = !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError
                : $"exit code {result.ExitCode}";
            throw new InvalidOperationException(
                $"git status failed inside submodule '{submodulePath}': {detail.Trim()}");
        }

        return !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    /// <summary>
    /// Extract path → recorded-SHA from <c>git submodule status --cached</c>
    /// output. The prefix character is irrelevant for the cached form
    /// (it's always the parent's recorded commit), so we drop it.
    /// </summary>
    internal static Dictionary<string, string> ParseRecordedShaByPath(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(output))
            return map;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 2) continue;

            var rest = line[1..];
            var firstSpace = rest.IndexOf(' ');
            if (firstSpace <= 0) continue;

            var sha = rest[..firstSpace];
            var pathAndDescribe = rest[(firstSpace + 1)..];
            var path = StripDescribe(pathAndDescribe).Replace('\\', '/');
            map[path] = sha;
        }
        return map;
    }

    /// <summary>
    /// Parse the line-oriented output of <c>git submodule status</c>.
    /// Each line is: <c>&lt;prefix&gt;&lt;sha&gt; &lt;path&gt; [(describe)]</c>
    /// where <c>prefix</c> is one of <c>' '</c>, <c>'-'</c>, <c>'+'</c>,
    /// <c>'U'</c>. The SHA in the line is the *working-tree* commit for
    /// up-to-date/out-of-sync/conflicted entries and the *recorded*
    /// parent commit for uninitialized entries. When the caller passes
    /// a non-empty <paramref name="recordedByPath"/> map (produced by
    /// a parallel <c>submodule status --cached</c> run), it is used as
    /// the authoritative source for <see cref="SubmoduleInfo.RecordedSha"/>.
    /// </summary>
    internal static List<SubmoduleInfo> ParseSubmoduleStatusOutput(
        string output,
        IReadOnlyDictionary<string, ModuleConfigEntry> moduleConfig,
        IReadOnlyDictionary<string, string>? recordedByPath = null)
    {
        var result = new List<SubmoduleInfo>();
        if (string.IsNullOrWhiteSpace(output))
            return result;

        // Build a reverse lookup: path -> (name, url, branch). The
        // .gitmodules config is keyed by submodule name; the status
        // output is keyed by path. Most of the time they match, but a
        // user can have `[submodule "libs/foo"]` with path=`vendor/foo`,
        // so we index by path explicitly.
        var byPath = new Dictionary<string, ModuleConfigEntry>(StringComparer.Ordinal);
        foreach (var entry in moduleConfig.Values)
        {
            byPath[entry.Path] = entry;
        }

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 2) continue;

            var prefix = line[0];
            var rest = line[1..];

            // SHA is 40 hex chars; split off path after the space.
            var firstSpace = rest.IndexOf(' ');
            if (firstSpace <= 0) continue;

            var sha = rest[..firstSpace];
            var pathAndDescribe = rest[(firstSpace + 1)..];
            var (path, describe) = SplitPathAndDescribe(pathAndDescribe);

            // Normalize to forward slashes (git's native form); Windows
            // users still see the slashes git itself uses.
            path = path.Replace('\\', '/');

            // Defense-in-depth — git should never emit traversal or
            // rooted paths here, but `ParseGitmodulesConfig` already
            // enforces the same invariant and we want mutation-facing
            // data to pass through a single checkpoint.
            if (!IsSafeRelativeComponent(path))
            {
                Log.Warn("Submodule", $"Rejecting unsafe submodule path '{path}' from submodule-status output");
                continue;
            }

            var status = MapPrefixToStatus(prefix, rawLine);

            byPath.TryGetValue(path, out var cfg);

            var isUninit = status == SubmoduleStatus.Uninitialized;
            // Priority for RecordedSha:
            //  1. The --cached map if available — authoritative.
            //  2. Uninitialized entries: the non-cached form's SHA *is*
            //     the recorded SHA (there is no working tree).
            //  3. UpToDate entries: working == recorded by definition,
            //     so the line's SHA is both.
            //  4. OutOfSync / Conflicted without a cached map: the
            //     line's SHA is the *working* commit, not the recorded
            //     one — leave empty rather than guess wrong.
            string recordedSha;
            if (recordedByPath != null && recordedByPath.TryGetValue(path, out var r))
            {
                recordedSha = r;
            }
            else if (isUninit || status == SubmoduleStatus.UpToDate)
            {
                recordedSha = sha;
            }
            else
            {
                recordedSha = string.Empty;
            }

            result.Add(new SubmoduleInfo
            {
                Name = cfg?.Name ?? path,
                Path = path,
                Url = cfg?.Url ?? string.Empty,
                Branch = cfg?.Branch,
                RecordedSha = recordedSha,
                WorkingSha = isUninit ? null : sha,
                Describe = describe,
                Status = status,
            });
        }

        return result;
    }

    /// <summary>
    /// Split <c>"path (describe)"</c> into path + describe. Uses the
    /// LAST <c>" ("</c> so a path that itself contains <c>" ("</c>
    /// (legal on Linux) still round-trips correctly.
    /// </summary>
    private static (string path, string? describe) SplitPathAndDescribe(string pathAndDescribe)
    {
        var parenIdx = pathAndDescribe.LastIndexOf(" (");
        if (parenIdx > 0 && pathAndDescribe.EndsWith(')'))
        {
            return (pathAndDescribe[..parenIdx], pathAndDescribe[(parenIdx + 2)..^1]);
        }
        return (pathAndDescribe, null);
    }

    /// <summary>
    /// Just the path portion of a <c>"path (describe)"</c> tail — used
    /// by <see cref="ParseRecordedShaByPath"/> where the describe is
    /// not needed.
    /// </summary>
    private static string StripDescribe(string pathAndDescribe)
        => SplitPathAndDescribe(pathAndDescribe).path;

    /// <summary>
    /// Map a <c>git submodule status</c> prefix character to
    /// <see cref="SubmoduleStatus"/>. Unknown prefixes log a warning
    /// and fall through to <see cref="SubmoduleStatus.UpToDate"/> so
    /// a future git version adding a new marker doesn't crash the
    /// sidebar — but we notice via logs.
    /// </summary>
    private static SubmoduleStatus MapPrefixToStatus(char prefix, string rawLine)
    {
        return prefix switch
        {
            ' ' => SubmoduleStatus.UpToDate,
            '-' => SubmoduleStatus.Uninitialized,
            '+' => SubmoduleStatus.OutOfSync,
            'U' => SubmoduleStatus.Conflicted,
            _ => UnknownFallback(prefix, rawLine),
        };

        static SubmoduleStatus UnknownFallback(char prefix, string rawLine)
        {
            Log.Warn("Submodule", $"Unknown status prefix '{prefix}' in line: {rawLine}");
            return SubmoduleStatus.UpToDate;
        }
    }

    /// <summary>
    /// Parse <c>git config --file .gitmodules --list</c> output into a
    /// name→(path,url,branch) map. Each line is <c>key=value</c> with
    /// keys shaped <c>submodule.&lt;name&gt;.&lt;field&gt;</c>. Anything
    /// else is ignored.
    /// </summary>
    /// <remarks>
    /// Security: <c>.gitmodules</c> is user-editable and ships inside
    /// the repo, so a hostile commit could set
    /// <c>[submodule ".."]</c> or absolute paths for <c>name</c> /
    /// <c>path</c>. Leaf later joins those into
    /// <c>.git/modules/&lt;name&gt;</c> for the delete step of
    /// <see cref="RemoveAsync"/>, so a missing guard lets a malicious
    /// repo trick the user into wiping arbitrary directories when they
    /// hit Remove. We filter at parse time — same class of fix git
    /// itself applied for CVE-2018-11235.
    /// </remarks>
    internal static Dictionary<string, ModuleConfigEntry> ParseGitmodulesConfig(string output)
    {
        var map = new Dictionary<string, ModuleConfigEntry>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(output))
            return map;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq];
            var value = line[(eq + 1)..];

            if (!key.StartsWith("submodule.", StringComparison.Ordinal)) continue;

            var rest = key[("submodule.".Length)..];
            var lastDot = rest.LastIndexOf('.');
            if (lastDot <= 0) continue;

            var name = rest[..lastDot];
            var field = rest[(lastDot + 1)..];

            if (!IsSafeRelativeComponent(name))
            {
                Log.Warn("Submodule", $"Rejecting submodule with unsafe name '{name}' from .gitmodules");
                continue;
            }

            if (!map.TryGetValue(name, out var entry))
            {
                entry = new ModuleConfigEntry { Name = name };
                map[name] = entry;
            }

            switch (field)
            {
                case "path":
                    var normalised = value.Replace('\\', '/');
                    if (!IsSafeRelativeComponent(normalised))
                    {
                        Log.Warn("Submodule", $"Rejecting submodule '{name}' with unsafe path '{value}' from .gitmodules");
                        // Leave Path empty so the drop step below purges
                        // this entry entirely — we never want it to
                        // reach RemoveAsync or the sidebar.
                        entry.Path = string.Empty;
                    }
                    else
                    {
                        entry.Path = normalised;
                    }
                    break;
                case "url": entry.Url = value; break;
                case "branch": entry.Branch = value; break;
            }
        }

        // Drop entries missing a path — they can't be matched to status
        // output, and a submodule without a path in .gitmodules is not
        // a real registration from git's POV. Also catches entries we
        // cleared above for safety reasons.
        foreach (var key in map.Where(kv => string.IsNullOrEmpty(kv.Value.Path)).Select(kv => kv.Key).ToList())
        {
            map.Remove(key);
        }

        return map;
    }

    /// <summary>
    /// True when <paramref name="value"/> is safe to join onto a
    /// repository path: not empty, not rooted, contains no
    /// parent-directory segments (<c>..</c>), and uses only forward
    /// slashes (already normalised by the caller). Anything that
    /// fails these checks gets dropped at parse time so the unsafe
    /// data can never reach a filesystem mutation.
    /// </summary>
    internal static bool IsSafeRelativeComponent(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (Path.IsPathRooted(value)) return false;
        if (value.Contains('\\')) return false;

        foreach (var segment in value.Split('/'))
        {
            if (segment is "." or "..") return false;
        }
        return true;
    }

    /// <summary>
    /// Clone + check out the commit the parent tree records for the
    /// given submodule paths. When <paramref name="paths"/> is empty,
    /// initializes and updates every registered submodule. Equivalent
    /// to <c>git submodule update --init [-- path...]</c>, plus
    /// <c>--recursive</c> when nested submodules should follow.
    /// </summary>
    public async Task InitAndUpdateAsync(string repoPath, IReadOnlyList<string> paths, bool recursive, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "submodule", "update", "--init" };
        if (recursive) args.Add("--recursive");
        if (paths.Count > 0)
        {
            args.Add("--");
            args.AddRange(paths);
        }

        await RunAndThrowOnFailure(repoPath, args, "Init/update submodule(s) failed", cancellationToken);
    }

    /// <summary>
    /// Re-read the URLs from <c>.gitmodules</c> and copy them into the
    /// per-repo <c>.git/config</c>. Needed when a submodule's URL
    /// changes upstream — otherwise the old URL stays in local config.
    /// </summary>
    public async Task SyncAsync(string repoPath, IReadOnlyList<string> paths, bool recursive, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "submodule", "sync" };
        if (recursive) args.Add("--recursive");
        if (paths.Count > 0)
        {
            args.Add("--");
            args.AddRange(paths);
        }

        await RunAndThrowOnFailure(repoPath, args, "Sync submodule(s) failed", cancellationToken);
    }

    /// <summary>
    /// Remove the working tree of a submodule and clear its entry from
    /// <c>.git/config</c>. Leaves the registration in <c>.gitmodules</c>
    /// intact — that is a Phase-3 remove concern.
    /// </summary>
    public async Task DeinitAsync(string repoPath, string path, bool force, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "submodule", "deinit" };
        if (force) args.Add("--force");
        args.Add("--");
        args.Add(path);

        await RunAndThrowOnFailure(repoPath, args, $"Deinit submodule '{path}' failed", cancellationToken);
    }

    /// <summary>
    /// Register a new submodule and clone its initial working tree. Mirrors
    /// <c>git submodule add [-b &lt;branch&gt;] &lt;url&gt; &lt;path&gt;</c>.
    /// Caller must stage the resulting changes to <c>.gitmodules</c> + the
    /// new gitlink entry with a follow-up commit.
    /// </summary>
    public async Task AddAsync(string repoPath, string url, string path, string? branch, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL is required.", nameof(url));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var args = new List<string> { "submodule", "add" };
        if (!string.IsNullOrWhiteSpace(branch))
        {
            args.Add("-b");
            args.Add(branch);
        }
        args.Add("--");
        args.Add(url);
        args.Add(path);

        await RunAndThrowOnFailure(repoPath, args, $"Add submodule '{path}' failed", cancellationToken);
    }

    /// <summary>
    /// Pull a submodule to the tip of its tracked branch. Equivalent to
    /// <c>git submodule update --remote [--] &lt;path&gt;</c>. Requires
    /// the submodule to have a <c>branch</c> configured; otherwise git
    /// falls back to HEAD on the remote which is usually not what the
    /// user wants. Callers should only surface this option for
    /// submodules with <see cref="SubmoduleInfo.Branch"/> set.
    /// </summary>
    public async Task UpdateToRemoteAsync(string repoPath, string path, CancellationToken cancellationToken = default)
    {
        await RunAndThrowOnFailure(
            repoPath,
            ["submodule", "update", "--remote", "--", path],
            $"Update submodule '{path}' to remote failed",
            cancellationToken);
    }

    /// <summary>
    /// Fully remove a submodule from the repository. Git makes this a
    /// four-step process and leaves orphan cache data if any step is
    /// skipped; this method wraps the sequence so callers don't have
    /// to remember the order.
    /// </summary>
    /// <remarks>
    /// Steps: deinit (drop local config + working tree), delete
    /// <c>.git/modules/&lt;name&gt;</c> cache, <c>git rm</c> the path
    /// (removes the gitlink and the <c>.gitmodules</c> entry). The
    /// final commit is left to the caller so the user can add a
    /// message — the remove produces staged changes, not a commit.
    /// </remarks>
    public async Task RemoveAsync(string repoPath, SubmoduleInfo submodule, CancellationToken cancellationToken = default)
    {
        if (submodule == null) throw new ArgumentNullException(nameof(submodule));

        // Step 1 — deinit if initialized. A failed deinit means the
        // local `.git/config` still holds `submodule.<name>.*` entries
        // pointing at the cache dir we're about to blow away; silently
        // continuing would leave the repo in a state where a later
        // re-add with the same name fails non-obviously. So: fail
        // loud. For already-uninitialized submodules deinit is a
        // no-op we don't need to run at all.
        if (submodule.IsInitialized)
        {
            await RunAndThrowOnFailure(
                repoPath,
                ["submodule", "deinit", "--force", "--", submodule.Path],
                $"Deinit step of remove for submodule '{submodule.Path}' failed",
                cancellationToken);
        }

        // Step 2 — clear the cached git data. Git writes submodule
        // state under .git/modules/<name> and won't clean this up on
        // its own; without removing it, re-adding with the same name
        // later fails with "already exists in the index".
        //
        // Submodule names may contain forward slashes ("vendor/three")
        // — Path.Combine handles those as sub-segments on Windows.
        // Files inside the cache dir can briefly hold pack-index locks
        // after deinit, so we retry with a short backoff before giving
        // up loudly.
        //
        // Safety: `ParseGitmodulesConfig` already rejects unsafe names,
        // but a belt-and-braces containment check here catches any
        // future code path that constructs a `SubmoduleInfo` without
        // going through the parser. We resolve both sides through
        // `GetFullPath` and require the cache dir to live strictly
        // under `.git/modules/`.
        var modulesRoot = Path.GetFullPath(Path.Combine(repoPath, ".git", "modules"));
        var cacheDir = Path.GetFullPath(Path.Combine(modulesRoot, submodule.Name.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnder(cacheDir, modulesRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to remove submodule '{submodule.Name}': resolved cache path '{cacheDir}' escapes '{modulesRoot}'.");
        }
        if (Directory.Exists(cacheDir))
        {
            await RemoveDirectoryWithRetryAsync(cacheDir, cancellationToken);
        }

        // Step 3 — stage the removal from the tree + .gitmodules.
        // `git rm` handles the two in one shot.
        await RunAndThrowOnFailure(
            repoPath,
            ["rm", "-f", "--", submodule.Path],
            $"Remove submodule '{submodule.Path}' failed",
            cancellationToken);
    }

    /// <summary>
    /// Delete <paramref name="dir"/> recursively. Git pack files and
    /// index files inside <c>.git/modules/&lt;name&gt;</c> carry the
    /// read-only attribute on Windows; a plain
    /// <see cref="Directory.Delete(string, bool)"/> trips over those
    /// with <see cref="UnauthorizedAccessException"/>. This walks the
    /// tree bottom-up, clearing the attribute before each delete.
    /// Remaining transient locks (anti-virus, git's own deinit tail)
    /// get a brief retry with exponential backoff.
    /// </summary>
    private static async Task RemoveDirectoryWithRetryAsync(string dir, CancellationToken cancellationToken)
    {
        const int MaxAttempts = 5;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                DeleteTree(dir, cancellationToken);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MaxAttempts)
            {
                Log.Info("Submodule", $"Cache cleanup attempt {attempt} for '{dir}' failed: {ex.Message}; retrying.");
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Couldn't remove cached submodule data at '{dir}': {ex.Message}. " +
                    "Close any processes inside the submodule and retry.",
                    ex);
            }
        }
    }

    /// <summary>
    /// Bottom-up recursive delete that clears the read-only attribute
    /// before each call. Git's pack files (<c>*.pack</c> / <c>*.idx</c>)
    /// ship with the read-only bit set on Windows; without this, the
    /// built-in recursive delete aborts mid-tree on them. Checks the
    /// cancellation token between entries so a repo-switch mid-delete
    /// bails out promptly instead of churning through a large pack.
    /// Reparse points (junctions / symlinks) are removed as links
    /// without recursing — git never creates them inside
    /// <c>.git/modules</c>, but if one appears we must not follow it
    /// lest we delete the unrelated target.
    /// </summary>
    private static void DeleteTree(string dir, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
            File.Delete(file);
        }
        foreach (var child in Directory.EnumerateDirectories(dir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childAttrs = File.GetAttributes(child);
            if ((childAttrs & FileAttributes.ReparsePoint) != 0)
            {
                // Junction / symlink — drop the link only, not the
                // target it points at.
                Directory.Delete(child, recursive: false);
            }
            else
            {
                DeleteTree(child, cancellationToken);
            }
        }
        Directory.Delete(dir, recursive: false);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> lives at or below
    /// <paramref name="root"/> on disk. Both paths must already be
    /// resolved via <see cref="Path.GetFullPath(string)"/>. Uses
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> because
    /// Windows filesystems are case-insensitive by default.
    /// </summary>
    private static bool IsUnder(string candidate, string root)
    {
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunAndThrowOnFailure(string repoPath, IReadOnlyList<string> args, string message, CancellationToken cancellationToken)
    {
        var result = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken);
        if (!result.Success)
        {
            var detail = !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError
                : result.StandardOutput;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"{message} (exit code {result.ExitCode})"
                    : $"{message}: {detail.Trim()}");
        }
    }

    /// <summary>
    /// Working record while parsing <c>.gitmodules</c> — keeps name/path
    /// together so the status-output parser can join on path.
    /// </summary>
    internal sealed class ModuleConfigEntry
    {
        public required string Name { get; init; }
        public string Path { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? Branch { get; set; }
    }
}
