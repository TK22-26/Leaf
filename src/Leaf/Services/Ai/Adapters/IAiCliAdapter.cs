#nullable enable
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Adapters;

/// <summary>
/// Per-provider knowledge for talking to a CLI: how to construct the
/// invocation (executable, args, stdin handling, working directory) and
/// how to extract a structured payload from the provider's stdout
/// envelope. The runner (<see cref="IAiCliRunner"/>) is the transport;
/// adapters are the provider-specific wrapping around it.
/// </summary>
/// <remarks>
/// One adapter per CLI provider (Claude / Gemini / Codex). Ollama is
/// HTTP, not CLI, so it uses <c>OllamaService</c> directly without an
/// adapter. The adapters are stateless: they read no settings, hold no
/// per-request state, and can therefore be registered as singletons in DI.
/// </remarks>
public interface IAiCliAdapter
{
    /// <summary>Provider tag, used by both UI dispatch and DI resolution.</summary>
    AiProviderKind Provider { get; }

    /// <summary>
    /// Build the CLI invocation that will execute the prompt. Adapters
    /// own provider-specific argument construction (e.g. Claude's
    /// <c>--json-schema</c>, Codex's reasoning-effort flag) — the
    /// runner just runs whatever it's handed.
    /// </summary>
    /// <param name="prompt">User-facing prompt text. Sent on stdin.</param>
    /// <param name="jsonSchema">
    /// JSON schema constraining the response shape. Providers that
    /// support schema-output (Claude, Codex) get it as an arg / file;
    /// providers that don't (Gemini) ignore it and rely on the prompt
    /// to enforce the shape.
    /// </param>
    /// <param name="repoPath">
    /// Working directory for the spawned process. Codex uses it to
    /// resolve relative paths in its agentic context; other adapters
    /// can ignore it.
    /// </param>
    AiCliInvocation BuildInvocation(string prompt, string jsonSchema, string? repoPath);

    /// <summary>
    /// Unwrap whatever envelope the provider wraps its actual response in.
    /// Returns the inner JSON / text the caller actually wanted (the
    /// thing that matches <c>jsonSchema</c>). When the response isn't
    /// wrapped, return <paramref name="rawStdout"/> verbatim.
    /// </summary>
    /// <remarks>
    /// Examples of envelopes:
    /// <list type="bullet">
    ///   <item>Claude <c>--output-format json</c>: <c>{"result": "...inner JSON..."}</c></item>
    ///   <item>Codex <c>--json</c>: JSONL stream where the last <c>"agent_message"</c>
    ///   event carries the structured response.</item>
    ///   <item>Gemini <c>--output-format json</c>: <c>{"response": "..."}</c></item>
    /// </list>
    /// </remarks>
    string ExtractStructuredOutput(string rawStdout);
}
