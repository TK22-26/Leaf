using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Default implementation — stores tool configuration as standard git
/// config keys. We store one composite value per tool: the <c>cmd</c>
/// line (which Git's own difftool/mergetool can consume directly). The
/// display name is stripped at write time and re-derived from presets at
/// read time.
/// </summary>
public sealed class ExternalToolConfigService : IExternalToolConfigService
{
    private readonly IGitService _gitService;

    public ExternalToolConfigService(IGitService gitService)
    {
        _gitService = gitService;
    }

    public async Task<string?> GetSelectedToolNameAsync(string repoPath, ExternalToolKind kind, CancellationToken cancellationToken = default)
    {
        var key = kind == ExternalToolKind.Diff ? "diff.tool" : "merge.tool";
        var name = await _gitService.GetConfigAsync(repoPath, key, cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public async Task<ExternalTool?> GetCurrentToolAsync(string repoPath, ExternalToolKind kind, CancellationToken cancellationToken = default)
    {
        var selected = await GetSelectedToolNameAsync(repoPath, kind, cancellationToken);
        if (string.IsNullOrEmpty(selected) || selected == ExternalTool.BuiltInName)
        {
            return null;
        }

        // Read the cmd the user has configured. If they picked a known
        // preset name but never wrote a cmd line, fall back to the
        // shipped preset — this is the normal path when Leaf writes its
        // own defaults.
        var cmdKey = $"{ToolKey(kind)}.{selected}.cmd";
        var cmd = await _gitService.GetConfigAsync(repoPath, cmdKey, cancellationToken: cancellationToken);

        var preset = ExternalToolPresets.TryGet(selected, kind);
        if (string.IsNullOrWhiteSpace(cmd))
        {
            return preset;
        }

        var (command, argsTemplate) = SplitCmd(cmd);
        return new ExternalTool(
            Name: selected,
            DisplayName: preset?.DisplayName ?? selected,
            Kind: kind,
            Command: command,
            ArgsTemplate: argsTemplate,
            KnownInstallPaths: preset?.KnownInstallPaths ?? []);
    }

    public async Task SetSelectedToolAsync(string repoPath, ExternalTool tool, GitConfigScope scope, CancellationToken cancellationToken = default)
    {
        if (tool.IsBuiltIn)
        {
            await ClearSelectedToolAsync(repoPath, tool.Kind, scope, cancellationToken);
            return;
        }

        var cmdKey = $"{ToolKey(tool.Kind)}.{tool.Name}.cmd";
        var cmdValue = BuildCmd(tool);
        await _gitService.SetConfigAsync(repoPath, cmdKey, cmdValue, scope, cancellationToken);

        var selectorKey = tool.Kind == ExternalToolKind.Diff ? "diff.tool" : "merge.tool";
        await _gitService.SetConfigAsync(repoPath, selectorKey, tool.Name, scope, cancellationToken);
    }

    public async Task ClearSelectedToolAsync(string repoPath, ExternalToolKind kind, GitConfigScope scope, CancellationToken cancellationToken = default)
    {
        var selectorKey = kind == ExternalToolKind.Diff ? "diff.tool" : "merge.tool";
        await _gitService.UnsetConfigAsync(repoPath, selectorKey, scope, cancellationToken);
    }

    private static string ToolKey(ExternalToolKind kind) =>
        kind == ExternalToolKind.Diff ? "difftool" : "mergetool";

    /// <summary>
    /// Serialise an <see cref="ExternalTool"/> to the single
    /// git-compatible command line. Quoting the command if it contains
    /// spaces lets git launch the tool through <c>sh -c</c> without a
    /// path-resolution surprise.
    /// </summary>
    private static string BuildCmd(ExternalTool tool)
    {
        var commandPart = tool.Command.Contains(' ') && !tool.Command.StartsWith('"')
            ? $"\"{tool.Command}\""
            : tool.Command;

        return string.IsNullOrEmpty(tool.ArgsTemplate)
            ? commandPart
            : $"{commandPart} {tool.ArgsTemplate}";
    }

    /// <summary>
    /// Split a git-style cmd line back into executable + arg template.
    /// Handles the two shapes we actually write: <c>exe args...</c> and
    /// <c>"path with spaces.exe" args...</c>.
    /// </summary>
    internal static (string Command, string ArgsTemplate) SplitCmd(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.StartsWith('"'))
        {
            var closing = cmd.IndexOf('"', 1);
            if (closing > 0)
            {
                var command = cmd.Substring(1, closing - 1);
                var rest = cmd[(closing + 1)..].TrimStart();
                return (command, rest);
            }
        }

        var firstSpace = cmd.IndexOf(' ');
        return firstSpace < 0
            ? (cmd, string.Empty)
            : (cmd[..firstSpace], cmd[(firstSpace + 1)..]);
    }
}
