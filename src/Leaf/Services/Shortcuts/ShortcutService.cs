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

        // If the new gesture conflicts with another shortcut in the
        // same scope, unbind that other shortcut. Without this, both
        // rows would end up with the same gesture and only the
        // first-registered one would fire at runtime — confusing for
        // the user. The Settings UI surfaces an amber warning before
        // the user clicks Save, so the reassignment is opt-in.
        var changedIds = new List<string>();
        if (gesture != null)
        {
            var thisDef = _definitions[commandId];
            foreach (var def in _definitionList)
            {
                if (def.Id == commandId) continue;
                if (def.Scope != thisDef.Scope) continue;
                var other = GetGesture(def.Id);
                if (GesturesEqual(other, gesture))
                {
                    // Unbind the conflicting row by storing an explicit
                    // null override. Setting null on a row whose default
                    // is null is a no-op, but storing it explicitly
                    // ensures the override survives a future
                    // SetGesture(default-equal) check.
                    _overrides[def.Id] = null;
                    changedIds.Add(def.Id);
                    Log.Info("Shortcuts", $"SetGesture({commandId}) unbinds conflicting '{def.Id}' (was {Format(other)}).");
                }
            }
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
        // Fire for the primary id last so a host that rebuilds bindings
        // on each event sees the unbinds first, then the assignment.
        foreach (var id in changedIds) GestureChanged?.Invoke(this, id);
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

            // Distinguish three cases on disk:
            //   1. Empty string  -> user explicitly unbound the shortcut
            //                       (store null in _overrides)
            //   2. Valid gesture -> user override (store the gesture)
            //   3. Garbage       -> corrupt entry; SKIP storing so the
            //                       row falls through to its registered
            //                       default. This matches what the user
            //                       expects from a "reset to default"
            //                       outcome and avoids silently leaving
            //                       the row unbound.
            if (string.IsNullOrEmpty(gestureString))
            {
                _overrides[id] = null;
                continue;
            }
            var parsed = ParseGesture(gestureString);
            if (parsed != null)
            {
                _overrides[id] = parsed;
            }
            // parsed == null + non-empty input means corrupt -> falls
            // through to default. ParseGesture already logged a warning.
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

    /// <summary>
    /// Parse a persisted gesture string. Returns null on parse failure;
    /// the caller (LoadOverrides) treats null as "skip this entry, use
    /// default", so a corrupt user file doesn't silently unbind anything.
    /// </summary>
    private static KeyGesture? ParseGesture(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return (KeyGesture?)GestureConverter.ConvertFromInvariantString(value);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException)
        {
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
