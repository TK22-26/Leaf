#nullable enable
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentIcons.Common;
using Leaf.Services;

namespace Leaf.Controls.Merge;

/// <summary>
/// Collapsible merge-editor footer panel listing every git subprocess the
/// app fires while the editor is open (C6). Consumes
/// <see cref="IGitService.GitCommandExecuted"/>; detaches on Unloaded so
/// a re-opened editor doesn't leak subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// Entries cap at <see cref="MaxEntries"/> — the log exists for in-session
/// transparency, not auditing, so trimming the oldest entries when the cap
/// fires keeps memory bounded without losing what the user is likely to
/// care about (recent activity). Entries are added on the UI thread via
/// the event raiser's own synchronisation — <see cref="IGitService"/>
/// raises on the thread that owns the original invocation.
/// </para>
/// </remarks>
public partial class GitCommandLog : UserControl
{
    /// <summary>Cap so memory can't grow unboundedly during a long editor session.</summary>
    public const int MaxEntries = 200;

    private readonly ObservableCollection<GitCommandLogEntry> _entries = new();
    private IGitService? _service;

    public GitCommandLog()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _entries;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _entries.CollectionChanged += (_, _) =>
        {
            CountBadge.Text = _entries.Count > 0 ? _entries.Count.ToString() : string.Empty;
            // Keep the empty-state placeholder and the entry list mutually
            // exclusive — both share the same Grid cell so showing one means
            // collapsing the other (Visibility, not Opacity, so the listbox
            // doesn't reserve layout while empty).
            EmptyStateText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EntriesList.Visibility = _entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Resolve lazily so the control is xaml-previewable without a
        // live service provider. If App.Services isn't available, the
        // control renders empty — acceptable for design-time only.
        if (_service is null && Leaf.App.Services is { } provider)
        {
            _service = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetService<IGitService>(provider);
            if (_service is not null)
            {
                _service.GitCommandExecuted += OnGitCommandExecuted;
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_service is not null)
        {
            _service.GitCommandExecuted -= OnGitCommandExecuted;
            _service = null;
        }
    }

    private void OnGitCommandExecuted(object? sender, GitCommandEventArgs e)
    {
        // Drop "chatty" background reads before we hop the dispatcher — the
        // user cares about actions they triggered (merge-file, commit,
        // merge --abort), not C5's per-hover blame cache warmers. Filtering
        // before the UI-thread hop also keeps the ObservableCollection
        // pressure low on rapid hover.
        if (IsBackgroundChatter(e)) return;

        // Dispatcher hop: the git runner can fire from a worker thread if
        // the command was awaited under ConfigureAwait(false). Use
        // BeginInvoke (asynchronous, ordered per-dispatcher) — synchronous
        // Dispatcher.Invoke would deadlock if the caller were blocking on
        // the same task via .Wait / .Result while this runner raises
        // its completed event. Matches the MergeBlameService / MergeMotionHelpers
        // "fire and forget to UI thread" pattern.
        Dispatcher.BeginInvoke(() =>
        {
            var entry = new GitCommandLogEntry(e);
            _entries.Add(entry);
            while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            EntriesList.ScrollIntoView(entry);
        });
    }

    /// <summary>
    /// True when <paramref name="e"/> is a read-only probe fired by a
    /// background service (blame hover, HEAD resolver, log-cache warmup)
    /// rather than a user-triggered action. These flood the log with noise
    /// on rapid hover and obscure the "what did I actually do" signal the
    /// footer is meant to surface. Classification is argv-prefix based so
    /// adding a new probe type is a one-line update here.
    /// </summary>
    internal static bool IsBackgroundChatter(GitCommandEventArgs e)
    {
        var args = e.Arguments;
        if (string.IsNullOrEmpty(args)) return false;
        // git blame / rev-parse HEAD are the two C5 blame-service calls.
        // Both are invisible to the user and produce no state change.
        return args.StartsWith("blame", StringComparison.Ordinal)
            || args.StartsWith("rev-parse HEAD", StringComparison.Ordinal);
    }

    private void OnToggleClicked(object sender, RoutedEventArgs e)
    {
        var expanded = ExpanderToggle.IsChecked == true;
        LogBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ToggleIcon.Symbol = expanded ? Symbol.ChevronDown : Symbol.ChevronRight;
    }
}

/// <summary>
/// Presentation adapter over <see cref="GitCommandEventArgs"/> that adds
/// the display strings + exit-status brush the XAML row template binds to.
/// Public for testability; the control's <c>_entries</c> collection holds
/// instances of this type.
/// </summary>
public sealed class GitCommandLogEntry
{
    public GitCommandLogEntry(GitCommandEventArgs raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ExitCode = raw.ExitCode;
        CommandText = "git " + raw.Arguments;
    }

    public int ExitCode { get; }
    public string CommandText { get; }

    /// <summary>Checkmark / cross indicator shown in the status column.</summary>
    public string ExitIndicator => ExitCode == 0 ? "✓" : "✗";

    /// <summary>
    /// Brush for the indicator. Resolved through <see cref="MergePaletteResources"/>
    /// so theme swaps (V8 light) pick up the new colour without rebinding.
    /// </summary>
    public Brush ExitBrush => ExitCode == 0
        ? MergePaletteResources.Resolve<Brush>("Merge.State.Resolved")
        : MergePaletteResources.Resolve<Brush>("Merge.State.Unresolved");
}
