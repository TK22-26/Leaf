#nullable enable
using System.Text;

namespace Leaf.Services.Merge;

/// <summary>
/// Single source of truth for the merge-resolution prompt + JSON schema.
/// Every <see cref="IAiMergeAssistant"/> provider implementation calls
/// into this — the prompt text and the response shape are identical
/// regardless of which CLI / server actually executes it.
/// </summary>
/// <remarks>
/// <para>
/// Privacy contract (enforced at the type boundary): the only inputs the
/// builder reads are the fields on <see cref="AiResolutionRequest"/>. It
/// has no access to <c>SettingsService</c>, git state, branch names, or
/// the wider file. The corresponding test asserts that nothing
/// extraneous leaks into the prompt.
/// </para>
/// <para>
/// The prompt is deliberately short and factual: stating the role, the
/// shape of the response, and feeding the four content blocks (base /
/// ours / theirs / context). Prompt engineering for individual provider
/// quirks (e.g. "claude likes markdown headers", "gemini wants
/// imperative") is intentionally NOT done here — the architectural
/// promise is that providers are interchangeable.
/// </para>
/// </remarks>
public static class AiMergePromptBuilder
{
    /// <summary>
    /// JSON schema constraining the response. Adapters that support
    /// schema-output (Claude inline, Codex via temp file) feed this to
    /// the CLI; Gemini ignores it and the prompt re-states the shape
    /// in plain English so output still validates.
    /// </summary>
    public static string ResolutionJsonSchema => """
        {
          "type": "object",
          "properties": {
            "proposedText": { "type": "string" },
            "rationale":    { "type": "string" },
            "confidence":   { "type": "string", "enum": ["high", "medium", "low"] }
          },
          "required": ["proposedText", "rationale", "confidence"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Build the prompt body sent to the AI. Reads only fields on
    /// <paramref name="request"/>; never reaches into git / settings /
    /// other files.
    /// </summary>
    public static string BuildPrompt(AiResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sb = new StringBuilder();
        sb.AppendLine("You are resolving a 3-way merge conflict in a source file.");
        sb.AppendLine("Return ONLY a single JSON object matching the schema below; no prose, no commentary, no code fences.");
        sb.AppendLine();
        sb.AppendLine("Response schema:");
        sb.AppendLine(ResolutionJsonSchema);
        sb.AppendLine();
        sb.AppendLine($"File: {request.FilePath}");
        sb.AppendLine($"Language: {request.Language}");
        sb.AppendLine();
        AppendBlock(sb, "Context BEFORE the conflict (informational only — do not include in proposedText):", request.ContextBefore);
        AppendBlock(sb, "BASE (common ancestor — what both sides started from):", request.BaseLines);
        AppendBlock(sb, "OURS (current branch's version):", request.OursLines);
        AppendBlock(sb, "THEIRS (incoming branch's version):", request.TheirsLines);
        AppendBlock(sb, "Context AFTER the conflict (informational only — do not include in proposedText):", request.ContextAfter);
        sb.AppendLine();
        sb.AppendLine("proposedText must contain ONLY the merged conflict region (LF line endings, no trailing newline unless the original block had one). Do NOT echo the surrounding context.");
        sb.AppendLine("rationale: one sentence explaining the merge decision.");
        sb.AppendLine("confidence: \"high\" if the merge is mechanically obvious, \"low\" if you are guessing about intent, \"medium\" otherwise.");
        return sb.ToString();
    }

    private static void AppendBlock(StringBuilder sb, string label, IReadOnlyList<string> lines)
    {
        sb.AppendLine(label);
        if (lines.Count == 0)
        {
            sb.AppendLine("(empty)");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("```");
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
        sb.AppendLine("```");
        sb.AppendLine();
    }
}
