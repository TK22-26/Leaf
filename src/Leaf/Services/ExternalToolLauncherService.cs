using System.Diagnostics;
using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Default launcher: substitutes the <c>$LOCAL/$REMOTE/$BASE/$MERGED</c>
/// placeholders in <see cref="ExternalTool.ArgsTemplate"/>, resolves the
/// command via <see cref="IExternalToolDetectorService"/>, and starts the
/// process without a shell. Process launch runs on the thread pool so the
/// UI doesn't hiccup when Beyond Compare's startup takes half a second.
/// </summary>
public sealed class ExternalToolLauncherService : IExternalToolLauncherService
{
    private readonly IExternalToolDetectorService _detector;

    public ExternalToolLauncherService(IExternalToolDetectorService detector)
    {
        _detector = detector;
    }

    public Task<int> LaunchDiffAsync(ExternalTool tool, string localPath, string remotePath, CancellationToken cancellationToken = default)
    {
        if (tool.Kind != ExternalToolKind.Diff)
            throw new ArgumentException($"Tool '{tool.Name}' is not a diff tool (Kind = {tool.Kind}).", nameof(tool));

        var args = ExpandTemplate(tool.ArgsTemplate,
            local: localPath,
            remote: remotePath,
            baseFile: null,
            merged: null);

        return LaunchAsync(tool, args, cancellationToken);
    }

    public Task<int> LaunchMergeAsync(ExternalTool tool, string basePath, string localPath, string remotePath, string mergedPath, CancellationToken cancellationToken = default)
    {
        if (tool.Kind != ExternalToolKind.Merge)
            throw new ArgumentException($"Tool '{tool.Name}' is not a merge tool (Kind = {tool.Kind}).", nameof(tool));

        var args = ExpandTemplate(tool.ArgsTemplate,
            local: localPath,
            remote: remotePath,
            baseFile: basePath,
            merged: mergedPath);

        return LaunchAsync(tool, args, cancellationToken);
    }

    /// <summary>
    /// Visible for tests. Replaces the four supported placeholders;
    /// calls with a null argument throw if the template references that
    /// placeholder, because that signals a programming mistake (a diff
    /// tool with $BASE in its template, for example).
    /// </summary>
    internal static string ExpandTemplate(string template, string local, string remote, string? baseFile, string? merged)
    {
        var result = template
            .Replace("$LOCAL", local)
            .Replace("$REMOTE", remote);

        if (result.Contains("$BASE"))
        {
            if (baseFile == null)
                throw new InvalidOperationException("Args template references $BASE but no base path was supplied.");
            result = result.Replace("$BASE", baseFile);
        }

        if (result.Contains("$MERGED"))
        {
            if (merged == null)
                throw new InvalidOperationException("Args template references $MERGED but no merged path was supplied.");
            result = result.Replace("$MERGED", merged);
        }

        return result;
    }

    private async Task<int> LaunchAsync(ExternalTool tool, string args, CancellationToken cancellationToken)
    {
        var resolvedCommand = await _detector.ResolveCommandPathAsync(tool, cancellationToken).ConfigureAwait(false);
        if (resolvedCommand == null)
        {
            throw new InvalidOperationException(
                $"External tool '{tool.DisplayName}' could not be located. " +
                $"Checked PATH and known install locations for '{tool.Command}'.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = resolvedCommand,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        // Task.Run pushes the synchronous Process.Start off the UI
        // thread. Some GUI tools block briefly during init.
        using var process = await Task.Run(() => Process.Start(psi), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Process.Start returned null for '{resolvedCommand}'.");

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled — do our best to tear down the child
            // rather than leave it orphaned blocking $MERGED writes.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw;
        }

        return process.ExitCode;
    }
}
