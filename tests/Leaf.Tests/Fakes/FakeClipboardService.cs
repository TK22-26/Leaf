using Leaf.Services;

namespace Leaf.Tests.Fakes;

/// <summary>
/// Minimal fake clipboard service — stores whatever was last set and
/// returns it on Get. No OS clipboard interaction.
/// </summary>
public class FakeClipboardService : IClipboardService
{
    public string? LastText { get; private set; }

    public void SetText(string text)
    {
        LastText = text;
    }

    public string? GetText() => LastText;
}
