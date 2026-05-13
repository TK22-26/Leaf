using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings control for toast notification toggles. Renders one row per
/// <see cref="NotificationCategory"/> with a title, a one-line subtitle,
/// and a <see cref="ToggleButton"/>. The Errors category isn't represented
/// here — it's pinned on (rendered separately in the XAML) so a user
/// can't accidentally mute failures.
/// </summary>
public partial class NotificationsSettingsControl : UserControl, ISettingsSectionControl
{
    /// <summary>
    /// The row definitions for the toggle list. Each entry is
    /// (category, title shown to the user, one-line description).
    /// Order is the user-facing order — most-useful first, mute-by-default
    /// rows toward the bottom so the page reads as a noise-reduction
    /// punch-list.
    /// </summary>
    private static readonly (NotificationCategory Category, string Title, string Subtitle)[] Rows =
    [
        (NotificationCategory.SyncOperations,    "Sync operations",       "Pull, push, and fetch completion summaries."),
        (NotificationCategory.MergeAndRebase,    "Merge & rebase",        "Merge, rebase, cherry-pick, and revert outcomes (including conflict warnings)."),
        (NotificationCategory.BranchCheckout,    "Branch & tag checkout", "Switching branches / tags, plus checkout-conflict warnings."),
        (NotificationCategory.GitFlow,           "GitFlow",               "Feature, release, and hotfix start / finish / publish."),
        (NotificationCategory.Worktree,          "Worktrees",             "Worktree create, remove, switch, lock, prune."),
        (NotificationCategory.Submodule,         "Submodules",            "Submodule init, update, add, remove, sync."),
        (NotificationCategory.PullRequest,       "Pull requests",         "PR created, closed, and provider-related notices."),
        (NotificationCategory.Patch,             "Patches",               "Patch created, applied, or copied to clipboard."),
        (NotificationCategory.Repository,        "Repository management", "Repo added, cloned, watch-folder scan results."),
        (NotificationCategory.BranchAdmin,       "Branch admin",          "Routine create, delete, rename, set-upstream confirmations."),
        (NotificationCategory.Stash,             "Stash",                 "Stash save, pop, and drop confirmations."),
        (NotificationCategory.RemoteConfig,      "Remote configuration",  "Add, edit, remove, set-default, URL copied."),
        (NotificationCategory.CancelledOperations, "Cancelled operations", "\"Worktree creation cancelled\", \"merge aborted\", etc."),
    ];

    /// <summary>
    /// Each ToggleButton is created in code so the row table above is the
    /// single source of truth. We keep references here so
    /// <see cref="SaveSettings"/> can read the live state back into
    /// <see cref="AppSettings"/>.
    /// </summary>
    private readonly Dictionary<NotificationCategory, ToggleButton> _toggles = [];

    public NotificationsSettingsControl()
    {
        InitializeComponent();
        BuildRows();
    }

    /// <summary>
    /// Materialise the row list into ToggleList. Each row is a 2-column
    /// Grid: title + subtitle on the left, ToggleButton on the right.
    /// A bottom border separates rows except for the last one, which
    /// gets a zero-thickness underline so the card edge reads cleanly.
    /// </summary>
    private void BuildRows()
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            var (category, title, subtitle) = Rows[i];

            var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelStack = new StackPanel();
            labelStack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorPrimaryBrush"),
            });
            labelStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush"),
                Margin = new Thickness(0, 2, 12, 0),
            });
            Grid.SetColumn(labelStack, 0);
            grid.Children.Add(labelStack);

            var toggle = new ToggleButton
            {
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);

            _toggles[category] = toggle;

            // Wrap each row in a Border so the bottom separator stays
            // attached to the row, not to the wrapping StackPanel — that
            // way a future re-order or visibility-toggle doesn't leave
            // orphan dividers.
            var rowBorder = new Border
            {
                Child = grid,
                BorderBrush = (System.Windows.Media.Brush)FindResource("DividerStrokeColorDefaultBrush"),
                BorderThickness = i == Rows.Length - 1
                    ? new Thickness(0)
                    : new Thickness(0, 0, 0, 1),
            };
            ToggleList.Children.Add(rowBorder);
        }
    }

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        // Mirror AppSettings → toggle.IsChecked for each row. Anything
        // not registered in _toggles silently skips so a future
        // NotificationCategory value can be rolled in without a code
        // change here AND the row table.
        foreach (var (category, _, _) in Rows)
        {
            if (_toggles.TryGetValue(category, out var toggle))
            {
                toggle.IsChecked = ReadFlag(settings, category);
            }
        }
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        foreach (var (category, _, _) in Rows)
        {
            if (_toggles.TryGetValue(category, out var toggle))
            {
                WriteFlag(settings, category, toggle.IsChecked == true);
            }
        }
    }

    /// <summary>
    /// Map a category to the corresponding <c>AppSettings.Notify*</c>
    /// property. A switch is more verbose than reflection but compile-time
    /// checked — when a new category is added the compiler points at this
    /// method (and at <see cref="NotificationService"/>).
    /// </summary>
    private static bool ReadFlag(AppSettings s, NotificationCategory c) => c switch
    {
        NotificationCategory.SyncOperations => s.NotifySyncOperations,
        NotificationCategory.BranchCheckout => s.NotifyBranchCheckout,
        NotificationCategory.BranchAdmin => s.NotifyBranchAdmin,
        NotificationCategory.MergeAndRebase => s.NotifyMergeAndRebase,
        NotificationCategory.GitFlow => s.NotifyGitFlow,
        NotificationCategory.Worktree => s.NotifyWorktree,
        NotificationCategory.Submodule => s.NotifySubmodule,
        NotificationCategory.Stash => s.NotifyStash,
        NotificationCategory.PullRequest => s.NotifyPullRequest,
        NotificationCategory.Patch => s.NotifyPatch,
        NotificationCategory.Repository => s.NotifyRepository,
        NotificationCategory.RemoteConfig => s.NotifyRemoteConfig,
        NotificationCategory.CancelledOperations => s.NotifyCancelledOperations,
        _ => true,
    };

    private static void WriteFlag(AppSettings s, NotificationCategory c, bool value)
    {
        switch (c)
        {
            case NotificationCategory.SyncOperations: s.NotifySyncOperations = value; break;
            case NotificationCategory.BranchCheckout: s.NotifyBranchCheckout = value; break;
            case NotificationCategory.BranchAdmin: s.NotifyBranchAdmin = value; break;
            case NotificationCategory.MergeAndRebase: s.NotifyMergeAndRebase = value; break;
            case NotificationCategory.GitFlow: s.NotifyGitFlow = value; break;
            case NotificationCategory.Worktree: s.NotifyWorktree = value; break;
            case NotificationCategory.Submodule: s.NotifySubmodule = value; break;
            case NotificationCategory.Stash: s.NotifyStash = value; break;
            case NotificationCategory.PullRequest: s.NotifyPullRequest = value; break;
            case NotificationCategory.Patch: s.NotifyPatch = value; break;
            case NotificationCategory.Repository: s.NotifyRepository = value; break;
            case NotificationCategory.RemoteConfig: s.NotifyRemoteConfig = value; break;
            case NotificationCategory.CancelledOperations: s.NotifyCancelledOperations = value; break;
        }
    }
}
