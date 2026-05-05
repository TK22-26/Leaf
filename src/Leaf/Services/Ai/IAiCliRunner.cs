#nullable enable

namespace Leaf.Services.Ai;

/// <summary>
/// Spawns an external CLI executable, pipes a prompt to its stdin, and
/// captures stdout / stderr / exit code. Provider-agnostic — knows nothing
/// about Claude / Gemini / Codex; that knowledge lives in
/// <c>IAiCliAdapter</c> implementations and the per-feature services that
/// build the prompt.
/// </summary>
/// <remarks>
/// <para>
/// This is the shared transport behind both AI commit-message generation
/// and AI merge-conflict resolution. Centralising it here means the
/// PATH-resolution / batch-file / deadlock-avoidance machinery is fixed
/// once across the codebase, instead of being copy-pasted into each
/// caller.
/// </para>
/// <para>
/// The runner does not interpret the CLI's output beyond surfacing it
/// verbatim through <see cref="AiCliProcessResult.Stdout"/>; provider-
/// specific envelope unwrapping (Claude structured output, Codex JSONL,
/// Gemini response wrappers) is the adapter's job, not the runner's.
/// </para>
/// </remarks>
public interface IAiCliRunner
{
    /// <summary>
    /// Run the configured invocation. Throws <see cref="OperationCanceledException"/>
    /// when <paramref name="cancellationToken"/> fires; never throws for a
    /// non-zero exit code or pipe break — those surface through the result
    /// (<see cref="AiCliProcessResult.Success"/> = false, with diagnostic
    /// text in <see cref="AiCliProcessResult.Detail"/>).
    /// </summary>
    Task<AiCliProcessResult> RunAsync(
        AiCliInvocation invocation,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inputs to <see cref="IAiCliRunner.RunAsync"/>. Built by adapters; the
/// runner just executes whatever it's handed.
/// </summary>
/// <param name="Executable">
/// Command name (e.g. <c>"claude"</c>, <c>"gemini"</c>) — resolved against
/// the user's PATH plus <c>.exe</c> / <c>.cmd</c> / <c>.bat</c> extensions
/// — or an absolute path. The runner handles batch-file invocation
/// transparently (Windows can't <see cref="System.Diagnostics.Process.Start(string)"/>
/// a bare <c>.cmd</c>).
/// </param>
/// <param name="Arguments">
/// CLI arguments. Passed through <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>
/// for safe quoting (the batch-file path manually quotes them; see the
/// runner implementation for the rationale).
/// </param>
/// <param name="Stdin">
/// Text written to the process's stdin and then closed. May be empty —
/// some CLIs accept the prompt as an argument instead.
/// </param>
/// <param name="WorkingDirectory">
/// Optional working directory. Codex needs it set to the repo root for its
/// agentic context to find files; other adapters can leave it null.
/// </param>
public sealed record AiCliInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    string Stdin,
    string? WorkingDirectory);

/// <summary>
/// Output from <see cref="IAiCliRunner.RunAsync"/>.
/// </summary>
/// <param name="Success">
/// True when the process exited 0 within the timeout. False on non-zero
/// exit, timeout, missing executable, or pipe-write failure.
/// </param>
/// <param name="ExitCode">
/// Process exit code, or <c>-1</c> when the process never started or was
/// killed before exit (timeout / cancellation).
/// </param>
/// <param name="Stdout">
/// Combined stdout text (UTF-8). Empty string when the process never
/// produced any. Adapter consumers parse this; the runner does not.
/// </param>
/// <param name="Stderr">
/// Combined stderr text (UTF-8). Surfaced both for debugging and for
/// inclusion in <see cref="Detail"/> when the process fails.
/// </param>
/// <param name="Detail">
/// User-facing failure summary when <see cref="Success"/> is false (e.g.
/// <c>"exit 1: command not found"</c>, <c>"timed out after 60s"</c>).
/// Empty on success.
/// </param>
public sealed record AiCliProcessResult(
    bool Success,
    int ExitCode,
    string Stdout,
    string Stderr,
    string Detail);
