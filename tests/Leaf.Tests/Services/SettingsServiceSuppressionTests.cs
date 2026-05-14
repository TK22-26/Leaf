#nullable enable
using System.IO;
using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Tests for the per-dialog "Don't show this again" suppression API on
/// <see cref="SettingsService"/>. The persistence is just a JSON
/// dictionary on disk; the value is the answer the user wants
/// remembered (true = always Yes/OK, false = always No/Cancel).
/// </summary>
public class SettingsServiceSuppressionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _service;

    public SettingsServiceSuppressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"leaf-suppr-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new SettingsService(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void GetSuppressedAnswer_NoEntry_ReturnsNull()
    {
        _service.GetSuppressedAnswer("never.set").Should().BeNull();
    }

    [Fact]
    public void SetThenGetSuppressedAnswer_RoundTripsThroughDisk()
    {
        _service.SetSuppressedAnswer("branch.forceDelete", true);

        // Use a fresh service so we exercise the load-from-disk path,
        // not just the in-memory copy that SetSuppressedAnswer mutated.
        var reloaded = new SettingsService(_tempDir);
        reloaded.GetSuppressedAnswer("branch.forceDelete").Should().BeTrue();
    }

    [Fact]
    public void SetSuppressedAnswer_CanStoreFalseAnswer()
    {
        // The dictionary has to support both signed answers — a "Don't ask
        // me again" on a No-click should remember "always No", not vanish.
        _service.SetSuppressedAnswer("dangerous.confirmation", false);

        _service.GetSuppressedAnswer("dangerous.confirmation").Should().BeFalse();
    }

    [Fact]
    public void SetSuppressedAnswer_OverwritesPreviousValue()
    {
        _service.SetSuppressedAnswer("toggle", true);
        _service.SetSuppressedAnswer("toggle", false);

        _service.GetSuppressedAnswer("toggle").Should().BeFalse();
    }

    [Fact]
    public void ClearSuppression_RemovesEntry_ReturnsToDefault()
    {
        _service.SetSuppressedAnswer("branch.forceDelete", true);
        _service.ClearSuppression("branch.forceDelete");

        _service.GetSuppressedAnswer("branch.forceDelete").Should().BeNull();
    }

    [Fact]
    public void ClearSuppression_OnUnknownKey_NoOps()
    {
        // Idempotent so a "reset all hidden dialogs" Settings UI can
        // call it on a stale key without crashing.
        var act = () => _service.ClearSuppression("never.set");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceKey_NoOps(string? key)
    {
        // Defensive: prevents a stray empty string from polluting the
        // dictionary or shadowing real entries.
        _service.SetSuppressedAnswer(key!, true);
        _service.GetSuppressedAnswer(key!).Should().BeNull();
    }

    [Fact]
    public void SuppressedKeys_CoexistWithExistingSettings()
    {
        // Loading a settings file with the new dictionary must not clobber
        // unrelated properties. We assert this indirectly: round-trip the
        // theme alongside the suppression map.
        var settings = _service.LoadSettings();
        settings.Theme = "Dark";
        _service.SaveSettings(settings);

        _service.SetSuppressedAnswer("k1", true);

        var reloaded = new SettingsService(_tempDir).LoadSettings();
        reloaded.Theme.Should().Be("Dark");
        reloaded.SuppressedMessageKeys.Should().ContainKey("k1").WhoseValue.Should().BeTrue();
    }
}
