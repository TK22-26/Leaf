#nullable enable
using Leaf.Models.Merge;

namespace Leaf.Services.Merge;

/// <summary>
/// Provider-agnostic contract for AI-assisted conflict resolution. Implementations
/// talk to an MCP server (Model Context Protocol) over stdio, so the WPF client
/// never binds directly to Anthropic/OpenAI/local-model SDKs — swapping backends
/// is a settings change, not a recompile.
/// </summary>
/// <remarks>
/// <para>
/// The request payload is strictly bounded: only the conflict's base / ours /
/// theirs content plus a small configurable context window around it (default
/// ±20 lines, hard cap ±200), file path, and inferred language. Branch names,
/// full-file content, and other Leaf-private state are never sent.
/// </para>
/// <para>
/// The server runs out-of-process and owns all API keys — this is both a
/// privacy and a security boundary. The interface exposes no keys; the MCP
/// server path is the only Leaf-side configuration.
/// </para>
/// </remarks>
public interface IAiMergeAssistant
{
    /// <summary>
    /// Whether the user has enabled the AI merge feature in settings. Lets
    /// callers decide whether to show the "Ask AI" affordance at all.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Whether the user has acknowledged the first-run consent dialog. When
    /// <c>false</c>, the VM should fire its consent-request event before
    /// invoking <see cref="RequestResolutionAsync"/>.
    /// </summary>
    bool IsConsentGiven { get; }

    /// <summary>
    /// Absolute path to the configured MCP server executable, or <c>null</c>
    /// if none is set. Surfaced to the consent dialog so the user knows
    /// where their data is headed.
    /// </summary>
    string? McpServerPath { get; }

    /// <summary>
    /// Ask the configured MCP server for a proposed resolution. Returns <c>null</c>
    /// when the feature is disabled or the user has not granted consent for this
    /// session. Throws on transport errors so the caller can surface them.
    /// </summary>
    Task<AiResolution?> RequestResolutionAsync(
        AiResolutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Input payload sent to the MCP server.</summary>
public sealed record AiResolutionRequest(
    string FilePath,
    string Language,
    IReadOnlyList<string> BaseLines,
    IReadOnlyList<string> OursLines,
    IReadOnlyList<string> TheirsLines,
    IReadOnlyList<string> ContextBefore,
    IReadOnlyList<string> ContextAfter);

/// <summary>Output from the MCP server.</summary>
/// <param name="ProposedText">
/// The suggested merged text for the conflict region, in LF line endings.
/// The caller applies this verbatim as <c>ResolutionState.Manual</c> if accepted.
/// </param>
/// <param name="Rationale">Short human-readable explanation of the AI's reasoning.</param>
/// <param name="Confidence">Server-derived confidence tier.</param>
public sealed record AiResolution(
    string ProposedText,
    string Rationale,
    AiConfidence Confidence);

public enum AiConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Thrown when the MCP transport fails (server not found, non-zero exit, malformed
/// response). Wraps the underlying exception so consumers can log without trying
/// to interpret provider-specific error shapes.
/// </summary>
public sealed class AiMergeAssistantException : Exception
{
    public AiMergeAssistantException(string message) : base(message) { }
    public AiMergeAssistantException(string message, Exception innerException) : base(message, innerException) { }
}
