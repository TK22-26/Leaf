#nullable enable
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Adapters;

/// <summary>
/// Adapter for the <c>gemini</c> CLI. Sends the prompt on stdin, requests
/// JSON output, and unwraps the response field from Gemini's wrapper
/// envelope. The schema parameter is unused — Gemini doesn't accept a
/// JSON-schema constraint via flag, so the prompt itself enforces the
/// shape.
/// </summary>
public sealed class GeminiCliAdapter : IAiCliAdapter
{
    public AiProviderKind Provider => AiProviderKind.Gemini;

    public AiCliInvocation BuildInvocation(string prompt, string jsonSchema, string? repoPath)
    {
        // -p "-" reads the prompt body from stdin. --output-format json
        // wraps the response in {"session_id":"...","response":"...","stats":{...}};
        // the adapter's ExtractStructuredOutput unwraps that.
        return new AiCliInvocation(
            Executable: "gemini",
            Arguments: new[] { "-p", "-", "--output-format", "json" },
            Stdin: prompt,
            WorkingDirectory: repoPath);
    }

    public string ExtractStructuredOutput(string rawStdout)
        => CommitMessageParser.ExtractGeminiResponse(rawStdout);
}
