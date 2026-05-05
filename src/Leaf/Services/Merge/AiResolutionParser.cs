#nullable enable
using System.Text.Json;

namespace Leaf.Services.Merge;

/// <summary>
/// Single source of truth for parsing a merge-resolution response into
/// an <see cref="AiResolution"/>. Every <see cref="IAiMergeAssistant"/>
/// implementation funnels its provider's output through this parser so
/// behaviour (JSON locating, validation, error messages) is identical
/// regardless of which CLI / server actually produced the bytes.
/// </summary>
/// <remarks>
/// Throws <see cref="AiMergeAssistantException"/> on every parse failure
/// — callers don't try / catch for individual cases. The failure modes
/// are:
/// <list type="bullet">
///   <item>response payload above the size cap (defends against a model
///   dumping prose / tool traces into the response)</item>
///   <item>no JSON object anywhere in the response</item>
///   <item>JSON parse failure</item>
///   <item>missing or empty <c>proposedText</c></item>
///   <item>malformed <c>confidence</c> (an unrecognised non-empty value
///   is rejected; absent is tolerated and defaults to Medium).</item>
/// </list>
/// </remarks>
public static class AiResolutionParser
{
    /// <summary>
    /// Hard cap on the raw response we'll attempt to parse. 256 KB is
    /// orders of magnitude more than a sane resolution and keeps us safe
    /// against a runaway model dumping its internal monologue.
    /// </summary>
    private const int MaxResponseBytes = 256 * 1024;

    /// <summary>
    /// Parse <paramref name="rawResponse"/> into an <see cref="AiResolution"/>.
    /// Tolerates leading/trailing prose around a JSON object (some
    /// providers wrap the JSON in text); rejects everything else.
    /// </summary>
    public static AiResolution Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            throw new AiMergeAssistantException("AI provider returned an empty response.");
        }
        if (rawResponse.Length > MaxResponseBytes)
        {
            throw new AiMergeAssistantException(
                $"AI provider response exceeds {MaxResponseBytes / 1024} KB cap; refusing to parse.");
        }

        var jsonText = ExtractJsonObject(rawResponse)
            ?? throw new AiMergeAssistantException(
                "AI provider response did not contain a JSON object.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonText); }
        catch (JsonException ex)
        {
            throw new AiMergeAssistantException(
                $"AI provider returned malformed JSON ({ex.Message}).", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AiMergeAssistantException(
                    "AI provider response JSON root is not an object.");
            }

            var proposedText = ReadStringField(root, "proposedText");
            if (string.IsNullOrEmpty(proposedText))
            {
                throw new AiMergeAssistantException(
                    "AI provider response has no proposedText (or it was empty).");
            }

            var rationale = ReadStringField(root, "rationale") ?? string.Empty;
            var confidence = ReadConfidence(root);

            return new AiResolution(proposedText, rationale, confidence);
        }
    }

    private static string? ReadStringField(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            // Numbers/bools/objects/arrays under a string field are a
            // protocol error — fail loudly rather than coerce.
            _ => throw new AiMergeAssistantException(
                $"AI provider response field '{name}' is not a string."),
        };
    }

    private static AiConfidence ReadConfidence(JsonElement root)
    {
        if (!root.TryGetProperty("confidence", out var el))
        {
            // Absent → Medium. Documented in the contract; a provider that
            // doesn't emit a confidence value isn't malformed, just
            // unopinionated.
            return AiConfidence.Medium;
        }
        if (el.ValueKind == JsonValueKind.Null)
        {
            return AiConfidence.Medium;
        }
        if (el.ValueKind != JsonValueKind.String)
        {
            throw new AiMergeAssistantException(
                "AI provider response field 'confidence' is not a string.");
        }
        var value = el.GetString();
        return value?.ToLowerInvariant() switch
        {
            "high" => AiConfidence.High,
            "medium" => AiConfidence.Medium,
            "low" => AiConfidence.Low,
            null or "" => AiConfidence.Medium,
            // Unknown non-empty value → reject. Garbage in confidence
            // could lead to a misleading "high confidence" UI when the
            // model actually meant something else.
            _ => throw new AiMergeAssistantException(
                $"AI provider returned unrecognised confidence value '{value}'."),
        };
    }

    /// <summary>
    /// Find the first balanced JSON object in <paramref name="text"/>.
    /// Tolerates leading prose ("Sure, here's the resolution:") that
    /// some providers prepend even when asked for JSON only. Returns
    /// null when no balanced object is present.
    /// </summary>
    private static string? ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        if (start < 0) return null;

        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (inString)
            {
                if (c == '\\') { escape = true; continue; }
                if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return text.Substring(start, i - start + 1);
                    break;
            }
        }
        return null;
    }
}
