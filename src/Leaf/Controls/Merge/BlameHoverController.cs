#nullable enable
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Leaf.Models;
using Leaf.Services.Merge;

namespace Leaf.Controls.Merge;

/// <summary>
/// Owns the hover → debounce → fetch → popup pipeline for the C5 blame-peek
/// surface. A single controller instance serves every pane in the merge
/// editor so there's only one popup in the visual tree and the debounce
/// timer is shared — moving the pointer from Ours to Theirs restarts the
/// same timer rather than racing two copies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Debounce.</b> 500 ms (plan §C5). The timer resets on every
/// <see cref="UIElement.MouseMove"/> and cancels on
/// <see cref="UIElement.MouseLeave"/> so passing the pointer quickly across
/// the pane produces zero git subprocess calls.
/// </para>
/// <para>
/// <b>Cancellation.</b> Each fetch spawns a <see cref="CancellationTokenSource"/>;
/// a new hover target or leave event cancels it before the blame call
/// returns so stale records never reach the UI. The
/// <see cref="IMergeBlameService"/> cache absorbs duplicate requests for the
/// same line across cancellations.
/// </para>
/// <para>
/// <b>Line resolution.</b> Delegated to a <see cref="LineAtPointResolver"/>
/// per pane so each caller supplies the math appropriate to its rendering
/// (MergePaneGlyphLayout for ReadOnlyMergePane, AvalonEdit's TextView for
/// ResultPane). Keeps the controller ignorant of either surface's internals.
/// </para>
/// </remarks>
public sealed class BlameHoverController
{
    /// <summary>Debounce interval per plan §C5 — long enough that flick-scrolling produces zero fetches.</summary>
    internal const int DebounceMs = 500;

    /// <summary>
    /// Resolve the 1-based line number (if any) under
    /// <paramref name="pointInPane"/> on <paramref name="pane"/>. Return
    /// <c>null</c> when the pointer is over non-content area (padding,
    /// margins, empty extent past end-of-file) so the controller can skip
    /// the fetch entirely instead of spawning a request that would hit
    /// "line not found" in the cache.
    /// </summary>
    public delegate int? LineAtPointResolver(FrameworkElement pane, Point pointInPane);

    private readonly IMergeBlameService _service;
    private readonly Func<string> _repoPathProvider;
    private readonly Func<string?> _filePathProvider;
    private readonly Popup _popup;
    private readonly BlamePeekPopover _popover;
    private readonly DispatcherTimer _debounce;
    private readonly Action<string> _commitRequestedCallback;

    private CancellationTokenSource? _fetchCts;
    private FrameworkElement? _pendingPane;
    private Point _pendingPoint;
    private int _pendingLine = -1;
    private LineAtPointResolver? _pendingResolver;

    public BlameHoverController(
        IMergeBlameService service,
        Func<string> repoPathProvider,
        Func<string?> filePathProvider,
        Action<string> commitRequestedCallback)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _repoPathProvider = repoPathProvider ?? throw new ArgumentNullException(nameof(repoPathProvider));
        _filePathProvider = filePathProvider ?? throw new ArgumentNullException(nameof(filePathProvider));
        _commitRequestedCallback = commitRequestedCallback ?? throw new ArgumentNullException(nameof(commitRequestedCallback));

        _popover = new BlamePeekPopover();
        _popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = PlacementMode.MousePoint,
            HorizontalOffset = 12,
            VerticalOffset = 12,
            Child = _popover,
        };
        _popover.CommitRequested += (_, sha) =>
        {
            DismissPopup(returnFocusToPane: true);
            _commitRequestedCallback(sha);
        };
        // Keyboard dismiss: Escape inside the popover returns focus to the
        // active pane (the one whose MouseMove or keyboard trigger opened
        // the popup) so the user doesn't land in limbo after closing.
        _popover.DismissRequested += (_, _) => DismissPopup(returnFocusToPane: true);
        // Popover mouse tracking: when the user moves the pointer from the
        // pane into the popup, pane.MouseLeave fires and would otherwise
        // dismiss immediately. Tracking popup's own IsMouseOver lets us
        // defer dismissal while the user is interacting with the popup.
        _popover.MouseEnter += (_, _) => _popoverHasPointer = true;
        _popover.MouseLeave += (_, _) =>
        {
            _popoverHasPointer = false;
            // If focus has already transferred into the popup, keep it
            // open so keyboard users can Tab/Enter after the mouse moved
            // away. Otherwise dismiss — the hover is over.
            if (!PopoverHasKeyboardFocus()) DismissPopup(returnFocusToPane: false);
        };

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMs),
        };
        _debounce.Tick += OnDebounceElapsed;
    }

    private bool _popoverHasPointer;
    private FrameworkElement? _lastOpeningPane;
    // Tracks which pane (if any) currently has the mouse pointer. Updated
    // on each tracked pane's MouseEnter/Leave so the deferred-dismiss
    // callback in OnPaneMouseLeave can tell whether the pointer has
    // landed on a sibling pane rather than genuinely leaving the editor.
    // Without this, moving from Ours → Theirs produced a
    // close-then-reopen flicker while the fresh 500 ms debounce ran.
    private FrameworkElement? _currentHoveredPane;

    private bool PopoverHasKeyboardFocus()
    {
        if (!_popup.IsOpen) return false;
        var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, _popover)) return true;
            focused = System.Windows.Media.VisualTreeHelper.GetParent(focused)
                   ?? System.Windows.LogicalTreeHelper.GetParent(focused);
        }
        return false;
    }

    private void DismissPopup(bool returnFocusToPane)
    {
        _popup.IsOpen = false;
        _popoverHasPointer = false;
        if (returnFocusToPane && _lastOpeningPane is { } pane)
        {
            pane.Focus();
        }
        _lastOpeningPane = null;
    }

    /// <summary>Attach the controller's hover handlers to <paramref name="pane"/>.</summary>
    public void TrackPane(FrameworkElement pane, LineAtPointResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(resolver);
        pane.MouseEnter += OnPaneMouseEnter;
        pane.MouseMove += (sender, e) => OnPaneMouseMove((FrameworkElement)sender!, e, resolver);
        pane.MouseLeave += OnPaneMouseLeave;
    }

    private void OnPaneMouseEnter(object sender, MouseEventArgs e)
    {
        _currentHoveredPane = sender as FrameworkElement;
    }

    private void OnPaneMouseMove(FrameworkElement pane, MouseEventArgs e, LineAtPointResolver resolver)
    {
        var point = e.GetPosition(pane);
        var line = resolver(pane, point);
        if (line is null || line.Value < 1)
        {
            CancelPending();
            return;
        }

        // Same line as the pending one: let the existing debounce continue.
        // A different line (or pane) resets it — standard "wait for rest".
        if (ReferenceEquals(_pendingPane, pane) && _pendingLine == line.Value)
        {
            return;
        }

        CancelPending();
        _pendingPane = pane;
        _pendingPoint = point;
        _pendingLine = line.Value;
        _pendingResolver = resolver;
        _debounce.Start();
    }

    private void OnPaneMouseLeave(object sender, MouseEventArgs e)
    {
        // Clear the currently-hovered-pane tracker if it was this pane.
        // A sibling pane's MouseEnter (fired AFTER this MouseLeave per
        // WPF's cross-element transit ordering) will re-set it so the
        // deferred-dismiss check below sees the correct state.
        if (ReferenceEquals(_currentHoveredPane, sender))
        {
            _currentHoveredPane = null;
        }

        // Always cancel any in-flight debounce — moving off the pane
        // aborts the not-yet-shown popup.
        CancelPending();
        if (!_popup.IsOpen) return;
        // Popup is already visible: defer dismissal so we can tell the
        // difference between three transit cases:
        //   1. pane → popup  : popover.MouseEnter flips _popoverHasPointer
        //   2. pane → sibling pane : that pane's MouseEnter sets
        //      _currentHoveredPane — skip dismiss so the popup stays
        //      visible while the new pane's debounce runs, then
        //      OnDebounceElapsed swaps the record in place (no flicker).
        //   3. pane → outside  : none of the above flip; dismiss runs.
        // Also honours keyboard focus into the popup (Tab path).
        _popup.Dispatcher.BeginInvoke(() =>
        {
            if (_popoverHasPointer) return;
            if (_currentHoveredPane is not null) return;
            if (PopoverHasKeyboardFocus()) return;
            DismissPopup(returnFocusToPane: false);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CancelPending()
    {
        _debounce.Stop();
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _fetchCts = null;
        _pendingPane = null;
        _pendingLine = -1;
        _pendingResolver = null;
    }

    private async void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce.Stop();
        var pane = _pendingPane;
        var line = _pendingLine;
        if (pane is null || line < 1) return;

        var filePath = _filePathProvider();
        if (string.IsNullOrEmpty(filePath)) return;
        var repoPath = _repoPathProvider();
        if (string.IsNullOrEmpty(repoPath)) return;

        _fetchCts = new CancellationTokenSource();
        var token = _fetchCts.Token;

        FileBlameLine? record;
        try
        {
            record = await _service.GetLineBlameAsync(repoPath, filePath, line, token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // Blame failures shouldn't toast the user — the popover just
            // stays hidden. Log the exception type only (telemetry privacy:
            // no path / sha / content) so diagnostic traces still surface
            // the failure.
            Leaf.Services.Log.Info("MergeBlame", $"HoverFetchFailed: {ex.GetType().Name}");
            return;
        }

        if (token.IsCancellationRequested || record is null) return;
        if (!ReferenceEquals(_pendingPane, pane) || _pendingLine != line) return;

        _popover.SetRecord(record);
        _popup.PlacementTarget = pane;
        _lastOpeningPane = pane;
        _popup.IsOpen = true;
    }

    /// <summary>
    /// Keyboard-triggered blame peek. Host wires this to an Alt+B (or
    /// similar) key binding on the pane so mouse-free users can request
    /// blame for the caret/active line. Focus transfers into the popup's
    /// sha link immediately — press Enter to jump, Escape to dismiss. No
    /// debounce: the explicit invocation means the user wants the popup
    /// now, not after a dwell.
    /// </summary>
    /// <remarks>
    /// <b>Must not throw.</b> Key-binding handlers discard the returned
    /// Task (fire-and-forget), so any unhandled exception here would land
    /// on <c>TaskScheduler.UnobservedTaskException</c>. Every awaited call
    /// inside is wrapped in the broad-catch block that falls through to
    /// <c>Log.Info</c>. If a future edit introduces another await, keep
    /// that invariant.
    /// </remarks>
    public async Task ShowForLineAsync(FrameworkElement pane, int oneBasedLine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (oneBasedLine < 1) return;

        var filePath = _filePathProvider();
        if (string.IsNullOrEmpty(filePath)) return;
        var repoPath = _repoPathProvider();
        if (string.IsNullOrEmpty(repoPath)) return;

        // Cancel any in-flight hover fetch before kicking the keyboard
        // path's own request. Without this, a hover that resolves after
        // the Alt+B fetch would overwrite the keyboard-focused record
        // with stale data while the sha Hyperlink keeps its focus
        // highlight — a confusing "click this sha, jump to a different
        // commit" race.
        CancelPending();

        FileBlameLine? record;
        try
        {
            record = await _service.GetLineBlameAsync(repoPath, filePath, oneBasedLine, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Leaf.Services.Log.Info("MergeBlame", $"KeyboardFetchFailed: {ex.GetType().Name}");
            return;
        }
        if (record is null) return;

        _popover.SetRecord(record);
        _popup.PlacementTarget = pane;
        _lastOpeningPane = pane;
        _popup.IsOpen = true;
        _popover.FocusShaLink();
    }

    /// <summary>
    /// Dispose the debounce timer + any in-flight fetch. Mirrors the
    /// Unloaded-detach pattern other merge controls use for dispatcher-timer
    /// lifetime.
    /// </summary>
    public void Dispose()
    {
        _debounce.Stop();
        _debounce.Tick -= OnDebounceElapsed;
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _fetchCts = null;
        _popup.IsOpen = false;
        // Drop the strong reference from the Popup to the most-recent pane
        // so a disposed controller doesn't keep the pane rooted through
        // the window's Popup tracker.
        _popup.PlacementTarget = null;
        _currentHoveredPane = null;
        _lastOpeningPane = null;
    }
}
