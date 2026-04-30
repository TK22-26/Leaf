using System.IO;
using System.Windows.Input;
using FluentAssertions;
using Leaf.Services;
using Leaf.Services.Shortcuts;
using Xunit;

namespace Leaf.Tests.Services.Shortcuts;

/// <summary>
/// Pins the contract of the §5.9 shortcut registry. Each test isolates
/// its persisted state by swapping <see cref="SettingsService"/>'s static
/// settings-file path before constructing the service — the alternative
/// (DI + a fake settings service) needs more scaffolding for the same
/// coverage.
/// </summary>
public class ShortcutServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;

    public ShortcutServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "leaf-shortcut-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settings = NewSettingsServiceWithCustomPath(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
    }

    [Fact]
    public void Definitions_AreNonEmpty()
    {
        var sut = new ShortcutService(_settings);
        sut.Definitions.Should().NotBeEmpty(
            because: "ShortcutDefaults registers the App-scope shortcuts at construction.");
        sut.Definitions.Should().Contain(d => d.Id == ShortcutCommandId.View.ToggleTerminal);
    }

    [Fact]
    public void GetGesture_NoOverride_ReturnsRegisteredDefault()
    {
        var sut = new ShortcutService(_settings);
        var defaultGesture = sut.Definitions
            .First(d => d.Id == ShortcutCommandId.View.ToggleTerminal).DefaultGesture;

        sut.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().Be(defaultGesture);
    }

    [Fact]
    public void SetGesture_UnknownId_ThrowsLoudly()
    {
        var sut = new ShortcutService(_settings);
        FluentActions.Invoking(() => sut.SetGesture("does.not.exist", new KeyGesture(Key.X, ModifierKeys.Control)))
            .Should().Throw<ArgumentException>()
            .WithMessage("*Unknown shortcut command id*");
    }

    [Fact]
    public void SetGesture_OverrideThenReadBack_ReturnsOverride()
    {
        var sut = new ShortcutService(_settings);
        var newGesture = new KeyGesture(Key.T, ModifierKeys.Control | ModifierKeys.Shift);

        sut.SetGesture(ShortcutCommandId.View.ToggleTerminal, newGesture);

        sut.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().Match<KeyGesture>(g =>
            g.Key == Key.T && g.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void SetGesture_MatchingDefault_DropsOverride()
    {
        var sut = new ShortcutService(_settings);
        var defaultGesture = sut.GetGesture(ShortcutCommandId.View.ToggleTerminal);

        // First override away, then back to the default. The settings
        // dictionary should end up empty for this id — keeping the
        // persisted file minimal.
        sut.SetGesture(ShortcutCommandId.View.ToggleTerminal, new KeyGesture(Key.X, ModifierKeys.Control));
        sut.SetGesture(ShortcutCommandId.View.ToggleTerminal, defaultGesture);

        var saved = _settings.LoadSettings();
        saved.ShortcutOverrides.Should().NotContainKey(ShortcutCommandId.View.ToggleTerminal,
            because: "matching the default means there's nothing to persist");
    }

    [Fact]
    public void SetGesture_Persists_NewServiceLoadsOverride()
    {
        var first = new ShortcutService(_settings);
        first.SetGesture(ShortcutCommandId.View.ToggleTerminal,
            new KeyGesture(Key.T, ModifierKeys.Alt));

        // Construct a fresh service against the same settings file —
        // this is the round-trip the next app launch sees.
        var second = new ShortcutService(_settings);
        second.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().Match<KeyGesture>(g =>
            g.Key == Key.T && g.Modifiers == ModifierKeys.Alt);
    }

    [Fact]
    public void ResetAll_ClearsOverrides_AndFiresEvent()
    {
        var sut = new ShortcutService(_settings);
        sut.SetGesture(ShortcutCommandId.View.ToggleTerminal, new KeyGesture(Key.T, ModifierKeys.Alt));

        string? lastEventId = "not-fired";
        var fired = false;
        sut.GestureChanged += (_, id) => { fired = true; lastEventId = id; };

        sut.ResetAll();

        fired.Should().BeTrue();
        lastEventId.Should().BeNull(because: "ResetAll fires with null = 'rebuild every binding'");
        var defaultGesture = sut.Definitions
            .First(d => d.Id == ShortcutCommandId.View.ToggleTerminal).DefaultGesture;
        sut.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().Be(defaultGesture);
    }

    [Fact]
    public void GestureChanged_FiresWithCommandId_OnSetGesture()
    {
        var sut = new ShortcutService(_settings);
        string? observedId = null;
        sut.GestureChanged += (_, id) => observedId = id;

        sut.SetGesture(ShortcutCommandId.View.ToggleTerminal, new KeyGesture(Key.T, ModifierKeys.Alt));

        observedId.Should().Be(ShortcutCommandId.View.ToggleTerminal);
    }

    [Fact]
    public void FindConflict_ReturnsId_WhenAnotherCommandHoldsTheGesture()
    {
        var sut = new ShortcutService(_settings);
        var ctrlT = new KeyGesture(Key.T, ModifierKeys.Control);
        sut.SetGesture(ShortcutCommandId.View.ToggleTerminal, ctrlT);

        var conflict = sut.FindConflict(ctrlT, ShortcutScope.Application);

        conflict.Should().Be(ShortcutCommandId.View.ToggleTerminal);
    }

    [Fact]
    public void FindConflict_DifferentScope_DoesNotConflict()
    {
        var sut = new ShortcutService(_settings);
        var defaultGesture = sut.GetGesture(ShortcutCommandId.View.ToggleTerminal);
        defaultGesture.Should().NotBeNull();

        // Same gesture, different scope = no conflict. Merge editor
        // shortcuts can re-use App-scope keys without colliding because
        // the merge editor is a modal child window.
        var conflict = sut.FindConflict(defaultGesture!, ShortcutScope.MergeEditor);

        conflict.Should().BeNull();
    }

    [Fact]
    public void Loader_IgnoresStaleIdsAndContinues()
    {
        // Hand-write a settings file with one valid id and one renamed-
        // away id. The service must load the valid one and skip the
        // stale entry without throwing.
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "shortcutOverrides": {
                "view.toggleTerminal": "Ctrl+Alt+T",
                "renamed.away.in.a.future.version": "Ctrl+X"
              }
            }
            """);

        var sut = new ShortcutService(_settings);

        sut.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().Match<KeyGesture>(g =>
            g.Key == Key.T && g.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt));
    }

    private static SettingsService NewSettingsServiceWithCustomPath(string dir) =>
        new(dir);
}
