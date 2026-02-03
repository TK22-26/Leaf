namespace Leaf.Services;

/// <summary>
/// Abstraction for clipboard operations to enable testability.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Sets text content to the clipboard.
    /// </summary>
    /// <param name="text">The text to copy to clipboard.</param>
    void SetText(string text);

    /// <summary>
    /// Gets text content from the clipboard.
    /// </summary>
    /// <returns>The text from clipboard, or null if not available.</returns>
    string? GetText();
}
