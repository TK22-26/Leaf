using System.Windows.Input;

namespace Leaf.Services.Shortcuts;

/// <summary>
/// Default <see cref="IShortcutService"/>. Reads overrides from
/// <see cref="SettingsService"/>'s <c>AppSettings.ShortcutOverrides</c>
/// dictionary and writes them back on every change.
/// </summary>
public class ShortcutService : IShortcutService
{
    private readonly SettingsService _settings;
    private readonly Dictionary<string, ShortcutDefinition> _definitions =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyGesture?> _overrides =
        new(StringComparer.Ordinal);
    private static readonly KeyGestureConverter GestureConverter = new();

    public ShortcutService(SettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // Defaults are populated once at startup. Wrapping the call here
        // keeps consumer construction order simple — anyone resolving
        // IShortcutService gets a fully-loaded registry without an
        // explicit init step.
        ShortcutDefaults.RegisterAll(this);
        LoadOverrides();
    }

    /// <inheritdoc />
    public IReadOnlyList<ShortcutDefinition> Definitions =>
        // Order is the registration order — ShortcutDefaults groups by
        // category which keeps the Settings UI rendering stable without
        // a separate display-order field.
        _definitionList;
    private readonly List<ShortcutDefinition> _definitionList = new();

    /// <inheritdoc />
    public event EventHandler<string?>? GestureChanged;

    /// <inheritdoc />
    public KeyGesture? GetGesture(string commandId)
    {
        if (_overrides.TryGetValue(commandId, out var overrideGesture))
        {
            return overrideGesture;
        }
        return _definitions.TryGetValue(commandId, out var def) ? def.DefaultGesture : null;
    }

    /// <inheritdoc />
    public void SetGesture(string commandId, KeyGesture? gesture)
    {
        if (!_definitions.ContainsKey(commandId))
        {
            // Rejecting unknown ids loudly is per the Engineering
            // Software Policy — silently storing an override that no
            // host will ever bind would be a debugging nightmare.
            throw new ArgumentException(
                $"Unknown shortcut command id '{commandId}'. " +
                $"Register it in ShortcutDefaults before calling SetGesture.",
                nameof(commandId));
        }

        var defaultGesture = _definitions[commandId].DefaultGesture;
        var clearsOverride = GesturesEqual(gesture, defaultGesture);

        if (clearsOverride)
        {
            // Setting the default = no override needed, drop it so the
            // settings file stays minimal.
            _overrides.Remove(commandId);
        }
        else
        {
            _overrides[commandId] = gesture;
        }

        Persist();
        Log.Info("Shortcuts", $"SetGesture({commandId}) = {Format(gesture)} (default={Format(defaultGesture)})");
        GestureChanged?.Invoke(this, commandId);
    }

    /// <inheritdoc />
    public void ResetAll()
    {
        _overrides.Clear();
        Persist();
        Log.Info("Shortcuts", "ResetAll: dropped every user override");
        GestureChanged?.Invoke(this, null);
    }

    /// <inheritdoc />
    public string? FindConflict(KeyGesture gesture, ShortcutScope scope)
    {
        if (gesture == null) return null;
        foreach (var def in _definitionList)
        {
            if (def.Scope != scope) continue;
            var current = GetGesture(def.Id);
            if (GesturesEqual(current, gesture)) return def.Id;
        }
        return null;
    }

    /// <summary>
    /// Adds a definition to the registry. Called once per shortcut at
    /// startup by <see cref="ShortcutDefaults"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Duplicate id.</exception>
    internal void Register(ShortcutDefinition definition)
    {
        if (_definitions.ContainsKey(definition.Id))
        {
            throw new InvalidOperationException(
                $"Shortcut id '{definition.Id}' is already registered.");
        }
        _definitions.Add(definition.Id, definition);
        _definitionList.Add(definition);
    }

    private void LoadOverrides()
    {
        var settings = _settings.LoadSettings();
        if (settings.ShortcutOverrides == null) return;

        foreach (var (id, gestureString) in settings.ShortcutOverrides)
        {
            if (!_definitions.ContainsKey(id))
            {
                // Stale id from an earlier Leaf version. Don't silently
                // drop — log so we can spot regressions where a renamed
                // shortcut left users without their custom binding.
                Log.Warn("Shortcuts", $"Override for unknown id '{id}' ignored (was the command renamed?).");
                continue;
            }

            var parsed = ParseGesture(gestureString);
            // Empty / null gesture string = "user explicitly unbound this
            // shortcut" — preserved as a null entry in the override map.
            _overrides[id] = parsed;
        }

        Log.Info("Shortcuts", $"Loaded {_overrides.Count} shortcut override(s).");
    }

    private void Persist()
    {
        var settings = _settings.LoadSettings();
        settings.ShortcutOverrides = _overrides
            .ToDictionary(kvp => kvp.Key, kvp => Format(kvp.Value));
        _settings.SaveSettings(settings);
    }

    private static KeyGesture? ParseGesture(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return (KeyGesture?)GestureConverter.ConvertFromInvariantString(value);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException)
        {
            // Corrupt entry from a hand-edited settings file. Falling
            // back to the default beats throwing during startup; we log
            // so the user sees the reason their custom binding didn't
            // come back.
            Log.Warn("Shortcuts", $"Could not parse gesture '{value}': {ex.Message}. Falling back to default.");
            return null;
        }
    }

    private static string Format(KeyGesture? gesture)
    {
        if (gesture == null) return string.Empty;
        return GestureConverter.ConvertToInvariantString(gesture) ?? string.Empty;
    }

    private static bool GesturesEqual(KeyGesture? a, KeyGesture? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Key == b.Key && a.Modifiers == b.Modifiers;
    }
}
