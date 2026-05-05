#nullable enable
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Adapters;

/// <summary>
/// Adapter for the <c>claude</c> CLI. Sends the prompt on stdin and asks
/// Claude to emit JSON conforming to the supplied schema; unwraps the
/// structured output from Claude's response envelope.
/// </summary>
public sealed class ClaudeCliAdapter : IAiCliAdapter
{
    public AiProviderKind Provider => AiProviderKind.Claude;

    public AiCliInvocation BuildInvocation(string prompt, string jsonSchema, string? repoPath)
    {
        // --model sonnet is a rolling alias — Claude updates it across
        // versions, so this stays current automatically. --json-schema
        // accepts the schema inline as a string argument; no temp file
        // needed (unlike Codex). Trailing "-" tells the CLI to read the
        // prompt body from stdin.
        return new AiCliInvocation(
            Executable: "claude",
            Arguments: new[]
            {
                "-p",
                "--model", "sonnet",
                "--output-format", "json",
                "--json-schema", jsonSchema,
                "-",
            },
            Stdin: prompt,
            WorkingDirectory: repoPath);
    }

    public string ExtractStructuredOutput(string rawStdout)
        => CommitMessageParser.ExtractClaudeStructuredOutput(rawStdout);
}
