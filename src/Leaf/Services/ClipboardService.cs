using System.Windows;

namespace Leaf.Services;

/// <summary>
/// Production implementation of IClipboardService using System.Windows.Clipboard.
/// </summary>
public class ClipboardService : IClipboardService
{
    /// <inheritdoc />
    public void SetText(string text) => Clipboard.SetText(text);

    /// <inheritdoc />
    public string? GetText() => Clipboard.GetText();
}
