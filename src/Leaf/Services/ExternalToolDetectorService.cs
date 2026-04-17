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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlySet<string>? _cachedInstalledNames;
    private readonly Dictionary<string, string?> _cachedPaths = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlySet<string>> GetInstalledToolNamesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedInstalledNames != null)
            return _cachedInstalledNames;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedInstalledNames != null)
                return _cachedInstalledNames;

            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Diff and merge presets share names; scanning either set
            // covers both. We iterate presets directly so adding a new
            // tool doesn't require touching detection code.
            foreach (var name in ExternalToolPresets.All.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var diffPreset = ExternalToolPresets.TryGet(name, ExternalToolKind.Diff);
                if (diffPreset == null) continue;

                var resolved = await Task.Run(() => ResolveUncached(diffPreset), cancellationToken).ConfigureAwait(false);
                if (resolved != null)
                {
                    installed.Add(name);
                }
            }

            _cachedInstalledNames = installed;
            return _cachedInstalledNames;
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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedPaths.TryGetValue(key, out cached))
                return cached;

            var resolved = await Task.Run(() => ResolveUncached(tool), cancellationToken).ConfigureAwait(false);
            _cachedPaths[key] = resolved;
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void InvalidateCache()
    {
        _gate.Wait();
        try
        {
            _cachedInstalledNames = null;
            _cachedPaths.Clear();
        }
        finally
        {
            _gate.Release();
        }
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
