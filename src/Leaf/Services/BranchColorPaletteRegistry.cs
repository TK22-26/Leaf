using Leaf.Models;

namespace Leaf.Services;

/// <inheritdoc />
public sealed class BranchColorPaletteRegistry : IBranchColorPaletteRegistry
{
    /// <summary>Stable id for the shipped default palette.</summary>
    public const string DefaultId = "default";

    /// <summary>Stable id for the shipped Okabe-Ito colour-blind-safe palette.</summary>
    public const string OkabeItoId = "okabe-ito";

    /// <summary>Stable id for the shipped pastel palette.</summary>
    public const string PastelId = "pastel";

    /// <summary>Stable id for the shipped high-contrast palette.</summary>
    public const string HighContrastId = "high-contrast";

    private readonly SettingsService _settingsService;
    private readonly object _lock = new();
    private readonly List<BranchColorPalette> _builtIns;

    public BranchColorPaletteRegistry(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _builtIns = BuildBuiltIns();
    }

    public BranchColorPalette Default => _builtIns[0];

    public event EventHandler? PalettesChanged;

    public IReadOnlyList<BranchColorPalette> GetAll()
    {
        var settings = _settingsService.LoadSettings();
        var result = new List<BranchColorPalette>(_builtIns.Count + settings.CustomBranchColorPalettes.Count);
        result.AddRange(_builtIns);

        // Defensive copy — shipping the deserialized objects out would let
        // callers mutate _settingsService's cached list. Custom palettes are
        // forced to IsBuiltIn=false in case a hand-edited settings.json
        // tried to claim built-in status.
        foreach (var custom in settings.CustomBranchColorPalettes)
        {
            if (string.IsNullOrWhiteSpace(custom.Id)) continue;
            // Skip ids that collide with built-ins; the built-in wins.
            if (_builtIns.Any(b => string.Equals(b.Id, custom.Id, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new BranchColorPalette
            {
                Id = custom.Id,
                DisplayName = custom.DisplayName,
                Colors = [.. custom.Colors],
                IsBuiltIn = false,
            });
        }
        return result;
    }

    public BranchColorPalette GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Default;

        var all = GetAll();
        var match = all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        return match ?? Default;
    }

    public void AddOrUpdateCustom(BranchColorPalette palette)
    {
        if (palette is null) throw new ArgumentNullException(nameof(palette));
        if (string.IsNullOrWhiteSpace(palette.Id))
            throw new ArgumentException("Palette id is required", nameof(palette));
        if (IsBuiltInId(palette.Id))
            throw new InvalidOperationException("Built-in palettes cannot be modified. Clone first via CloneBuiltInForEditing.");

        // Reject palettes with no usable colour entries — would make every
        // branch resolve to the registry default and silently mask the
        // user's mistake. Per Engineering Software Policy, fail loudly.
        var usable = palette.ParsedColors();
        if (usable.Count == 0)
            throw new ArgumentException("Palette must contain at least one valid colour", nameof(palette));

        lock (_lock)
        {
            var settings = _settingsService.LoadSettings();
            var list = settings.CustomBranchColorPalettes;
            var idx = list.FindIndex(p => string.Equals(p.Id, palette.Id, StringComparison.OrdinalIgnoreCase));
            var stored = new BranchColorPalette
            {
                Id = palette.Id,
                DisplayName = string.IsNullOrWhiteSpace(palette.DisplayName) ? palette.Id : palette.DisplayName,
                Colors = [.. palette.Colors],
                IsBuiltIn = false,
            };
            if (idx < 0) list.Add(stored);
            else list[idx] = stored;

            _settingsService.SaveSettings(settings);
        }

        PalettesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteCustom(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || IsBuiltInId(id))
            return;

        bool removed;
        lock (_lock)
        {
            var settings = _settingsService.LoadSettings();
            removed = settings.CustomBranchColorPalettes.RemoveAll(
                p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) _settingsService.SaveSettings(settings);
        }

        if (removed) PalettesChanged?.Invoke(this, EventArgs.Empty);
    }

    public BranchColorPalette CloneBuiltInForEditing(BranchColorPalette source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return new BranchColorPalette
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = $"{source.DisplayName} (copy)",
            Colors = [.. source.Colors],
            IsBuiltIn = false,
        };
    }

    private bool IsBuiltInId(string id) =>
        _builtIns.Any(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Construct the four shipped palettes. Hex values picked for WCAG-acceptable
    /// contrast against both light and dark Fluent surface tokens, and tested
    /// to remain distinguishable under deuteranopia / protanopia simulation
    /// for the colour-blind variant.
    /// </summary>
    private static List<BranchColorPalette> BuildBuiltIns() =>
    [
        new BranchColorPalette
        {
            Id = DefaultId,
            DisplayName = "Default",
            IsBuiltIn = true,
            // 12 vibrant, well-spaced hues. Replaces the pre-§5.14 HSL-from-hash
            // generator with a curated set that doesn't drift into mud or neon.
            Colors =
            [
                "#3B82F6", // blue-500
                "#10B981", // emerald-500
                "#F59E0B", // amber-500
                "#EF4444", // red-500
                "#8B5CF6", // violet-500
                "#EC4899", // pink-500
                "#14B8A6", // teal-500
                "#F97316", // orange-500
                "#84CC16", // lime-500
                "#6366F1", // indigo-500
                "#06B6D4", // cyan-500
                "#A855F7", // purple-500
            ],
        },
        new BranchColorPalette
        {
            Id = OkabeItoId,
            DisplayName = "Colour-blind safe (Okabe-Ito)",
            IsBuiltIn = true,
            // Okabe & Ito 2008 — the canonical colour-blind-safe palette.
            // Eight colours distinguishable across deuteranopia, protanopia,
            // and tritanopia. Order matches the published reference.
            Colors =
            [
                "#0072B2", // blue
                "#E69F00", // orange
                "#009E73", // bluish green
                "#CC79A7", // reddish purple
                "#56B4E9", // sky blue
                "#D55E00", // vermillion
                "#F0E442", // yellow
                "#000000", // black — anchors high-contrast endpoints
            ],
        },
        new BranchColorPalette
        {
            Id = PastelId,
            DisplayName = "Pastel",
            IsBuiltIn = true,
            // Lower-saturation tones for users who find the default palette
            // too punchy on long sessions. Still keeps WCAG AA against the
            // Fluent dark surface (#202020-ish).
            Colors =
            [
                "#93C5FD", // blue-300
                "#6EE7B7", // emerald-300
                "#FCD34D", // amber-300
                "#FCA5A5", // red-300
                "#C4B5FD", // violet-300
                "#F9A8D4", // pink-300
                "#5EEAD4", // teal-300
                "#FDBA74", // orange-300
                "#BEF264", // lime-300
                "#A5B4FC", // indigo-300
            ],
        },
        new BranchColorPalette
        {
            Id = HighContrastId,
            DisplayName = "High contrast",
            IsBuiltIn = true,
            // Maximum saturation, primaries-and-secondaries. For users who
            // need the strongest possible visual separation between branches
            // (and the Windows High Contrast theme).
            Colors =
            [
                "#0066FF",
                "#FF0000",
                "#00CC44",
                "#FF8800",
                "#CC00FF",
                "#FFCC00",
                "#00CCCC",
                "#FF0099",
            ],
        },
    ];
}
