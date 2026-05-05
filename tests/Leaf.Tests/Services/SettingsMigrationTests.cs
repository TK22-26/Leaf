#nullable enable
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Tests for one-time JSON-level renames in <see cref="SettingsService.LoadSettings"/>.
/// The migration is needed when an <see cref="AppSettings"/> property is renamed
/// — without it, the old key would silently drop its value at typed-deserialization
/// time. New entries live in <c>SettingsService.RenamedSettingsKeys</c>.
/// </summary>
public class SettingsMigrationTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"leaf-settings-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void LoadSettings_MigratesAiMergeMcpServerPath_ToExternalServerPath()
    {
        // Arrange: write a settings.json that uses the legacy key name.
        var legacyJson = """
        {
          "aiMergeEnabled": true,
          "aiMergeConsentGiven": true,
          "aiMergeMcpServerPath": "C:/legacy/server.exe"
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), legacyJson);
        var service = new SettingsService(_tempDir);

        // Act
        var settings = service.LoadSettings();

        // Assert: value moved to the new property, old property gone.
        settings.AiMergeExternalServerPath.Should().Be("C:/legacy/server.exe");
        settings.AiMergeEnabled.Should().BeTrue();
        settings.AiMergeConsentGiven.Should().BeTrue();

        // The migration should also rewrite the file so the legacy key is gone.
        var rewritten = File.ReadAllText(Path.Combine(_tempDir, "settings.json"));
        rewritten.Should().NotContain("aiMergeMcpServerPath");
        rewritten.Should().Contain("aiMergeExternalServerPath");
    }

    [Fact]
    public void LoadSettings_NoLegacyKey_IsNoOp()
    {
        // A fresh settings file with only the new key should round-trip unchanged.
        var freshJson = """
        {
          "aiMergeExternalServerPath": "C:/current/server.exe"
        }
        """;
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(settingsPath, freshJson);
        var beforeMtime = File.GetLastWriteTimeUtc(settingsPath);

        var service = new SettingsService(_tempDir);
        var settings = service.LoadSettings();

        settings.AiMergeExternalServerPath.Should().Be("C:/current/server.exe");

        // Migration shouldn't have rewritten the file when nothing changed.
        File.GetLastWriteTimeUtc(settingsPath).Should().Be(beforeMtime);
    }

    [Fact]
    public void LoadSettings_BothKeysPresent_NewKeyWins()
    {
        // Defensive: hand-edited settings.json could end up with both keys.
        // The new key takes priority — we don't silently overwrite a value
        // the user set under the new name.
        var conflictedJson = """
        {
          "aiMergeMcpServerPath": "C:/old/server.exe",
          "aiMergeExternalServerPath": "C:/new/server.exe"
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), conflictedJson);
        var service = new SettingsService(_tempDir);

        var settings = service.LoadSettings();

        settings.AiMergeExternalServerPath.Should().Be("C:/new/server.exe");

        var rewritten = File.ReadAllText(Path.Combine(_tempDir, "settings.json"));
        rewritten.Should().NotContain("aiMergeMcpServerPath");
    }

    [Fact]
    public void LoadSettings_NewInstall_AiMergeProviderEmpty()
    {
        // No settings file yet — defaults apply. AiMergeProvider stays
        // empty until the user picks one (or the settings UI fills it
        // in via UpdateMergeProviderOptions on first load).
        var service = new SettingsService(_tempDir);
        var settings = service.LoadSettings();
        settings.AiMergeProvider.Should().BeEmpty();
    }

    [Fact]
    public void LoadSettings_MalformedJson_DoesNotThrow_AndReturnsDefaults()
    {
        // Migration must tolerate a corrupt settings file without crashing
        // — the existing LoadSettings catch already returns defaults on
        // JsonException, and the migration shouldn't break that contract.
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "{not valid json");
        var service = new SettingsService(_tempDir);

        var act = () => service.LoadSettings();

        var settings = act.Should().NotThrow().Subject;
        settings.AiMergeExternalServerPath.Should().BeEmpty();
    }
}
