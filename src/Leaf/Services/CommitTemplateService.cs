using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Leaf.Models;

namespace Leaf.Services;

/// <inheritdoc />
public sealed class CommitTemplateService : ICommitTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly SettingsService _settingsService;
    private readonly object _lock = new();

    // Repo-scoped templates loaded from .git/leaf/commit-templates.json on
    // the active repository. Empty list when no repo is open or when the
    // file doesn't exist. Replaced wholesale on SetActiveRepository.
    private List<CommitTemplate> _repoTemplates = [];
    private string? _activeRepositoryPath;

    public CommitTemplateService(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public event EventHandler? TemplatesChanged;

    public IReadOnlyList<CommitTemplate> GetAll()
    {
        var settings = _settingsService.LoadSettings();

        // Compose: built-ins (with user body/regex tweaks applied) → user
        // global templates → repo-scoped templates.
        var result = new List<CommitTemplate>();
        var userOverrides = settings.CommitTemplateOverrides ?? new Dictionary<string, CommitTemplate>();

        foreach (var preset in CommitTemplatePresets.All())
        {
            if (userOverrides.TryGetValue(preset.Id, out var tweaked))
            {
                // Built-in + user tweak — merge: take the user's body/regex,
                // keep the preset's id/name/IsBuiltIn so the row renders
                // as a built-in in the UI but uses the user's content.
                result.Add(new CommitTemplate
                {
                    Id = preset.Id,
                    Name = preset.Name,
                    Body = tweaked.Body,
                    TicketRegex = tweaked.TicketRegex,
                    Scope = CommitTemplateScope.Global,
                    IsBuiltIn = true,
                });
            }
            else
            {
                result.Add(Clone(preset));
            }
        }

        // User global custom templates — anything in CommitTemplates that
        // isn't a preset id (preset ids are reserved keys for tweaks).
        foreach (var template in settings.CommitTemplates ?? [])
        {
            if (string.IsNullOrWhiteSpace(template.Id)) continue;
            // Skip preset ids — those flow through the override map above,
            // not the custom-templates list. This is belt-and-braces in
            // case a hand-edited settings.json puts a preset id in both.
            if (CommitTemplatePresets.All().Any(p => string.Equals(p.Id, template.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            result.Add(new CommitTemplate
            {
                Id = template.Id,
                Name = template.Name,
                Body = template.Body,
                TicketRegex = template.TicketRegex,
                Scope = CommitTemplateScope.Global,
                IsBuiltIn = false,
            });
        }

        // Repo-scoped templates always force IsBuiltIn=false and Scope=Repository
        // even if the on-disk file lies about either field.
        lock (_lock)
        {
            foreach (var template in _repoTemplates)
            {
                if (string.IsNullOrWhiteSpace(template.Id)) continue;
                result.Add(new CommitTemplate
                {
                    Id = template.Id,
                    Name = template.Name,
                    Body = template.Body,
                    TicketRegex = template.TicketRegex,
                    Scope = CommitTemplateScope.Repository,
                    IsBuiltIn = false,
                });
            }
        }

        return result;
    }

    public CommitTemplate? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return GetAll().FirstOrDefault(
            t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void AddOrUpdate(CommitTemplate template)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));
        if (string.IsNullOrWhiteSpace(template.Id))
            throw new ArgumentException("Template id is required", nameof(template));
        if (string.IsNullOrWhiteSpace(template.Name))
            throw new ArgumentException("Template name is required", nameof(template));

        var isPreset = CommitTemplatePresets.All().Any(
            p => string.Equals(p.Id, template.Id, StringComparison.OrdinalIgnoreCase));

        if (isPreset)
        {
            // User tweaked a built-in — store the override against the
            // preset's id. The preset's name/scope stay frozen; only body
            // and ticket regex are taken from the user's edit.
            var settings = _settingsService.LoadSettings();
            settings.CommitTemplateOverrides ??= new Dictionary<string, CommitTemplate>();
            settings.CommitTemplateOverrides[template.Id] = new CommitTemplate
            {
                Id = template.Id,
                Name = template.Name, // ignored by GetAll, kept for forward-compat
                Body = template.Body,
                TicketRegex = template.TicketRegex,
                Scope = CommitTemplateScope.Global,
                IsBuiltIn = true,
            };
            _settingsService.SaveSettings(settings);
        }
        else if (template.Scope == CommitTemplateScope.Repository)
        {
            if (string.IsNullOrEmpty(_activeRepositoryPath))
                throw new InvalidOperationException(
                    "Cannot save a Repository-scope template when no repository is active.");

            lock (_lock)
            {
                var idx = _repoTemplates.FindIndex(t =>
                    string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase));
                var stored = Clone(template);
                stored.Scope = CommitTemplateScope.Repository;
                stored.IsBuiltIn = false;
                if (idx < 0) _repoTemplates.Add(stored);
                else _repoTemplates[idx] = stored;
                SaveRepoTemplates();
            }
        }
        else
        {
            // User global custom template.
            var settings = _settingsService.LoadSettings();
            settings.CommitTemplates ??= [];
            var idx = settings.CommitTemplates.FindIndex(t =>
                string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase));
            var stored = Clone(template);
            stored.Scope = CommitTemplateScope.Global;
            stored.IsBuiltIn = false;
            if (idx < 0) settings.CommitTemplates.Add(stored);
            else settings.CommitTemplates[idx] = stored;
            _settingsService.SaveSettings(settings);
        }

        TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        var isPreset = CommitTemplatePresets.All().Any(
            p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (isPreset)
        {
            // "Delete" on a built-in means "drop my override and revert
            // to the shipped body/regex" — not actually removing the row.
            var settings = _settingsService.LoadSettings();
            if (settings.CommitTemplateOverrides == null) return;
            if (!settings.CommitTemplateOverrides.Remove(id)) return;
            _settingsService.SaveSettings(settings);
            TemplatesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // User global?
        var globalSettings = _settingsService.LoadSettings();
        var globalRemoved = globalSettings.CommitTemplates?.RemoveAll(
            t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (globalRemoved)
        {
            _settingsService.SaveSettings(globalSettings);
            TemplatesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Repo-scoped?
        bool repoRemoved;
        lock (_lock)
        {
            repoRemoved = _repoTemplates.RemoveAll(
                t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (repoRemoved) SaveRepoTemplates();
        }
        if (repoRemoved) TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetToDefaults()
    {
        var settings = _settingsService.LoadSettings();
        var anyChanged =
            (settings.CommitTemplates?.Count ?? 0) > 0
            || (settings.CommitTemplateOverrides?.Count ?? 0) > 0;

        settings.CommitTemplates = [];
        settings.CommitTemplateOverrides = new Dictionary<string, CommitTemplate>();
        _settingsService.SaveSettings(settings);

        bool repoCleared;
        lock (_lock)
        {
            repoCleared = _repoTemplates.Count > 0;
            _repoTemplates = [];
            if (repoCleared) SaveRepoTemplates();
        }

        if (anyChanged || repoCleared)
            TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetActiveRepository(string? repositoryPath)
    {
        var newPath = string.IsNullOrWhiteSpace(repositoryPath) ? null : repositoryPath;
        var changed = !string.Equals(_activeRepositoryPath, newPath, StringComparison.OrdinalIgnoreCase);

        _activeRepositoryPath = newPath;

        lock (_lock)
        {
            _repoTemplates = LoadRepoTemplates(_activeRepositoryPath);
        }

        // Always fire — even if the path didn't change, the file might
        // have been edited externally between calls (rare but possible).
        if (changed || _repoTemplates.Count > 0)
            TemplatesChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Resolve(
        CommitTemplate template,
        string? branchName,
        string? userName,
        string? userEmail,
        out int cursorOffset)
    {
        if (template is null) throw new ArgumentNullException(nameof(template));

        var ticket = ExtractTicket(branchName, template.TicketRegex);
        var now = DateTimeOffset.Now;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["branch"] = branchName ?? string.Empty,
            ["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["datetime"] = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["user.name"] = userName ?? string.Empty,
            ["user.email"] = userEmail ?? string.Empty,
            ["ticket"] = ticket,
        };

        // Single pass: scan the body, look for {token}, substitute when
        // recognised, leave the literal text otherwise (so the user can
        // type "{not-a-real-token}" without it disappearing). The {cursor}
        // token is special — it's removed and its final byte offset
        // recorded in cursorOffset.
        var sb = new StringBuilder(template.Body.Length);
        cursorOffset = -1;

        var body = template.Body;
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] == '{')
            {
                var close = body.IndexOf('}', i + 1);
                if (close > i)
                {
                    var token = body.Substring(i + 1, close - i - 1);
                    if (string.Equals(token, "cursor", StringComparison.OrdinalIgnoreCase))
                    {
                        if (cursorOffset < 0) cursorOffset = sb.Length;
                        i = close + 1;
                        continue;
                    }
                    if (values.TryGetValue(token, out var replacement))
                    {
                        sb.Append(replacement);
                        i = close + 1;
                        continue;
                    }
                    // Unknown token — preserve literally so the user's
                    // intent ("just the text in braces") survives.
                }
            }
            sb.Append(body[i]);
            i++;
        }

        if (cursorOffset < 0) cursorOffset = sb.Length;
        return sb.ToString();
    }

    /// <summary>
    /// Extract a ticket id from <paramref name="branchName"/> using
    /// <paramref name="regex"/>. First capture group's value wins; falls
    /// back to the whole match when the regex has no groups; returns
    /// empty string when the regex is empty, invalid, or doesn't match.
    /// Failures are silent because this runs on every template apply —
    /// a malformed user regex shouldn't spam the log.
    /// </summary>
    internal static string ExtractTicket(string? branchName, string? regex)
    {
        if (string.IsNullOrEmpty(branchName) || string.IsNullOrWhiteSpace(regex))
            return string.Empty;

        try
        {
            var match = Regex.Match(
                branchName,
                regex,
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
            if (!match.Success) return string.Empty;
            if (match.Groups.Count > 1 && match.Groups[1].Success)
                return match.Groups[1].Value;
            return match.Value;
        }
        catch (ArgumentException)
        {
            // Invalid regex — user's mistake, swallow.
            return string.Empty;
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological pattern — bail.
            return string.Empty;
        }
    }

    // ---- repo-scoped storage -----------------------------------------

    private static string GetRepoTemplatesPath(string repositoryPath)
        => Path.Combine(repositoryPath, ".git", "leaf", "commit-templates.json");

    private static List<CommitTemplate> LoadRepoTemplates(string? repositoryPath)
    {
        if (string.IsNullOrEmpty(repositoryPath)) return [];
        var path = GetRepoTemplatesPath(repositoryPath);
        try
        {
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<CommitTemplate>>(json, JsonOptions);
            if (loaded is null) return [];
            // Sanitize on load — repo file lives in version control adjacent
            // territory, treat it as untrusted input.
            foreach (var t in loaded)
            {
                t.Scope = CommitTemplateScope.Repository;
                t.IsBuiltIn = false;
            }
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("CommitTemplates", $"Could not read repo templates from {path}: {ex.Message}");
            return [];
        }
    }

    private void SaveRepoTemplates()
    {
        if (string.IsNullOrEmpty(_activeRepositoryPath)) return;

        var path = GetRepoTemplatesPath(_activeRepositoryPath);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Snapshot under the lock — caller already holds it on the
            // write paths, but the load path doesn't, and Lists serialise
            // by enumerating which is unsafe under concurrent mutation.
            var snapshot = _repoTemplates.ToList();
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("CommitTemplates", $"Could not write repo templates to {path}", ex);
        }
    }

    // ---- helpers -----------------------------------------------------

    private static CommitTemplate Clone(CommitTemplate src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Body = src.Body,
        TicketRegex = src.TicketRegex,
        Scope = src.Scope,
        IsBuiltIn = src.IsBuiltIn,
    };
}
