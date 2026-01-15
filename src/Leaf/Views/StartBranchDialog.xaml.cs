using System.Windows;
using System.Windows.Controls;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views;

public partial class StartBranchDialog : Window
{
    private readonly IGitFlowService _gitFlowService;
    private readonly IGitService _gitService;
    private readonly string _repoPath;
    private GitFlowConfig? _config;
    private SemanticVersion? _suggestedVersion;

    public GitFlowBranchType SelectedBranchType { get; private set; }
    public string? BranchName { get; private set; }
    public string? BaseRef { get; private set; }

    public StartBranchDialog(IGitFlowService gitFlowService, IGitService gitService, string repoPath, GitFlowBranchType? preselectedType = null)
    {
        InitializeComponent();
        _gitFlowService = gitFlowService;
        _gitService = gitService;
        _repoPath = repoPath;

        LoadConfigAndSetDefaults(preselectedType);
    }

    private async void LoadConfigAndSetDefaults(GitFlowBranchType? preselectedType)
    {
        try
        {
            _config = await _gitFlowService.GetConfigAsync(_repoPath);

            if (preselectedType.HasValue)
            {
                // Hide branch type selection if preselected
                BranchTypeSection.Visibility = Visibility.Collapsed;

                switch (preselectedType.Value)
                {
                    case GitFlowBranchType.Feature:
                        FeatureRadio.IsChecked = true;
                        break;
                    case GitFlowBranchType.Release:
                        ReleaseRadio.IsChecked = true;
                        break;
                    case GitFlowBranchType.Hotfix:
                        HotfixRadio.IsChecked = true;
                        break;
                    case GitFlowBranchType.Support:
                        SupportRadio.IsChecked = true;
                        break;
                }
            }
            else
            {
                FeatureRadio.IsChecked = true;
            }

            UpdateUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load GitFlow configuration:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void BranchType_Changed(object sender, RoutedEventArgs e)
    {
        UpdateUI();
    }

    private async void UpdateUI()
    {
        if (_config == null) return;

        GitFlowBranchType type = GetSelectedBranchType();
        string prefix = GetPrefix(type);

        switch (type)
        {
            case GitFlowBranchType.Feature:
                HeaderText.Text = "Start Feature";
                SubheaderText.Text = "Create a new feature branch from develop";
                NameLabel.Text = "Feature Name";
                NameHint.Text = "Enter a descriptive name (e.g., user-authentication)";
                BaseBranchInfoText.Text = $"This branch will be created from {_config.DevelopBranch}.";
                BaseBranchSection.Visibility = Visibility.Collapsed;
                VersionSuggestionPanel.Visibility = Visibility.Collapsed;
                break;

            case GitFlowBranchType.Release:
                HeaderText.Text = "Start Release";
                SubheaderText.Text = "Create a new release branch from develop";
                NameLabel.Text = "Version";
                NameHint.Text = "Enter the release version (e.g., 1.2.0)";
                BaseBranchInfoText.Text = $"This branch will be created from {_config.DevelopBranch}.";
                BaseBranchSection.Visibility = Visibility.Collapsed;
                await LoadSuggestedVersion(GitFlowBranchType.Release);
                break;

            case GitFlowBranchType.Hotfix:
                HeaderText.Text = "Start Hotfix";
                SubheaderText.Text = "Create a hotfix branch from main";
                NameLabel.Text = "Version";
                NameHint.Text = "Enter the hotfix version (e.g., 1.1.1)";
                BaseBranchInfoText.Text = $"This branch will be created from {_config.MainBranch}.";
                BaseBranchSection.Visibility = Visibility.Collapsed;
                await LoadSuggestedVersion(GitFlowBranchType.Hotfix);
                break;

            case GitFlowBranchType.Support:
                HeaderText.Text = "Start Support";
                SubheaderText.Text = "Create a support branch for a previous version";
                NameLabel.Text = "Support Name";
                NameHint.Text = "Enter the support branch name (e.g., 1.x)";
                BaseBranchInfoText.Text = "This branch will be created from the specified tag or commit.";
                BaseBranchSection.Visibility = Visibility.Visible;
                VersionSuggestionPanel.Visibility = Visibility.Collapsed;
                break;
        }

        UpdateBranchPreview();
        ValidateInput();
    }

    private async Task LoadSuggestedVersion(GitFlowBranchType type)
    {
        try
        {
            _suggestedVersion = await _gitFlowService.SuggestNextVersionAsync(_repoPath, type);
            SuggestedVersionText.Text = _suggestedVersion.ToString();
            VersionSuggestionPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            VersionSuggestionPanel.Visibility = Visibility.Collapsed;
        }
    }

    private GitFlowBranchType GetSelectedBranchType()
    {
        if (FeatureRadio.IsChecked == true) return GitFlowBranchType.Feature;
        if (ReleaseRadio.IsChecked == true) return GitFlowBranchType.Release;
        if (HotfixRadio.IsChecked == true) return GitFlowBranchType.Hotfix;
        if (SupportRadio.IsChecked == true) return GitFlowBranchType.Support;
        return GitFlowBranchType.Feature;
    }

    private string GetPrefix(GitFlowBranchType type)
    {
        if (_config == null) return string.Empty;

        return type switch
        {
            GitFlowBranchType.Feature => _config.FeaturePrefix,
            GitFlowBranchType.Release => _config.ReleasePrefix,
            GitFlowBranchType.Hotfix => _config.HotfixPrefix,
            GitFlowBranchType.Support => _config.SupportPrefix,
            _ => string.Empty
        };
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateBranchPreview();
        ValidateInput();
    }

    private void SuggestedVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_suggestedVersion != null)
        {
            NameTextBox.Text = _suggestedVersion.ToString();
        }
    }

    private void UpdateBranchPreview()
    {
        if (_config == null) return;

        var type = GetSelectedBranchType();
        var prefix = GetPrefix(type);
        var name = NameTextBox.Text.Trim();

        BranchPreviewText.Text = string.IsNullOrEmpty(name) ? $"{prefix}..." : $"{prefix}{name}";
    }

    private async void ValidateInput()
    {
        if (_config == null) return;

        var name = NameTextBox.Text.Trim();
        var type = GetSelectedBranchType();

        if (string.IsNullOrWhiteSpace(name))
        {
            StartButton.IsEnabled = false;
            ValidationErrorBorder.Visibility = Visibility.Collapsed;
            return;
        }

        // Check for invalid characters
        if (name.Contains(" ") || name.Contains("..") || name.StartsWith("/") || name.EndsWith("/"))
        {
            ValidationErrorBorder.Visibility = Visibility.Visible;
            ValidationErrorText.Text = "Branch name contains invalid characters.";
            StartButton.IsEnabled = false;
            return;
        }

        // For support branches, check base ref
        if (type == GitFlowBranchType.Support && string.IsNullOrWhiteSpace(BaseRefTextBox.Text))
        {
            ValidationErrorBorder.Visibility = Visibility.Visible;
            ValidationErrorText.Text = "Please specify a base tag or commit for the support branch.";
            StartButton.IsEnabled = false;
            return;
        }

        // Validate with service
        try
        {
            (bool isValid, string? error) result;

            switch (type)
            {
                case GitFlowBranchType.Feature:
                    result = await _gitFlowService.ValidateStartFeatureAsync(_repoPath, name);
                    break;
                case GitFlowBranchType.Release:
                    result = await _gitFlowService.ValidateStartReleaseAsync(_repoPath, name);
                    break;
                case GitFlowBranchType.Hotfix:
                    result = await _gitFlowService.ValidateStartHotfixAsync(_repoPath, name);
                    break;
                default:
                    result = (true, null);
                    break;
            }

            if (!result.isValid)
            {
                ValidationErrorBorder.Visibility = Visibility.Visible;
                ValidationErrorText.Text = result.error ?? "Invalid branch name.";
                StartButton.IsEnabled = false;
                return;
            }
        }
        catch
        {
            // If validation fails, allow user to proceed (will fail on actual creation)
        }

        ValidationErrorBorder.Visibility = Visibility.Collapsed;
        StartButton.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var type = GetSelectedBranchType();

        ProgressSection.Visibility = Visibility.Visible;
        StartButton.IsEnabled = false;

        try
        {
            var progress = new Progress<string>(msg => ProgressText.Text = msg);

            // Check for uncommitted changes before starting
            ProgressText.Text = "Checking for uncommitted changes...";
            var repoInfo = await _gitService.GetRepositoryInfoAsync(_repoPath);

            if (repoInfo.IsDirty)
            {
                ProgressSection.Visibility = Visibility.Collapsed;

                var result = MessageBox.Show(
                    "You have uncommitted changes in your working directory.\n\n" +
                    "Starting a new GitFlow branch requires switching branches, which may fail or cause issues with your changes.\n\n" +
                    "Would you like to stash your changes first? They can be restored after switching.",
                    "Uncommitted Changes Detected",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                {
                    StartButton.IsEnabled = true;
                    return;
                }

                if (result == MessageBoxResult.Yes)
                {
                    ProgressSection.Visibility = Visibility.Visible;
                    ProgressText.Text = "Stashing changes...";
                    await _gitService.StashAsync(_repoPath, $"Auto-stash before starting {type.ToString().ToLower()} '{name}'");
                }
                // If No, proceed anyway (user's choice)

                ProgressSection.Visibility = Visibility.Visible;
            }

            switch (type)
            {
                case GitFlowBranchType.Feature:
                    ProgressText.Text = "Creating feature branch...";
                    await _gitFlowService.StartFeatureAsync(_repoPath, name, progress);
                    break;

                case GitFlowBranchType.Release:
                    ProgressText.Text = "Creating release branch...";
                    await _gitFlowService.StartReleaseAsync(_repoPath, name, progress);
                    break;

                case GitFlowBranchType.Hotfix:
                    ProgressText.Text = "Creating hotfix branch...";
                    await _gitFlowService.StartHotfixAsync(_repoPath, name, progress);
                    break;

                case GitFlowBranchType.Support:
                    ProgressText.Text = "Creating support branch...";
                    await _gitFlowService.StartSupportAsync(_repoPath, name, BaseRefTextBox.Text.Trim(), progress);
                    break;
            }

            SelectedBranchType = type;
            BranchName = name;
            BaseRef = type == GitFlowBranchType.Support ? BaseRefTextBox.Text.Trim() : null;

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create branch:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            ProgressSection.Visibility = Visibility.Collapsed;
            StartButton.IsEnabled = true;
        }
    }
}
