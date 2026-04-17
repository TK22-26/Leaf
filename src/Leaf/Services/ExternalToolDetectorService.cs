using System.Collections.Concurrent;
using System.IO;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Default detector: for each preset, tries the known install paths in
/// order, then falls back to a PATH lookup respecting <c>%PATHEXT%</c>.
/// Everything runs on the thread pool via <c>Task.Run</c>; the work is
/// I/O-bound and should not block the UI.
/// </summary>
public sealed class ExternalToolDetectorService : IExternalToolDetectorService
{
    // _gate guards the one-shot scan that builds _cachedInstalledNames.
    // Single-entry path cache (_cachedPaths) uses ConcurrentDictionary
    // directly so concurrent diff-tool launches don't race the unsynced
    // Dictionary mutations the old version had.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IReadOnlySet<string>? _cachedInstalledNames;
    private readonly ConcurrentDictionary<string, string?> _cachedPaths = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlySet<string>> GetInstalledToolNamesAsync(CancellationToken cancellationToken = default)
    {
        // volatile read pairs with the volatile write below so the set's
        // contents are observed once the reference is visible.
        var snapshot = _cachedInstalledNames;
        if (snapshot != null)
            return snapshot;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            snapshot = _cachedInstalledNames;
            if (snapshot != null)
                return snapshot;

            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Iterate every preset name and probe either kind — today
            // every tool ships with both, but a future merge-only (or
            // diff-only) preset would otherwise silently drop off the
            // Detect list. Prefer the Diff variant when both exist
            // because it has the lighter install footprint for tools
            // like Beyond Compare.
            foreach (var name in ExternalToolPresets.All.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var preset = ExternalToolPresets.TryGet(name, ExternalToolKind.Diff)
                             ?? ExternalToolPresets.TryGet(name, ExternalToolKind.Merge);
                if (preset == null) continue;

                var resolved = await Task.Run(() => ResolveUncached(preset), cancellationToken).ConfigureAwait(false);
                if (resolved != null)
                {
                    installed.Add(name);
                }
            }

            _cachedInstalledNames = installed;
            return installed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ResolveCommandPathAsync(ExternalTool tool, CancellationToken cancellationToken = default)
    {
        var key = $"{tool.Name}|{tool.Command}";
        if (_cachedPaths.TryGetValue(key, out var cached))
            return cached;

        // GetOrAdd would race the expensive disk probe; we do it outside
        // the dictionary and let the last writer win. The key includes
        // the command so two calls for the same tool always produce the
        // same resolved path (modulo disk state), making redundant work
        // harmless.
        var resolved = await Task.Run(() => ResolveUncached(tool), cancellationToken).ConfigureAwait(false);
        _cachedPaths[key] = resolved;
        return resolved;
    }

    public void InvalidateCache()
    {
        // Non-blocking: used from UI thread when the user clicks Detect.
        // Writes are visible through the volatile field + ConcurrentDictionary.
        _cachedInstalledNames = null;
        _cachedPaths.Clear();
    }

    private static string? ResolveUncached(ExternalTool tool)
    {
        // Absolute paths win if the user typed one directly.
        if (Path.IsPathRooted(tool.Command) && File.Exists(tool.Command))
            return tool.Command;

        foreach (var candidate in tool.KnownInstallPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (File.Exists(expanded))
                return expanded;
        }

        return FindOnPath(tool.Command);
    }

    /// <summary>
    /// Manual PATH walk — safer than shelling to <c>where.exe</c> because
    /// it doesn't spawn a process per lookup. Honours <c>%PATHEXT%</c>
    /// so bare names like <c>"code"</c> still resolve to <c>code.cmd</c>.
    /// </summary>
    private static string? FindOnPath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT";

        var directories = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var commandHasExt = !string.IsNullOrEmpty(Path.GetExtension(command));

        foreach (var directory in directories)
        {
            try
            {
                if (commandHasExt)
                {
                    var direct = Path.Combine(directory, command);
                    if (File.Exists(direct))
                        return direct;
                }

                foreach (var ext in extensions)
                {
                    var candidate = Path.Combine(directory, command + ext);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (ArgumentException)
            {
                // PATH entry had invalid chars — just skip it.
            }
        }

        return null;
    }
}
