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

        // `git submodule status` gives prefix+sha+path+describe per line.
        // `config --file .gitmodules --list` gives us name/url/branch.
        // Running both in parallel keeps the UI snappy even on repos
        // with many submodules.
        var statusTask = _context.CommandRunner.RunAsync(
            repoPath,
            ["submodule", "status"],
            cancellationToken: cancellationToken);

        var configTask = _context.CommandRunner.RunAsync(
            repoPath,
            ["config", "--file", ".gitmodules", "--list"],
            cancellationToken: cancellationToken);

        await Task.WhenAll(statusTask, configTask);

        var statusResult = await statusTask;
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

        return ParseSubmoduleStatusOutput(statusResult.StandardOutput, moduleConfig);
    }

    /// <summary>
    /// Parse the line-oriented output of <c>git submodule status</c>.
    /// Each line is: <c>&lt;prefix&gt;&lt;sha&gt; &lt;path&gt; [(describe)]</c>
    /// where <c>prefix</c> is one of <c>' '</c>, <c>'-'</c>, <c>'+'</c>,
    /// <c>'U'</c>. Uninitialized entries use the recorded parent SHA
    /// in the sha field (not a working-tree sha, because there is none).
    /// </summary>
    internal static List<SubmoduleInfo> ParseSubmoduleStatusOutput(string output, IReadOnlyDictionary<string, ModuleConfigEntry> moduleConfig)
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

            string path;
            string? describe = null;

            var parenIdx = pathAndDescribe.IndexOf(" (");
            if (parenIdx > 0 && pathAndDescribe.EndsWith(')'))
            {
                path = pathAndDescribe[..parenIdx];
                describe = pathAndDescribe[(parenIdx + 2)..^1];
            }
            else
            {
                path = pathAndDescribe;
            }

            // Normalize to forward slashes (git's native form); Windows
            // users still see the slashes git itself uses.
            path = path.Replace('\\', '/');

            var status = prefix switch
            {
                ' ' => SubmoduleStatus.UpToDate,
                '-' => SubmoduleStatus.Uninitialized,
                '+' => SubmoduleStatus.OutOfSync,
                'U' => SubmoduleStatus.Conflicted,
                _ => SubmoduleStatus.UpToDate,
            };

            byPath.TryGetValue(path, out var cfg);

            // For uninitialized entries, the `sha` is the parent-recorded
            // commit — not a working-tree sha. For everything else, the
            // working-tree commit *is* the output, and the recorded
            // commit comes from the parent's tree; since we don't have
            // that here cheaply, treat them as the same unless we detect
            // out-of-sync. Exception: `+` means they differ, but git
            // doesn't print both in this form. Callers wanting both
            // should use `git diff-index HEAD` on the parent instead.
            var isUninit = status == SubmoduleStatus.Uninitialized;

            result.Add(new SubmoduleInfo
            {
                Name = cfg?.Name ?? path,
                Path = path,
                Url = cfg?.Url ?? string.Empty,
                Branch = cfg?.Branch,
                RecordedSha = sha,
                WorkingSha = isUninit ? null : sha,
                Describe = describe,
                Status = status,
            });
        }

        return result;
    }

    /// <summary>
    /// Parse <c>git config --file .gitmodules --list</c> output into a
    /// name→(path,url,branch) map. Each line is <c>key=value</c> with
    /// keys shaped <c>submodule.&lt;name&gt;.&lt;field&gt;</c>. Anything
    /// else is ignored.
    /// </summary>
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

            if (!map.TryGetValue(name, out var entry))
            {
                entry = new ModuleConfigEntry { Name = name };
                map[name] = entry;
            }

            switch (field)
            {
                case "path": entry.Path = value.Replace('\\', '/'); break;
                case "url": entry.Url = value; break;
                case "branch": entry.Branch = value; break;
            }
        }

        // Drop entries missing a path — they can't be matched to status
        // output, and a submodule without a path in .gitmodules is not
        // a real registration from git's POV.
        foreach (var key in map.Where(kv => string.IsNullOrEmpty(kv.Value.Path)).Select(kv => kv.Key).ToList())
        {
            map.Remove(key);
        }

        return map;
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

        // Step 1 — deinit if initialized. Ignore failures here when
        // the submodule is already uninitialized (common case for a
        // user who registered but never updated); we still want to
        // strip the registration below.
        if (submodule.IsInitialized)
        {
            var deinitResult = await _context.CommandRunner.RunAsync(
                repoPath,
                ["submodule", "deinit", "--force", "--", submodule.Path],
                cancellationToken: cancellationToken);
            if (!deinitResult.Success)
            {
                Log.Warn("Submodule",
                    $"Deinit step failed for '{submodule.Path}' (exit {deinitResult.ExitCode}); continuing with removal. " +
                    (deinitResult.StandardError?.Trim() ?? string.Empty));
            }
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
        var cacheDir = Path.Combine(repoPath, ".git", "modules", submodule.Name.Replace('/', Path.DirectorySeparatorChar));
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
                DeleteTree(dir);
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
    /// built-in recursive delete aborts mid-tree on them.
    /// </summary>
    private static void DeleteTree(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
            File.Delete(file);
        }
        foreach (var child in Directory.EnumerateDirectories(dir))
        {
            DeleteTree(child);
        }
        Directory.Delete(dir, recursive: false);
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
