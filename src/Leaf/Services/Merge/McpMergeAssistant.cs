#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Leaf.Services.Merge;

/// <summary>
/// MCP-stdio-backed implementation of <see cref="IAiMergeAssistant"/>. Spawns
/// a configurable external process (the "merge resolution MCP server") and
/// exchanges JSON payloads over its stdin/stdout. Leaf ships a default server
/// path at <c>tools/leaf-merge-mcp/</c>; users can swap the path in settings
/// to point at any other MCP implementation (local model, Gemini, corporate
/// endpoint, etc.).
/// </summary>
/// <remarks>
/// <para>
/// The wire protocol is intentionally simple — one JSON request object on
/// stdin, one JSON response object on stdout — because full MCP tool-use
/// session management is the MCP server's responsibility, not Leaf's. This
/// keeps the Leaf client identical regardless of which backend is plugged in.
/// </para>
/// <para>
/// The process is started fresh for each request (not persisted), so a
/// crashed or hanging MCP server doesn't leave Leaf with stale state. Trade-off:
/// small per-request spawn cost, worth it for the isolation.
/// </para>
/// </remarks>
public sealed class McpMergeAssistant : IAiMergeAssistant
{
    private readonly Func<string?> _serverPathProvider;
    private readonly Func<bool> _enabledProvider;
    private readonly Func<bool> _consentGivenProvider;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// <paramref name="serverPathProvider"/> returns the absolute path to the MCP
    /// server executable (or <c>null</c> if none configured). <paramref name="enabledProvider"/>
    /// returns whether the user has enabled the feature globally. <paramref name="consentGivenProvider"/>
    /// returns whether the user has acknowledged the first-run consent dialog.
    /// </summary>
    public McpMergeAssistant(
        Func<string?> serverPathProvider,
        Func<bool> enabledProvider,
        Func<bool> consentGivenProvider)
    {
        _serverPathProvider = serverPathProvider ?? throw new ArgumentNullException(nameof(serverPathProvider));
        _enabledProvider = enabledProvider ?? throw new ArgumentNullException(nameof(enabledProvider));
        _consentGivenProvider = consentGivenProvider ?? throw new ArgumentNullException(nameof(consentGivenProvider));
    }

    public bool IsEnabled => _enabledProvider();

    public bool IsConsentGiven => _consentGivenProvider();

    public string? McpServerPath => _serverPathProvider();

    public async Task<AiResolution?> RequestResolutionAsync(
        AiResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Feature gate: silently return null when disabled OR consent not given.
        // The VM treats null as "feature unavailable" and can route the user
        // to the settings / consent UI as appropriate.
        if (!_enabledProvider()) return null;
        if (!_consentGivenProvider()) return null;
        var serverPath = _serverPathProvider();
        if (string.IsNullOrWhiteSpace(serverPath) || !File.Exists(serverPath))
        {
            throw new AiMergeAssistantException(
                "AI merge assistant is enabled but no MCP server is configured. " +
                "Set Settings → AI Merge → MCP Server Path.");
        }

        // Privacy log: timing + outcome only, never request content.
        var sw = Stopwatch.StartNew();
        var outcome = "error";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = serverPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };

            // Wrap the subprocess launch itself: Process.Start can throw
            // Win32Exception (bad PE, permissions), InvalidOperationException
            // (already started), PlatformNotSupportedException (platform quirks).
            // Without this, callers would see a raw framework exception and
            // AsyncErrorHandler would produce a generic "AI failed" toast —
            // violating the VM's contract that AI errors surface through AiError.
            Process process;
            try
            {
                process = Process.Start(psi) ??
                    throw new AiMergeAssistantException($"Could not start MCP server at '{serverPath}'.");
            }
            catch (Win32Exception ex)
            {
                throw new AiMergeAssistantException(
                    $"Could not start MCP server at '{serverPath}' ({ex.Message}).", ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new AiMergeAssistantException(
                    $"Could not start MCP server at '{serverPath}' ({ex.Message}).", ex);
            }
            catch (PlatformNotSupportedException ex)
            {
                throw new AiMergeAssistantException(
                    $"Could not start MCP server at '{serverPath}' ({ex.Message}).", ex);
            }
            using var _proc = process;
            using var reg = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* already exited */ }
            });

            var payload = JsonSerializer.Serialize(new WireRequest(
                Tool: "resolve_conflict",
                FilePath: request.FilePath,
                Language: request.Language,
                BaseLines: request.BaseLines,
                OursLines: request.OursLines,
                TheirsLines: request.TheirsLines,
                ContextBefore: request.ContextBefore,
                ContextAfter: request.ContextAfter), JsonOptions);

            // Start both readers BEFORE writing stdin. A chatty MCP server can
            // fill the (~4–64 KB on Windows) stdout/stderr pipe buffer before
            // reading stdin; if we wait to drain until after write, the server
            // blocks on stdout and we block on stdin, deadlock. Mirrors the
            // GitCommandRunner pattern for the same reason.
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            // If the server exits before consuming stdin (early-exit on a fast
            // error path, or a misconfigured shim), WriteAsync/Close can throw
            // IOException / ObjectDisposedException on a broken pipe. Swallow
            // those here so we can still read whatever stdout + exit code the
            // server managed to produce — the diagnostic is far more useful
            // from stdout/stderr than from "pipe broke mid-write".
            try
            {
                await process.StandardInput.WriteAsync(payload).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException) { /* server exited before consuming stdin */ }
            catch (ObjectDisposedException) { /* stream torn down mid-write */ }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var err = await errorTask.ConfigureAwait(false);
                // Observe the stdout task too so it doesn't become an unobserved
                // faulted Task. The result is unused when we're going to throw.
                try { await outputTask.ConfigureAwait(false); } catch { /* ignored */ }
                throw new AiMergeAssistantException(
                    $"MCP server exited with code {process.ExitCode}: {err.Trim()}");
            }

            var stdout = await outputTask.ConfigureAwait(false);
            // Observe stderr on the success path too — a server that writes
            // diagnostics to stderr while returning a valid result would
            // otherwise leave an unobserved task behind.
            try { await errorTask.ConfigureAwait(false); } catch { /* ignored */ }
            WireResponse? parsed;
            try { parsed = JsonSerializer.Deserialize<WireResponse>(stdout, JsonOptions); }
            catch (JsonException ex)
            {
                throw new AiMergeAssistantException(
                    $"MCP server returned malformed JSON ({ex.Message}).", ex);
            }
            if (parsed is null || string.IsNullOrEmpty(parsed.ProposedText))
            {
                throw new AiMergeAssistantException("MCP server returned an empty resolution.");
            }

            var confidence = parsed.Confidence?.ToLowerInvariant() switch
            {
                "high" => AiConfidence.High,
                "low" => AiConfidence.Low,
                _ => AiConfidence.Medium,
            };
            outcome = "success";
            return new AiResolution(parsed.ProposedText, parsed.Rationale ?? string.Empty, confidence);
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            sw.Stop();
            Log.Info("AiMerge", $"RequestResolution outcome={outcome} duration_ms={sw.ElapsedMilliseconds}");
        }
    }

    // ── Wire types (internal — shape is the MCP request/response contract). ───

    private sealed record WireRequest(
        string Tool,
        string FilePath,
        string Language,
        IReadOnlyList<string> BaseLines,
        IReadOnlyList<string> OursLines,
        IReadOnlyList<string> TheirsLines,
        IReadOnlyList<string> ContextBefore,
        IReadOnlyList<string> ContextAfter);

    private sealed record WireResponse(
        string? ProposedText,
        string? Rationale,
        string? Confidence);
}
