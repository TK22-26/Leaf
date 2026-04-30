using System.IO;
using System.Windows.Input;
using FluentAssertions;
using Leaf.Services;
using Leaf.Services.Shortcuts;
using Xunit;

namespace Leaf.Tests.Services.Shortcuts;

/// <summary>
/// Pins the contract of the §5.9 shortcut registry. Each test isolates
/// its persisted state by routing <see cref="SettingsService"/> through
/// its internal app-data-folder ctor (added for testability) so a fresh
/// throwaway directory backs every test instance.
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
    public void Loader_CorruptGesture_FallsThroughToDefault()
    {
        // settings.json with a bogus gesture string for a known id.
        // Service must NOT store the override (which would silently
        // unbind the row); the row should stay on its registered
        // default.
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), """
            {
              "shortcutOverrides": {
                "view.toggleTerminal": "definitely-not-a-keygesture"
              }
            }
            """);

        var sut = new ShortcutService(_settings);
        var def = sut.Definitions.First(d => d.Id == ShortcutCommandId.View.ToggleTerminal);

        sut.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().Be(def.DefaultGesture,
            because: "corrupt persisted gesture should fall through to default, not unbind the row");
    }

    [Fact]
    public void Loader_EmptyStringGesture_PreservesUnboundState()
    {
        // Empty string in JSON means "user explicitly unbound this
        // shortcut" -- the row stays unbound after load.
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), """
            {
              "shortcutOverrides": {
                "view.toggleTerminal": ""
              }
            }
            """);

        var sut = new ShortcutService(_settings);

        sut.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().BeNull(
            because: "empty string is the explicit-unbind sentinel");
    }

    [Fact]
    public void SetGesture_Conflict_UnbindsOtherRow()
    {
        // The Settings UI promises the user that saving a conflicting
        // gesture will unbind the row that previously held it. Pin
        // that contract so we don't regress to the old "both rows hold
        // the same gesture, only the first-registered wins at runtime"
        // behaviour.
        var sut = new ShortcutService(_settings);
        var ctrlT = new KeyGesture(Key.T, ModifierKeys.Control);
        sut.SetGesture(ShortcutCommandId.View.ToggleTerminal, ctrlT);

        var changedIds = new List<string?>();
        sut.GestureChanged += (_, id) => changedIds.Add(id);

        // Reassign Ctrl+T to a different row in the same scope.
        sut.SetGesture(ShortcutCommandId.Repository.Pull, ctrlT);

        sut.GetGesture(ShortcutCommandId.Repository.Pull).Should().Match<KeyGesture>(g =>
            g.Key == Key.T && g.Modifiers == ModifierKeys.Control);
        sut.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().BeNull(
            because: "the previous holder of Ctrl+T should be unbound by the reassignment");
        changedIds.Should().Contain(ShortcutCommandId.View.ToggleTerminal,
            because: "the unbind needs its own GestureChanged so hosts rebuild bindings");
        changedIds.Should().Contain(ShortcutCommandId.Repository.Pull);
    }

    [Fact]
    public void SetGesture_ConflictAcrossScopes_DoesNotReassign()
    {
        // Different scopes don't share a binding table at runtime --
        // App-scope's Ctrl+T and MergeEditor-scope's Ctrl+T target
        // different windows. Reassignment must respect that.
        var sut = new ShortcutService(_settings);
        var ctrlT = new KeyGesture(Key.T, ModifierKeys.Control);
        sut.SetGesture(ShortcutCommandId.View.ToggleTerminal, ctrlT);
        sut.SetGesture(ShortcutCommandId.Merge.AcceptOurs, ctrlT);

        sut.GetGesture(ShortcutCommandId.View.ToggleTerminal).Should().NotBeNull(
            because: "different scope = no conflict, the App-scope binding survives");
        sut.GetGesture(ShortcutCommandId.Merge.AcceptOurs).Should().NotBeNull();
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
