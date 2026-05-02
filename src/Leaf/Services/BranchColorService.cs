using System.Collections.Generic;
using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Services;

/// <inheritdoc />
public sealed class BranchColorService : IBranchColorService, IDisposable
{
    private static readonly Brush HeadBrush = CreateHeadBrush();

    private readonly RepositoryInfo _repository;
    private readonly SettingsService _settingsService;
    private readonly IRepositoryManagementService _repositoryService;
    private readonly IBranchColorPaletteRegistry _paletteRegistry;
    private readonly GitFlowConfig? _gitFlowConfig;
    private readonly HashSet<string>? _remoteNames;

    // Cached palette + brushes. Rebuilt whenever the active palette changes
    // (settings flip, custom-palette edit, or registry refresh). Brushes
    // are frozen so they're safe to share across the build thread and
    // the render thread.
    private readonly object _paletteLock = new();
    private BranchColorPalette _activePalette;
    private Brush[] _paletteBrushes;

    // Per-brush cache for resolved branch colours. Cleared on every
    // ColorsChanged fire so a setting change immediately re-resolves
    // every branch on the next paint.
    private readonly Dictionary<string, Brush> _resolvedCache = new(StringComparer.OrdinalIgnoreCase);

    public BranchColorService(
        RepositoryInfo repository,
        SettingsService settingsService,
        IRepositoryManagementService repositoryService,
        IBranchColorPaletteRegistry paletteRegistry,
        GitFlowConfig? gitFlowConfig,
        IEnumerable<string>? remoteNames)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _repositoryService = repositoryService ?? throw new ArgumentNullException(nameof(repositoryService));
        _paletteRegistry = paletteRegistry ?? throw new ArgumentNullException(nameof(paletteRegistry));
        _gitFlowConfig = gitFlowConfig?.IsInitialized == true ? gitFlowConfig.Clone() : null;
        _remoteNames = remoteNames != null
            ? new HashSet<string>(remoteNames, StringComparer.OrdinalIgnoreCase)
            : null;

        _activePalette = ResolveActivePalette();
        _paletteBrushes = BuildPaletteBrushes(_activePalette);

        // React to palette changes (custom palette added/edited/deleted) by
        // re-resolving the active palette. Settings-driven changes (e.g. the
        // user picks a different default palette) come through
        // RefreshFromSettings since AppSettings has no events.
        _paletteRegistry.PalettesChanged += OnPalettesChanged;
    }

    public BranchColorPalette ActivePalette
    {
        get { lock (_paletteLock) return _activePalette; }
    }

    public bool HasAnyOverrides => _repository.BranchColorOverrides.Count > 0;

    public event EventHandler? ColorsChanged;

    public Brush GetBranchColor(string branchName)
    {
        if (string.IsNullOrEmpty(branchName))
            return ResolveFromPalette("(unnamed)");

        var normalized = NormalizeBranchName(branchName);

        lock (_resolvedCache)
        {
            if (_resolvedCache.TryGetValue(normalized, out var cached))
                return cached;
        }

        var brush = Resolve(normalized);

        lock (_resolvedCache)
        {
            _resolvedCache[normalized] = brush;
        }
        return brush;
    }

    public Color GetColor(string branchName)
    {
        var brush = GetBranchColor(branchName);
        if (brush is SolidColorBrush solid) return solid.Color;
        // The resolver only ever returns SolidColorBrush instances built
        // from the palette or overrides; if a non-solid sneaks in (theme
        // resource, design-time stub) treat it as opaque transparent so
        // callers don't crash on the cast and we fail visibly instead.
        return Colors.Transparent;
    }

    public bool HasOverride(string branchName)
    {
        var key = NormalizeBranchName(branchName);
        return _repository.BranchColorOverrides.ContainsKey(key);
    }

    public void SetOverride(string branchName, Color color)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException("Branch name is required", nameof(branchName));

        var key = NormalizeBranchName(branchName);
        var hex = BranchColorPalette.FormatColor(color);
        _repository.BranchColorOverrides[key] = hex;
        PersistAndNotify();
    }

    public void ClearOverride(string branchName)
    {
        var key = NormalizeBranchName(branchName);
        if (_repository.BranchColorOverrides.Remove(key))
            PersistAndNotify();
    }

    public void ClearAllOverrides()
    {
        if (_repository.BranchColorOverrides.Count == 0) return;
        _repository.BranchColorOverrides.Clear();
        PersistAndNotify();
    }

    /// <summary>
    /// Re-read the user's chosen palette id from settings and rebuild the
    /// palette + cache if it changed. Called by <c>GitGraphViewModel</c>
    /// after a settings dialog closes — AppSettings has no event channel,
    /// so the explicit pull-on-demand model lets the service stay
    /// settings-passive otherwise.
    /// </summary>
    public void RefreshFromSettings()
    {
        var newPalette = ResolveActivePalette();
        bool changed;
        lock (_paletteLock)
        {
            changed = !ReferenceEquals(_activePalette, newPalette)
                && !string.Equals(_activePalette.Id, newPalette.Id, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                _activePalette = newPalette;
                _paletteBrushes = BuildPaletteBrushes(newPalette);
            }
        }
        if (changed)
        {
            ClearResolvedCache();
            ColorsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Detach from the registry. Called by <c>GitGraphViewModel.Dispose</c>
    /// when the per-repo VM goes away.
    /// </summary>
    public void Dispose()
    {
        _paletteRegistry.PalettesChanged -= OnPalettesChanged;
    }

    // ---- internals ----

    private void OnPalettesChanged(object? sender, EventArgs e)
    {
        var newPalette = ResolveActivePalette();
        lock (_paletteLock)
        {
            _activePalette = newPalette;
            _paletteBrushes = BuildPaletteBrushes(newPalette);
        }
        ClearResolvedCache();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PersistAndNotify()
    {
        // Persist via the management service rather than rolling our own
        // LoadRepositories → mutate-snapshot → SaveRepositories cycle. The
        // management service has the authoritative in-memory repo list and
        // its CreateRepoSnapshot copies BranchColorOverrides verbatim, so
        // every save path stays consistent — and we don't risk overwriting
        // unrelated mutations made on _repository between snapshots.
        _repositoryService.SaveRepositories();

        ClearResolvedCache();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearResolvedCache()
    {
        lock (_resolvedCache) _resolvedCache.Clear();
    }

    private BranchColorPalette ResolveActivePalette()
    {
        var settings = _settingsService.LoadSettings();
        return _paletteRegistry.GetById(settings.DefaultBranchColorPaletteId);
    }

    private static Brush[] BuildPaletteBrushes(BranchColorPalette palette)
    {
        var colors = palette.ParsedColors();
        if (colors.Count == 0)
        {
            // Should never happen — the registry guarantees built-ins are
            // populated and AddOrUpdateCustom rejects empty palettes — but
            // belt-and-braces a single neutral grey rather than crash if
            // someone hand-edits settings.json into an empty Colors list.
            colors = [Color.FromRgb(0x80, 0x80, 0x80)];
        }
        var brushes = new Brush[colors.Count];
        for (int i = 0; i < colors.Count; i++)
        {
            var brush = new SolidColorBrush(colors[i]);
            brush.Freeze();
            brushes[i] = brush;
        }
        return brushes;
    }

    private Brush Resolve(string normalizedName)
    {
        // 1. HEAD label — Leaf accent green w/ transparency. Matches the
        //    pre-§5.14 special case in GraphBuilder so the HEAD chip
        //    keeps its identity-marker look across palette changes.
        if (string.Equals(normalizedName, "HEAD", StringComparison.OrdinalIgnoreCase))
            return HeadBrush;

        // 2. User override — wins over everything else.
        if (_repository.BranchColorOverrides.TryGetValue(normalizedName, out var hex)
            && BranchColorPalette.TryParseColor(hex, out var overrideColor))
        {
            var brush = new SolidColorBrush(overrideColor);
            brush.Freeze();
            return brush;
        }

        // 3. GitFlow semantic colour — only when GitFlow is initialised on
        //    this repo. Returns Transparent when the branch doesn't match
        //    a GitFlow type.
        if (_gitFlowConfig != null)
        {
            var gitFlowBrush = BranchInfo.GetGitFlowColorForName(normalizedName, _gitFlowConfig);
            if (gitFlowBrush != Brushes.Transparent)
                return gitFlowBrush;
        }

        // 4. Fall through to the active palette, indexed by stable hash.
        return ResolveFromPalette(normalizedName);
    }

    private Brush ResolveFromPalette(string normalizedName)
    {
        Brush[] brushes;
        lock (_paletteLock) brushes = _paletteBrushes;

        if (brushes.Length == 0)
            return Brushes.Gray;

        var hash = StableHash(normalizedName);
        var index = (int)(hash % (uint)brushes.Length);
        return brushes[index];
    }

    /// <summary>
    /// Strip a leading remote-name prefix (e.g. <c>origin/foo</c> →
    /// <c>foo</c>) and lowercase the result so the local and remote views
    /// of the same branch share a colour and storage is comparer-agnostic.
    ///
    /// <para>Lowercasing matters because <see cref="RepositoryInfo.BranchColorOverrides"/>
    /// round-trips through <c>System.Text.Json</c>, which discards the
    /// dictionary's <c>StringComparer.OrdinalIgnoreCase</c> on deserialise.
    /// Stable lowercase keys make the lookup work regardless of the
    /// reconstructed comparer. Branch names are technically case-sensitive
    /// in Git, but in practice nobody runs branches that differ only by
    /// case, and the rest of the colour pipeline already treats
    /// <c>main</c>/<c>Main</c> as the same via <c>OrdinalIgnoreCase</c>
    /// comparisons (GitFlow matching, hash equality).</para>
    /// </summary>
    private string NormalizeBranchName(string branchName)
    {
        if (string.IsNullOrEmpty(branchName)) return branchName;

        var working = branchName;
        if (_remoteNames != null)
        {
            var slashIndex = working.IndexOf('/');
            if (slashIndex > 0)
            {
                var prefix = working[..slashIndex];
                if (_remoteNames.Contains(prefix))
                    working = working[(slashIndex + 1)..];
            }
        }
        return working.ToLowerInvariant();
    }

    /// <summary>
    /// djb2-style hash. Stable across runs and process bitness (unlike
    /// <c>string.GetHashCode</c>), so persisted overrides keyed by branch
    /// name resolve to the same palette slot every launch.
    /// </summary>
    private static uint StableHash(string s)
    {
        uint hash = 5381;
        foreach (var c in s)
            hash = ((hash << 5) + hash) ^ c;
        return hash;
    }

    private static Brush CreateHeadBrush()
    {
        // Leaf accent green at 55 % alpha, matching LeafAccentSelectedBrush.
        var brush = new SolidColorBrush(Color.FromArgb(0x88, 0x28, 0xA7, 0x45));
        brush.Freeze();
        return brush;
    }
}
