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
