using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for BranchListView.xaml
/// </summary>
public partial class BranchListView : UserControl
{
    private GitFlowBranchType _currentBranchType;
    private string _currentPrefix = "";
    private SemanticVersion? _suggestedVersion;

    public BranchListView()
    {
        InitializeComponent();
    }

    private void Branch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BranchInfo branch)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        // Double-click to checkout
        if (e.ClickCount == 2 && !branch.IsCurrent)
        {
            _ = viewModel.CheckoutBranchAsync(branch);
            e.Handled = true;
            return;
        }

        // Single click - select this branch
        // For local branches, this also selects in GITFLOW since they share instances
        // For remote branches, only that specific branch is selected
        SelectBranch(viewModel.SelectedRepository, branch);
        e.Handled = true;
    }

    private void Branch_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BranchInfo branch)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        // If right-clicked branch is not selected, select it
        if (!branch.IsSelected)
        {
            SelectBranch(viewModel.SelectedRepository, branch);
        }

        // Don't mark handled - let context menu open
    }

    /// <summary>
    /// Selects the given branch. For local branches (which are shared between GITFLOW and LOCAL
    /// categories), this automatically shows selection in both places since they're the same instance.
    /// </summary>
    private static void SelectBranch(RepositoryInfo repo, BranchInfo branch)
    {
        // Clear current selection
        repo.ClearBranchSelection();

        // Select the clicked branch
        branch.IsSelected = true;
        repo.SelectedBranches.Add(branch);
    }

    private Button? _lastChevronButton;

    private async void GitFlowActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null)
            return;

        _lastChevronButton = button;
        e.Handled = true;

        // Build the context menu dynamically
        await BuildGitFlowContextMenu(button.ContextMenu);

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Right;
        button.ContextMenu.IsOpen = true;
    }

    #region GitFlow Context Menu

    private string? _activeRelease;
    private string? _activeHotfix;
    private List<string> _activeFeatures = new();

    private async Task BuildGitFlowContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();

        // Get GitFlow status first to determine menu state
        _activeFeatures = new List<string>();
        _activeRelease = null;
        _activeHotfix = null;

        if (DataContext is MainViewModel viewModel && viewModel.SelectedRepository != null)
        {
            var status = await viewModel.GetGitFlowStatusAsync();
            if (status != null)
            {
                _activeFeatures = status.ActiveFeatures.ToList();
                _activeRelease = status.ActiveReleases.FirstOrDefault();
                _activeHotfix = status.ActiveHotfixes.FirstOrDefault();
            }
        }

        // Start Feature (purple dot) - always show Start since multiple features allowed
        var startFeatureItem = new MenuItem
        {
            Header = "Start Feature",
            Icon = CreateColorDot("#8250DF")
        };
        startFeatureItem.Click += StartFeature_Click;
        menu.Items.Add(startFeatureItem);

        // Release (yellow/amber dot) - Start or Finish depending on active state
        if (_activeRelease != null)
        {
            var finishReleaseItem = new MenuItem
            {
                Header = $"Finish Release ({_activeRelease})",
                Icon = CreateColorDot("#BF8700"),
                Tag = _activeRelease
            };
            finishReleaseItem.Click += FinishRelease_Click;
            menu.Items.Add(finishReleaseItem);
        }
        else
        {
            var startReleaseItem = new MenuItem
            {
                Header = "Start Release",
                Icon = CreateColorDot("#BF8700")
            };
            startReleaseItem.Click += StartRelease_Click;
            menu.Items.Add(startReleaseItem);
        }

        // Hotfix (red dot) - Start or Finish depending on active state
        if (_activeHotfix != null)
        {
            var finishHotfixItem = new MenuItem
            {
                Header = $"Finish Hotfix ({_activeHotfix})",
                Icon = CreateColorDot("#CF222E"),
                Tag = _activeHotfix
            };
            finishHotfixItem.Click += FinishHotfix_Click;
            menu.Items.Add(finishHotfixItem);
        }
        else
        {
            var startHotfixItem = new MenuItem
            {
                Header = "Start Hotfix",
                Icon = CreateColorDot("#CF222E")
            };
            startHotfixItem.Click += StartHotfix_Click;
            menu.Items.Add(startHotfixItem);
        }

        // Finish Feature submenu (if any active features) - features can have multiple
        if (_activeFeatures.Count > 0)
        {
            menu.Items.Add(new Separator());

            var finishFeatureMenu = new MenuItem
            {
                Header = "Finish Feature",
                Icon = CreateColorDot("#8250DF")
            };
            foreach (var feature in _activeFeatures)
            {
                var featureItem = new MenuItem { Header = feature, Tag = feature };
                featureItem.Click += FinishFeature_Click;
                finishFeatureMenu.Items.Add(featureItem);
            }
            menu.Items.Add(finishFeatureMenu);
        }
    }

    private void FinishFeature_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string featureName)
        {
            FinishGitFlowBranch(GitFlowBranchType.Feature, featureName);
        }
    }

    private void FinishRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_activeRelease != null)
        {
            FinishGitFlowBranch(GitFlowBranchType.Release, _activeRelease);
        }
    }

    private void FinishHotfix_Click(object sender, RoutedEventArgs e)
    {
        if (_activeHotfix != null)
        {
            FinishGitFlowBranch(GitFlowBranchType.Hotfix, _activeHotfix);
        }
    }

    private async void FinishGitFlowBranch(GitFlowBranchType branchType, string name)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        var config = await viewModel.GetGitFlowConfigAsync();
        if (config == null) return;

        // Get the full branch name
        var prefix = branchType switch
        {
            GitFlowBranchType.Feature => config.FeaturePrefix,
            GitFlowBranchType.Release => config.ReleasePrefix,
            GitFlowBranchType.Hotfix => config.HotfixPrefix,
            _ => ""
        };
        var branchName = prefix + name;

        // Find the branch
        var branch = viewModel.SelectedRepository.LocalBranches
            .FirstOrDefault(b => b.Name.Equals(branchName, StringComparison.OrdinalIgnoreCase));

        if (branch != null)
        {
            await viewModel.FinishGitFlowBranchAsync(branch);
        }
    }

    #endregion

    #region Quick Create Flyout

    private void StartFeature_Click(object sender, RoutedEventArgs e)
    {
        OpenQuickCreate(GitFlowBranchType.Feature, "Start Feature", "#8250DF", "feature/");
    }

    private void StartRelease_Click(object sender, RoutedEventArgs e)
    {
        OpenQuickCreate(GitFlowBranchType.Release, "Start Release", "#BF8700", "release/");
    }

    private void StartHotfix_Click(object sender, RoutedEventArgs e)
    {
        OpenQuickCreate(GitFlowBranchType.Hotfix, "Start Hotfix", "#CF222E", "hotfix/");
    }

    private async void OpenQuickCreate(GitFlowBranchType branchType, string header, string color, string defaultPrefix)
    {
        _currentBranchType = branchType;
        _suggestedVersion = null;

        // Get actual prefix from GitFlow config
        if (DataContext is MainViewModel viewModel && viewModel.SelectedRepository != null)
        {
            var config = await viewModel.GetGitFlowConfigAsync();
            if (config != null)
            {
                _currentPrefix = branchType switch
                {
                    GitFlowBranchType.Feature => config.FeaturePrefix,
                    GitFlowBranchType.Release => config.ReleasePrefix,
                    GitFlowBranchType.Hotfix => config.HotfixPrefix,
                    _ => defaultPrefix
                };

                // Get suggested version for release/hotfix
                if (branchType == GitFlowBranchType.Release || branchType == GitFlowBranchType.Hotfix)
                {
                    _suggestedVersion = await viewModel.GetSuggestedVersionAsync(branchType);
                }
            }
            else
            {
                _currentPrefix = defaultPrefix;
            }
        }
        else
        {
            _currentPrefix = defaultPrefix;
        }

        // Setup UI
        QuickCreateHeader.Text = header;
        QuickCreateTypeIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        QuickCreateNameBox.Text = "";
        QuickCreatePreview.Text = _currentPrefix + "...";
        QuickCreateStartButton.IsEnabled = false;
        QuickCreateProgress.Visibility = Visibility.Collapsed;

        // Show/hide version suggestion
        if (_suggestedVersion != null)
        {
            QuickCreateVersionText.Text = _suggestedVersion.ToString();
            QuickCreateVersionPanel.Visibility = Visibility.Visible;
        }
        else
        {
            QuickCreateVersionPanel.Visibility = Visibility.Collapsed;
        }

        // Position popup next to the chevron button and show
        if (_lastChevronButton != null)
        {
            QuickCreatePopup.PlacementTarget = _lastChevronButton;
        }
        QuickCreatePopup.IsOpen = true;
        QuickCreateNameBox.Focus();
    }

    private void QuickCreateNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = QuickCreateNameBox.Text.Trim();
        QuickCreatePreview.Text = string.IsNullOrEmpty(name) ? _currentPrefix + "..." : _currentPrefix + name;
        QuickCreateStartButton.IsEnabled = !string.IsNullOrWhiteSpace(name) && IsValidBranchName(name);
    }

    private void QuickCreateNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && QuickCreateStartButton.IsEnabled)
        {
            QuickCreateStart_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            QuickCreatePopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void QuickCreateVersionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suggestedVersion != null)
        {
            QuickCreateNameBox.Text = _suggestedVersion.ToString();
            QuickCreateNameBox.CaretIndex = QuickCreateNameBox.Text.Length;
        }
    }

    private void QuickCreateCancel_Click(object sender, RoutedEventArgs e)
    {
        QuickCreatePopup.IsOpen = false;
    }

    private async void QuickCreateStart_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        var name = QuickCreateNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        // Disable UI
        QuickCreateNameBox.IsEnabled = false;
        QuickCreateStartButton.IsEnabled = false;
        QuickCreateProgress.Visibility = Visibility.Visible;
        QuickCreateProgressText.Text = "Checking for uncommitted changes...";

        try
        {
            // Check for uncommitted changes
            var repoInfo = await viewModel.GetRepositoryInfoAsync();
            if (repoInfo?.IsDirty == true)
            {
                var result = MessageBox.Show(
                    "You have uncommitted changes.\n\nWould you like to stash them first?",
                    "Uncommitted Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                {
                    ResetQuickCreateUI();
                    return;
                }

                if (result == MessageBoxResult.Yes)
                {
                    QuickCreateProgressText.Text = "Stashing changes...";
                    await viewModel.StashChangesAsync($"Auto-stash before {_currentBranchType.ToString().ToLower()} '{name}'");
                }
            }

            // Create the branch
            QuickCreateProgressText.Text = $"Creating {_currentBranchType.ToString().ToLower()} branch...";

            await viewModel.CreateGitFlowBranchAsync(_currentBranchType, name);

            QuickCreatePopup.IsOpen = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create branch:\n\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ResetQuickCreateUI();
        }
    }

    private void ResetQuickCreateUI()
    {
        QuickCreateNameBox.IsEnabled = true;
        QuickCreateStartButton.IsEnabled = !string.IsNullOrWhiteSpace(QuickCreateNameBox.Text);
        QuickCreateProgress.Visibility = Visibility.Collapsed;
    }

    private static bool IsValidBranchName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Contains(' ')) return false;
        if (name.Contains("..")) return false;
        if (name.StartsWith('/') || name.EndsWith('/')) return false;
        if (name.StartsWith('.') || name.EndsWith('.')) return false;
        return true;
    }

    #endregion

    #region Helpers

    private static Border CreateColorDot(string hexColor)
    {
        return new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    #endregion
}
