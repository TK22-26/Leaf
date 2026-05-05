using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Leaf.Models;
using Leaf.Services;
using Leaf.Utils;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for BranchListView.xaml.
///
/// Per plan §2.7 this code-behind is intentionally thin: selection state,
/// GitFlow dispatch, and quick-create orchestration all live in
/// <see cref="MainViewModel"/> partials (<c>.Selection.cs</c>, <c>.GitFlow.cs</c>,
/// <c>.Worktree.cs</c>, <c>.Branch.cs</c>). What remains here is the work
/// that genuinely belongs to the view: popup placement, focus, keyboard
/// shortcuts, inline progress state, and event-to-command dispatch.
/// </summary>
public partial class BranchListView : UserControl
{
    private GitFlowBranchType _currentBranchType;
    private string _currentPrefix = "";
    private SemanticVersion? _suggestedVersion;

    public BranchListView()
    {
        InitializeComponent();
        DataContextChanged += BranchListView_DataContextChanged;
    }

    private void BranchListView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
            oldVm.RequestBranchCreatePopup -= OnRequestBranchCreatePopup;
        if (e.NewValue is MainViewModel newVm)
            newVm.RequestBranchCreatePopup += OnRequestBranchCreatePopup;
    }

    private void OnRequestBranchCreatePopup(object? sender, EventArgs e)
    {
        if (sender is MainViewModel viewModel)
            OpenBranchCreatePopup(viewModel);
    }

    #region Branch clicks

    private void Branch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BranchInfo branch)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        // Double-click to checkout
        if (e.ClickCount == 2 && !branch.IsCurrent)
        {
            viewModel.CheckoutBranchAsync(branch)
                .FireAndForget(nameof(viewModel.CheckoutBranchAsync), isUserAction: true);
            e.Handled = true;
            return;
        }

        viewModel.SelectBranch(branch, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

        // Restore graph mode if in PR view
        if (!viewModel.IsGraphMode)
            viewModel.ClosePullRequestViewCommand.Execute(null);

        // Navigate to branch tip in git graph
        if (!string.IsNullOrEmpty(branch.TipSha))
            viewModel.GitGraphViewModel?.SelectCommitBySha(branch.TipSha);

        e.Handled = true;
    }

    private void Branch_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not BranchInfo branch)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        if (!branch.IsSelected)
            viewModel.SelectBranch(branch, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

        // Don't mark handled — let the context menu open.
    }

    #endregion

    #region Tag clicks

    private void Tag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not TagInfo tag)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        // Double-click to scroll to the tagged commit in the graph
        if (e.ClickCount == 2)
        {
            viewModel.GitGraphViewModel?.SelectCommitBySha(tag.TargetSha);
            e.Handled = true;
            return;
        }

        viewModel.SelectTag(tag, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

        if (!viewModel.IsGraphMode)
            viewModel.ClosePullRequestViewCommand.Execute(null);

        e.Handled = true;
    }

    private void Tag_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not TagInfo tag)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        if (!tag.IsSelected)
            viewModel.SelectTag(tag, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    #endregion

    #region Worktree clicks

    private void Worktree_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not WorktreeInfo worktree)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        // Double-click to switch to the worktree
        if (e.ClickCount == 2 && !worktree.IsCurrent)
        {
            viewModel.SwitchToWorktreeAsync(worktree)
                .FireAndForget(nameof(viewModel.SwitchToWorktreeAsync), isUserAction: true);
            e.Handled = true;
            return;
        }

        viewModel.SelectWorktree(worktree, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

        if (!viewModel.IsGraphMode)
            viewModel.ClosePullRequestViewCommand.Execute(null);

        e.Handled = true;
    }

    private void Worktree_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not WorktreeInfo worktree)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        if (!worktree.IsSelected)
            viewModel.SelectWorktree(worktree, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
    }

    #endregion

    #region GitFlow action menu

    private Button? _lastChevronButton;
    private bool _isGitFlowMenuBuilding;

    // Snapshot of active GitFlow branches captured when the menu is built.
    // The Finish handlers read these back when the user clicks a menu item.
    private string? _activeRelease;
    private string? _activeHotfix;
    private List<string> _activeFeatures = new();

    private async void GitFlowActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null)
            return;

        if (_isGitFlowMenuBuilding)
            return;

        _lastChevronButton = button;
        e.Handled = true;

        try
        {
            _isGitFlowMenuBuilding = true;

            await BuildGitFlowContextMenu(button.ContextMenu);

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Right;
            button.ContextMenu.IsOpen = true;
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(GitFlowActionButton_Click), isUserAction: true);
        }
        finally
        {
            _isGitFlowMenuBuilding = false;
        }
    }

    private async Task BuildGitFlowContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();

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

        // Start Feature (always present — multiple concurrent features allowed)
        var startFeatureItem = new MenuItem
        {
            Header = "Start Feature",
            Icon = CreateColorDot("#8250DF")
        };
        startFeatureItem.Click += StartFeature_Click;
        menu.Items.Add(startFeatureItem);

        // Release — Start or Finish depending on whether one is already active
        if (_activeRelease != null)
        {
            var finishRelease = new MenuItem
            {
                Header = $"Finish Release ({_activeRelease})",
                Icon = CreateColorDot("#BF8700"),
                Tag = _activeRelease
            };
            finishRelease.Click += FinishRelease_Click;
            menu.Items.Add(finishRelease);
        }
        else
        {
            var startRelease = new MenuItem
            {
                Header = "Start Release",
                Icon = CreateColorDot("#BF8700")
            };
            startRelease.Click += StartRelease_Click;
            menu.Items.Add(startRelease);
        }

        // Hotfix — Start or Finish depending on whether one is already active
        if (_activeHotfix != null)
        {
            var finishHotfix = new MenuItem
            {
                Header = $"Finish Hotfix ({_activeHotfix})",
                Icon = CreateColorDot("#CF222E"),
                Tag = _activeHotfix
            };
            finishHotfix.Click += FinishHotfix_Click;
            menu.Items.Add(finishHotfix);
        }
        else
        {
            var startHotfix = new MenuItem
            {
                Header = "Start Hotfix",
                Icon = CreateColorDot("#CF222E")
            };
            startHotfix.Click += StartHotfix_Click;
            menu.Items.Add(startHotfix);
        }

        // Finish Feature submenu — features are multi-instance so this is always a list
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

    private async void FinishFeature_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string featureName)
            await DispatchFinishAsync(GitFlowBranchType.Feature, featureName);
    }

    private async void FinishRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_activeRelease != null)
            await DispatchFinishAsync(GitFlowBranchType.Release, _activeRelease);
    }

    private async void FinishHotfix_Click(object sender, RoutedEventArgs e)
    {
        if (_activeHotfix != null)
            await DispatchFinishAsync(GitFlowBranchType.Hotfix, _activeHotfix);
    }

    private async Task DispatchFinishAsync(GitFlowBranchType type, string flowName)
    {
        try
        {
            if (DataContext is MainViewModel viewModel)
                await viewModel.FinishGitFlowBranchByNameAsync(type, flowName);
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(DispatchFinishAsync), isUserAction: true);
        }
    }

    #endregion

    #region GitFlow quick-create popup

    private void StartFeature_Click(object sender, RoutedEventArgs e)
        => OpenQuickCreate(GitFlowBranchType.Feature, "Start Feature", "#8250DF", "feature/");

    private void StartRelease_Click(object sender, RoutedEventArgs e)
        => OpenQuickCreate(GitFlowBranchType.Release, "Start Release", "#BF8700", "release/");

    private void StartHotfix_Click(object sender, RoutedEventArgs e)
        => OpenQuickCreate(GitFlowBranchType.Hotfix, "Start Hotfix", "#CF222E", "hotfix/");

    private async void OpenQuickCreate(GitFlowBranchType branchType, string header, string color, string defaultPrefix)
    {
        try
        {
            _currentBranchType = branchType;

            // Pull prefix + suggested version from the VM in a single await.
            var context = DataContext is MainViewModel viewModel
                ? await viewModel.PrepareGitFlowQuickCreateAsync(branchType, defaultPrefix)
                : new MainViewModel.GitFlowQuickCreateContext(defaultPrefix, SuggestedVersion: null);

            _currentPrefix = context.Prefix;
            _suggestedVersion = context.SuggestedVersion;

            QuickCreateHeader.Text = header;
            QuickCreateTypeIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            QuickCreateNameBox.Text = "";
            QuickCreateNameBox.IsEnabled = true;
            QuickCreatePreview.Text = _currentPrefix + "...";
            QuickCreateStartButton.IsEnabled = false;
            QuickCreateProgress.Visibility = Visibility.Collapsed;

            if (_suggestedVersion != null)
            {
                QuickCreateVersionText.Text = _suggestedVersion.ToString();
                QuickCreateVersionPanel.Visibility = Visibility.Visible;
            }
            else
            {
                QuickCreateVersionPanel.Visibility = Visibility.Collapsed;
            }

            if (_lastChevronButton != null)
                QuickCreatePopup.PlacementTarget = _lastChevronButton;
            QuickCreatePopup.IsOpen = true;
            QuickCreateNameBox.Focus();
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(OpenQuickCreate), isUserAction: true);
        }
    }

    private void QuickCreateNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = QuickCreateNameBox.Text.Trim();
        QuickCreatePreview.Text = string.IsNullOrEmpty(name) ? _currentPrefix + "..." : _currentPrefix + name;
        QuickCreateStartButton.IsEnabled = BranchNameValidator.IsValid(name);
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
        try
        {
            if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
                return;

            var name = QuickCreateNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            QuickCreateNameBox.IsEnabled = false;
            QuickCreateStartButton.IsEnabled = false;
            QuickCreateProgress.Visibility = Visibility.Visible;

            var progress = new Progress<string>(msg => QuickCreateProgressText.Text = msg);
            var created = await viewModel.StartGitFlowBranchWithStashCheckAsync(_currentBranchType, name, progress);

            if (created)
                QuickCreatePopup.IsOpen = false;
            else
                ResetQuickCreateUI();
        }
        catch (Exception ex)
        {
            ResetQuickCreateUI();
            AsyncErrorHandler.Handle(ex, nameof(QuickCreateStart_Click), isUserAction: true);
        }
    }

    private void ResetQuickCreateUI()
    {
        QuickCreateNameBox.IsEnabled = true;
        QuickCreateStartButton.IsEnabled = !string.IsNullOrWhiteSpace(QuickCreateNameBox.Text);
        QuickCreateProgress.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Worktree create popup

    private Button? _lastWorktreeButton;

    private void AddWorktreeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        _lastWorktreeButton = button;
        e.Handled = true;

        WorktreeNameBox.Text = "";
        WorktreeNameBox.IsEnabled = true;
        WorktreePathPreview.Text = "Path: ...";
        WorktreeCreateButton.IsEnabled = false;
        WorktreeCreateProgress.Visibility = Visibility.Collapsed;

        WorktreeCreatePopup.PlacementTarget = button;
        WorktreeCreatePopup.IsOpen = true;
        WorktreeNameBox.Focus();
    }

    private void WorktreeNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = WorktreeNameBox.Text.Trim();
        WorktreeCreateButton.IsEnabled = BranchNameValidator.IsValid(name);

        // Delegate the preview to the VM so the sanitization rule stays in
        // one place (WorktreeOperations.SanitizeBranchNameForPath).
        WorktreePathPreview.Text = DataContext is MainViewModel viewModel
            ? viewModel.GetWorktreePathPreview(name)
            : "Path: ...";
    }

    private void WorktreeNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && WorktreeCreateButton.IsEnabled)
        {
            WorktreeCreateConfirm_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            WorktreeCreatePopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void WorktreeCreateCancel_Click(object sender, RoutedEventArgs e)
    {
        WorktreeCreatePopup.IsOpen = false;
    }

    private async void WorktreeCreateConfirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
                return;

            var branchName = WorktreeNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(branchName))
                return;

            WorktreeNameBox.IsEnabled = false;
            WorktreeCreateButton.IsEnabled = false;
            WorktreeCreateProgress.Visibility = Visibility.Visible;
            WorktreeCreateProgressText.Text = "Creating worktree...";

            await viewModel.CreateWorktreeWithNewBranchAsync(branchName);
            WorktreeCreatePopup.IsOpen = false;
        }
        catch (Exception ex)
        {
            ResetWorktreeCreateUI();
            AsyncErrorHandler.Handle(ex, nameof(WorktreeCreateConfirm_Click), isUserAction: true);
        }
    }

    private void ResetWorktreeCreateUI()
    {
        WorktreeNameBox.IsEnabled = true;
        WorktreeCreateButton.IsEnabled = !string.IsNullOrWhiteSpace(WorktreeNameBox.Text);
        WorktreeCreateProgress.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Branch create popup

    private Button? _lastBranchButton;

    private void AddBranchButton_Click(object sender, RoutedEventArgs e)
    {
        _lastBranchButton = sender as Button;
        e.Handled = true;

        if (DataContext is MainViewModel viewModel)
            viewModel.CreateBranch(); // VM sets state + IsBranchInputVisible → PropertyChanged opens popup
    }

    private void OpenBranchCreatePopup(MainViewModel viewModel)
    {
        var isRename = viewModel.BranchInputActionText == "Rename";

        BranchCreateHeader.Text = isRename ? "Rename Branch" : "Create Branch";
        BranchCreateDescription.Text = isRename
            ? "Enter the new name for the branch:"
            : "Enter a name for the new branch:";
        BranchNameBox.Text = viewModel.NewBranchName;
        BranchNameBox.IsEnabled = true;
        BranchCreateButton.Content = viewModel.BranchInputActionText;
        BranchCreateButton.IsEnabled = !string.IsNullOrWhiteSpace(viewModel.NewBranchName);
        BranchCreateProgress.Visibility = Visibility.Collapsed;

        BranchCreatePopup.PlacementTarget = _lastBranchButton ?? (UIElement)this;

        BranchCreatePopup.IsOpen = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            BranchNameBox.Focus();
            if (isRename)
                BranchNameBox.SelectAll();
        });
    }

    private void BranchNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = BranchNameBox.Text.Trim();
        BranchCreateButton.IsEnabled = BranchNameValidator.IsValid(name);

        if (DataContext is MainViewModel viewModel)
            viewModel.NewBranchName = BranchNameBox.Text;
    }

    private void BranchNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BranchCreateButton.IsEnabled)
        {
            BranchCreateConfirm_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            BranchCreatePopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void BranchNameBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
        {
            if (BranchNameValidator.IsForbiddenCharacter(c))
            {
                e.Handled = true;
                return;
            }
        }

        if (sender is TextBox textBox)
        {
            var composed = textBox.Text.Insert(textBox.CaretIndex, e.Text);
            if (BranchNameValidator.HasInvalidStructure(composed))
                e.Handled = true;
        }
    }

    private void BranchCreateCancel_Click(object sender, RoutedEventArgs e)
    {
        BranchCreatePopup.IsOpen = false;
    }

    private async void BranchCreateConfirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainViewModel viewModel)
                return;

            var name = BranchNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            BranchNameBox.IsEnabled = false;
            BranchCreateButton.IsEnabled = false;
            BranchCreateProgress.Visibility = Visibility.Visible;
            BranchCreateProgressText.Text = viewModel.BranchInputActionText == "Rename"
                ? "Renaming branch..."
                : "Creating branch...";

            try
            {
                viewModel.NewBranchName = name;
                await viewModel.ConfirmCreateBranchAsync();
            }
            finally
            {
                BranchCreatePopup.IsOpen = false;
                ResetBranchCreateUI();
            }
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(BranchCreateConfirm_Click), isUserAction: true);
        }
    }

    private void BranchCreatePopup_Closed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.IsBranchInputVisible)
            viewModel.CancelBranchInputCommand.Execute(null);
    }

    private void ResetBranchCreateUI()
    {
        BranchNameBox.IsEnabled = true;
        BranchCreateButton.IsEnabled = !string.IsNullOrWhiteSpace(BranchNameBox.Text);
        BranchCreateProgress.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Pull requests

    private void PullRequest_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PullRequestInfo pr)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        viewModel.ActivatePullRequestAsync(pr)
            .FireAndForget(nameof(viewModel.ActivatePullRequestAsync), isUserAction: true);
        e.Handled = true;
    }

    private void PullRequest_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not PullRequestInfo pr)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        if (!pr.IsSelected)
            viewModel.SelectPullRequestInSidebar(pr);
    }

    private void CreatePRButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.OpenCreatePullRequestCommand.Execute(null);
    }

    #endregion

    #region Submodule clicks

    /// <summary>
    /// Context-sensitive double-click on a submodule row in the sidebar.
    /// Uninitialised submodules trigger init/clone; initialised ones open
    /// the submodule as a repository (switches Leaf to view it). Single
    /// clicks are intentionally inert today — selection state isn't
    /// surfaced for submodules and we'd rather not make rows feel
    /// click-actionable when nothing happens.
    /// </summary>
    /// <remarks>
    /// Mirrors the <c>Branch_MouseLeftButtonDown</c> pattern: handler
    /// owns the click-count check and the dispatch, the VM owns the
    /// commands. Conflicted submodules are treated as initialised — the
    /// user wants to navigate into them and resolve, not re-init.
    /// </remarks>
    private void Submodule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not SubmoduleInfo submodule)
            return;

        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRepository == null)
            return;

        if (e.ClickCount != 2)
            return;

        if (submodule.IsInitialized)
        {
            viewModel.OpenSubmoduleAsRepositoryAsync(submodule)
                .FireAndForget(nameof(viewModel.OpenSubmoduleAsRepositoryAsync), isUserAction: true);
        }
        else
        {
            viewModel.InitSubmoduleAsync(submodule)
                .FireAndForget(nameof(viewModel.InitSubmoduleAsync), isUserAction: true);
        }

        e.Handled = true;
    }

    #endregion

    #region UI helpers

    /// <summary>
    /// Build a small coloured dot used as a MenuItem icon. Pure UI
    /// drawing — stays in the code-behind.
    /// </summary>
    private static Border CreateColorDot(string hexColor) => new()
    {
        Width = 8,
        Height = 8,
        CornerRadius = new CornerRadius(4),
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)),
        VerticalAlignment = VerticalAlignment.Center
    };

    #endregion
}
