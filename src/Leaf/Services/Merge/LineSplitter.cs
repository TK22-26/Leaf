namespace Leaf.Services.Merge;

/// <summary>
/// Shared line-splitting helpers for the merge engine pipeline. Both the engine
/// (splitting input text for range carving) and the parser (splitting merged output
/// for marker detection) need the same four-step algorithm: detect trailing newline,
/// split on <c>\n</c>, drop the trailing empty element introduced by <c>Split</c>.
/// </summary>
internal static class LineSplitter
{
    /// <summary>
    /// Split <paramref name="text"/> on <c>\n</c> into an array of lines. A trailing
    /// <c>\n</c> does NOT produce an empty trailing element — it is recorded in
    /// <paramref name="hasTrailingNewline"/> instead. CR characters are not removed
    /// by this method; callers that need to defend against them (e.g. when receiving
    /// raw git stdout) should normalise first.
    /// </summary>
    public static string[] Split(string text, out bool hasTrailingNewline)
    {
        if (string.IsNullOrEmpty(text))
        {
            hasTrailingNewline = false;
            return Array.Empty<string>();
        }

        hasTrailingNewline = text[text.Length - 1] == '\n';
        var raw = text.Split('\n');
        var count = hasTrailingNewline && raw.Length > 0 && raw[^1].Length == 0 ? raw.Length - 1 : raw.Length;

        if (count == raw.Length) return raw;

        var lines = new string[count];
        Array.Copy(raw, lines, count);
        return lines;
    }
}
