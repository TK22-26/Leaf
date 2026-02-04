using System.Text.Json;

namespace Leaf.Services;

/// <summary>
/// Parses AI provider output to extract commit message and description.
/// Handles JSON, labeled output formats, and provider-specific response structures.
/// </summary>
public class CommitMessageParser : ICommitMessageParser
{
    /// <inheritdoc/>
    public (string? message, string? description, string? error) Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return (null, null, "empty output");
        }

        if (TryParseCommitJson(output, out var jsonMessage, out var jsonDescription, out var jsonError))
        {
            return (jsonMessage, jsonDescription, null);
        }

        if (TryParseLabeledOutput(output, out var labeledMessage, out var labeledDescription, out var labeledError))
        {
            return (labeledMessage, labeledDescription, null);
        }

        return (null, null, jsonError ?? labeledError ?? "unable to parse output");
    }

    /// <summary>
    /// Extracts the agent_message from Codex JSONL output.
    /// Looking for: {"type":"item.completed","item":{"type":"agent_message","text":"..."}}
    /// </summary>
    public static string ExtractCodexJsonlMessage(string jsonlOutput)
    {
        foreach (var line in jsonlOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "item.completed" &&
                    root.TryGetProperty("item", out var itemEl) &&
                    itemEl.TryGetProperty("type", out var itemTypeEl) &&
                    itemTypeEl.GetString() == "agent_message" &&
                    itemEl.TryGetProperty("text", out var textEl))
                {
                    return textEl.GetString() ?? jsonlOutput;
                }
            }
            catch
            {
                // Skip invalid JSON lines
            }
        }

        return jsonlOutput;
    }

    /// <summary>
    /// Extracts structured_output from Claude JSON output.
    /// Looking for: {"type":"result","structured_output":{"commitMessage":"...","description":"..."}}
    /// </summary>
    public static string ExtractClaudeStructuredOutput(string jsonOutput)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            if (root.TryGetProperty("structured_output", out var structuredEl))
            {
                return structuredEl.GetRawText();
            }
        }
        catch
        {
            // Fall through to return original
        }

        return jsonOutput;
    }

    /// <summary>
    /// Extracts response field from Gemini JSON output.
    /// Looking for: {"session_id":"...","response":"...","stats":{...}}
    /// </summary>
    public static string ExtractGeminiResponse(string jsonOutput)
    {
        try
        {
            var payload = ExtractJsonObject(jsonOutput) ?? jsonOutput;
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("response", out var responseEl))
            {
                if (responseEl.ValueKind == JsonValueKind.String)
                    return responseEl.GetString() ?? jsonOutput;
                if (responseEl.ValueKind == JsonValueKind.Object)
                    return responseEl.GetRawText();
            }
        }
        catch
        {
            // Fall through to return original
        }

        return jsonOutput;
    }

    private static bool TryParseCommitJson(string response, out string message, out string description, out string error)
    {
        message = string.Empty;
        description = string.Empty;
        error = string.Empty;

        var json = ExtractJsonObject(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "no JSON object found";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "JSON root is not an object";
                return false;
            }

            if (doc.RootElement.TryGetProperty("commitMessage", out var commitMessageProp))
            {
                message = commitMessageProp.GetString() ?? string.Empty;
            }
            else if (doc.RootElement.TryGetProperty("message", out var messageProp))
            {
                message = messageProp.GetString() ?? string.Empty;
            }
            else if (doc.RootElement.TryGetProperty("commit", out var commitProp))
            {
                message = commitProp.GetString() ?? string.Empty;
            }

            if (doc.RootElement.TryGetProperty("description", out var descriptionProp))
            {
                description = descriptionProp.GetString() ?? string.Empty;
            }
            else if (doc.RootElement.TryGetProperty("body", out var bodyProp))
            {
                description = bodyProp.GetString() ?? string.Empty;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON parse error: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseLabeledOutput(string response, out string message, out string description, out string error)
    {
        message = string.Empty;
        description = string.Empty;
        error = string.Empty;

        var lines = response.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();

        int commitIndex = lines.FindIndex(line => line.TrimStart().StartsWith("Commit message:", StringComparison.OrdinalIgnoreCase));
        int descriptionIndex = lines.FindIndex(line => line.TrimStart().StartsWith("Description:", StringComparison.OrdinalIgnoreCase));

        if (commitIndex == -1 && descriptionIndex == -1)
        {
            error = "no JSON or labeled output found";
            return false;
        }

        if (commitIndex != -1)
        {
            var commitLine = lines[commitIndex];
            var commitValue = commitLine[(commitLine.IndexOf(':') + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(commitValue))
            {
                message = commitValue;
            }
            else
            {
                for (int i = commitIndex + 1; i < lines.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                        continue;
                    if (lines[i].TrimStart().StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                        break;

                    message = lines[i].Trim();
                    break;
                }
            }
        }

        if (descriptionIndex != -1)
        {
            var descriptionLine = lines[descriptionIndex];
            var descriptionValue = descriptionLine[(descriptionLine.IndexOf(':') + 1)..].Trim();

            var descriptionLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(descriptionValue))
            {
                descriptionLines.Add(descriptionValue);
            }

            for (int i = descriptionIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (IsMetadataLine(line))
                {
                    break;
                }
                descriptionLines.Add(line);
            }

            description = string.Join(Environment.NewLine, descriptionLines).TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            error = "commit message missing in labeled output";
            return false;
        }

        return true;
    }

    private static bool IsMetadataLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.TrimStart();
        return trimmed.StartsWith("OpenAI", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("codex", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("tokens", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("--------", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("workdir:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("model:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("provider:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("approval:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("sandbox:", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("session", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("user", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("assistant", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("thinking", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("exec", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("[WorkingChanges]", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("Exception", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ExtractJsonObject(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var cleaned = response.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = cleaned.Split('\n');
            var filtered = lines.Where(line => !line.TrimStart().StartsWith("```", StringComparison.Ordinal));
            cleaned = string.Join("\n", filtered);
        }

        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return cleaned.Substring(start, end - start + 1);
    }
}
