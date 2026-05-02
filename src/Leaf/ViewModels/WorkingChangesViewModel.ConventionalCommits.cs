using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Leaf.ViewModels;

/// <summary>
/// §5.15 Phase 4 partial — Conventional Commits structured form.
///
/// <para>When <see cref="UseConventionalCommitsForm"/> is true, the
/// commit input control swaps its freeform text boxes for a structured
/// panel: type ComboBox, editable-with-history scope ComboBox,
/// description, optional body, breaking-change flag, footer. Every
/// field change rebuilds <see cref="CommitMessage"/> +
/// <see cref="CommitDescription"/> live so the existing commit pipeline
/// (validation, button enablement, AI auto-fill, amend) keeps working
/// without a parallel code path.</para>
///
/// <para>Toggling off leaves the assembled text in place — the user
/// can keep typing in freeform without losing what the form built.</para>
/// </summary>
public partial class WorkingChangesViewModel
{
    /// <summary>Conventional Commits canonical type list.</summary>
    public IReadOnlyList<string> ConventionalCommitTypes { get; } =
    [
        "feat", "fix", "docs", "style", "refactor", "perf",
        "test", "build", "ci", "chore", "revert",
    ];

    /// <summary>
    /// Whether the structured Conventional Commits form is currently
    /// active. Persisted so a user who opts in stays opted in across
    /// launches; matches the persistence model already used for
    /// <see cref="IsCommitOptionsExpanded"/>.
    /// </summary>
    [ObservableProperty]
    private bool _useConventionalCommitsForm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConventionalDescriptionRemaining))]
    private string _conventionalType = "feat";

    [ObservableProperty]
    private string _conventionalScope = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConventionalDescriptionRemaining))]
    private string _conventionalDescription = string.Empty;

    [ObservableProperty]
    private string _conventionalBody = string.Empty;

    [ObservableProperty]
    private bool _conventionalIsBreaking;

    [ObservableProperty]
    private string _conventionalFooter = string.Empty;

    /// <summary>
    /// Recently-used scope values, persisted in
    /// <see cref="AppSettings.ConventionalCommitScopeHistory"/>. Bound
    /// to the editable scope ComboBox so users can pick from past
    /// values rather than retype them.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _conventionalScopeHistory = [];

    /// <summary>
    /// Subject-line character budget left for the description after
    /// accounting for <c>type(scope)!: </c>. Drives the counter shown
    /// next to the description input — colour-coded amber/red by the
    /// same converter the freeform path uses.
    /// </summary>
    public int ConventionalDescriptionRemaining =>
        Math.Max(0, MaxMessageLength - BuildSubject().Length);

    /// <summary>
    /// Initialise structured-form state from settings (called once at
    /// VM construction time). Loads the persisted toggle, scope history,
    /// and — if the user already had a Conventional-shaped message in
    /// the freeform field — back-fills the structured fields by parsing.
    /// </summary>
    private void InitializeConventionalCommitsState()
    {
        var settings = _settingsService.LoadSettings();
        ConventionalScopeHistory = new ObservableCollection<string>(
            settings.ConventionalCommitScopeHistory ?? []);

        // Suppress the rebuild + persist + parse cascade for the duration
        // of the load so we don't clobber an empty CommitMessage with
        // "feat: " before the user has had a chance to do anything. The
        // user's first interaction with any structured field (or with
        // the toggle) is what should re-trigger Rebuild, and that runs
        // off the standard partial-method handlers.
        _suppressConventionalRebuild = true;
        try
        {
            UseConventionalCommitsForm = settings.UseConventionalCommitsForm;
        }
        finally
        {
            _suppressConventionalRebuild = false;
        }
    }

    partial void OnUseConventionalCommitsFormChanged(bool value)
    {
        // Persist the toggle so the choice survives across launches.
        var settings = _settingsService.LoadSettings();
        if (settings.UseConventionalCommitsForm != value)
        {
            settings.UseConventionalCommitsForm = value;
            _settingsService.SaveSettings(settings);
        }

        if (value)
        {
            // Switching ON — pre-populate the structured fields from the
            // current freeform message when it parses as Conventional, so
            // a user who's already typed "feat(api): something" doesn't
            // lose their work. When parsing fails the form just shows
            // empty fields; the user can build from there.
            TryParseConventionalIntoFields(CommitMessage, CommitDescription);
            // Recompute the assembled message so subject/description
            // align with the structured fields right away — even if
            // parsing wrote zero changes, this normalises whitespace.
            RebuildConventionalCommitMessage();
        }
        // Switching OFF leaves CommitMessage/CommitDescription where they
        // are — user keeps whatever the form just built.
    }

    partial void OnConventionalTypeChanged(string value) => RebuildConventionalCommitMessage();
    partial void OnConventionalScopeChanged(string value) => RebuildConventionalCommitMessage();
    partial void OnConventionalDescriptionChanged(string value) => RebuildConventionalCommitMessage();
    partial void OnConventionalBodyChanged(string value) => RebuildConventionalCommitMessage();
    partial void OnConventionalIsBreakingChanged(bool value) => RebuildConventionalCommitMessage();
    partial void OnConventionalFooterChanged(string value) => RebuildConventionalCommitMessage();

    /// <summary>
    /// Re-assemble <see cref="CommitMessage"/> + <see cref="CommitDescription"/>
    /// from the structured fields. Conventional Commits spec:
    /// <para><c>type(scope)!: description</c></para>
    /// <para>BLANK LINE</para>
    /// <para>body</para>
    /// <para>BLANK LINE</para>
    /// <para>BREAKING CHANGE: footer / Refs: …</para>
    /// </summary>
    private void RebuildConventionalCommitMessage()
    {
        if (!UseConventionalCommitsForm) return;
        // Suppressed during in-place parse-back so the cascade of
        // OnConventional*Changed handlers triggered by populating the
        // fields doesn't write the same five strings back to disk.
        if (_suppressConventionalRebuild) return;

        CommitMessage = BuildSubject();
        CommitDescription = BuildBodyAndFooter();
    }

    private string BuildSubject()
    {
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrWhiteSpace(ConventionalType) ? "feat" : ConventionalType.Trim());
        if (!string.IsNullOrWhiteSpace(ConventionalScope))
        {
            sb.Append('(');
            sb.Append(ConventionalScope.Trim());
            sb.Append(')');
        }
        if (ConventionalIsBreaking) sb.Append('!');
        sb.Append(": ");
        sb.Append(ConventionalDescription.TrimEnd());
        return sb.ToString();
    }

    private string BuildBodyAndFooter()
    {
        var hasBody = !string.IsNullOrWhiteSpace(ConventionalBody);
        var footer = ConventionalFooter?.Trim() ?? string.Empty;
        var hasFooter = !string.IsNullOrEmpty(footer);
        var hasBreakingFooter = ConventionalIsBreaking
            && !footer.Contains("BREAKING CHANGE", StringComparison.Ordinal);

        if (!hasBody && !hasFooter && !hasBreakingFooter) return string.Empty;

        var sb = new StringBuilder();
        if (hasBody)
        {
            sb.Append(ConventionalBody.Trim());
        }

        if (hasBreakingFooter)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append("BREAKING CHANGE: ");
            // Description doubles as the breaking-change explainer when
            // the user hasn't supplied a separate footer — that matches
            // what most teams write.
            sb.Append(string.IsNullOrWhiteSpace(footer)
                ? ConventionalDescription.Trim()
                : footer);
        }
        else if (hasFooter)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(footer);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parse a (subject, body) pair that may already be in Conventional
    /// Commits shape. On success populates the structured fields; on
    /// failure leaves them at their current values. Conservative — it
    /// only matches the canonical shape with the exact set of types we
    /// ship; an unknown type leaves the user in the form with empty
    /// fields rather than treating their text as a free-form custom type.
    /// </summary>
    internal void TryParseConventionalIntoFields(string subject, string body)
    {
        if (string.IsNullOrEmpty(subject)) return;

        // Regex: ^(type)(\(scope\))?(!)?: description
        var match = System.Text.RegularExpressions.Regex.Match(
            subject,
            @"^(?<type>[a-z]+)(?:\((?<scope>[^)]+)\))?(?<bang>!)?:\s+(?<desc>.*)$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success) return;

        var type = match.Groups["type"].Value;
        if (!ConventionalCommitTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
            return;

        // Use the field-level setters so OnConventional*Changed fires and
        // the assembled-message stays in sync — but suppress the
        // RebuildConventionalCommitMessage cascade for the run since the
        // input we're parsing came from the assembled message anyway and
        // re-running it on every set would write the same bytes back five
        // times.
        _suppressConventionalRebuild = true;
        try
        {
            ConventionalType = type;
            ConventionalScope = match.Groups["scope"].Success ? match.Groups["scope"].Value : string.Empty;
            ConventionalIsBreaking = match.Groups["bang"].Success;
            ConventionalDescription = match.Groups["desc"].Value;
            // Body stays whatever the user had — it's already correct.
            ConventionalBody = body ?? string.Empty;
            // Footer stays empty unless we can spot a "BREAKING CHANGE:"
            // line and lift it out of the body.
            // (Skipped for now — uncommon enough that round-tripping is
            // a lossy operation by design; the user can move text manually.)
        }
        finally
        {
            _suppressConventionalRebuild = false;
        }
    }

    private bool _suppressConventionalRebuild;

    /// <summary>
    /// Push <paramref name="scope"/> to the most-recently-used end of
    /// <see cref="ConventionalScopeHistory"/> (de-dup) and persist. Cap
    /// at 20 entries — match the audit plan's stated history budget.
    /// Called from the commit pipeline so only successfully-committed
    /// scopes get remembered.
    /// </summary>
    public void RememberConventionalScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;
        var trimmed = scope.Trim();

        // Move-to-front, capped at 20.
        var existingIdx = -1;
        for (var i = 0; i < ConventionalScopeHistory.Count; i++)
        {
            if (string.Equals(ConventionalScopeHistory[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                existingIdx = i;
                break;
            }
        }
        if (existingIdx >= 0) ConventionalScopeHistory.RemoveAt(existingIdx);
        ConventionalScopeHistory.Insert(0, trimmed);
        while (ConventionalScopeHistory.Count > 20)
            ConventionalScopeHistory.RemoveAt(ConventionalScopeHistory.Count - 1);

        var settings = _settingsService.LoadSettings();
        settings.ConventionalCommitScopeHistory = ConventionalScopeHistory.ToList();
        _settingsService.SaveSettings(settings);
    }
}
