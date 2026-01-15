using System.Windows;
using System.Windows.Media;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views;

public partial class FinishBranchDialog : Window
{
    private readonly IGitFlowService _gitFlowService;
    private readonly string _repoPath;
    private readonly string _branchName;
    private readonly GitFlowBranchType _branchType;
    private readonly string _flowName;
    private GitFlowConfig? _config;
    private string? _changelogContent;

    public MergeStrategy SelectedMergeStrategy { get; private set; }
    public bool DeleteBranch { get; private set; }
    public bool PushAfterFinish { get; private set; }
    public string? TagMessage { get; private set; }

    public FinishBranchDialog(
        IGitFlowService gitFlowService,
        string repoPath,
        string branchName,
        GitFlowBranchType branchType,
        string flowName)
    {
        InitializeComponent();
        _gitFlowService = gitFlowService;
        _repoPath = repoPath;
        _branchName = branchName;
        _branchType = branchType;
        _flowName = flowName;

        LoadConfigAndSetupUI();
    }

    private async void LoadConfigAndSetupUI()
    {
        try
        {
            _config = await _gitFlowService.GetConfigAsync(_repoPath);
            SetupUI();
            await LoadChangelog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load configuration:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetupUI()
    {
        if (_config == null) return;

        BranchNameText.Text = _branchName;

        // Set merge strategy default from config
        switch (_config.DefaultMergeStrategy)
        {
            case MergeStrategy.Merge:
                MergeStrategyMerge.IsChecked = true;
                break;
            case MergeStrategy.Squash:
                MergeStrategySquash.IsChecked = true;
                break;
            case MergeStrategy.Rebase:
                MergeStrategyRebase.IsChecked = true;
                break;
        }

        DeleteBranchCheckBox.IsChecked = _config.DeleteBranchAfterFinish;
        PushCheckBox.IsChecked = _config.AutoPushAfterFinish;

        switch (_branchType)
        {
            case GitFlowBranchType.Feature:
                HeaderText.Text = "Finish Feature";
                BranchTypeIndicator.Background = new SolidColorBrush(Color.FromRgb(0x82, 0x50, 0xDF));
                MergeInfoText.Text = $"This feature will be merged into {_config.DevelopBranch}.";
                TagOptionsSection.Visibility = Visibility.Collapsed;
                ChangelogSection.Visibility = Visibility.Collapsed;
                break;

            case GitFlowBranchType.Release:
                HeaderText.Text = "Finish Release";
                BranchTypeIndicator.Background = new SolidColorBrush(Color.FromRgb(0xBF, 0x87, 0x00));
                MergeInfoText.Text = $"This release will be merged into {_config.MainBranch} and {_config.DevelopBranch}, and a tag will be created.";
                TagOptionsSection.Visibility = Visibility.Visible;
                ChangelogSection.Visibility = Visibility.Visible;
                TagNameTextBox.Text = $"{_config.VersionTagPrefix}{_flowName}";
                TagMessageTextBox.Text = $"Release {_flowName}";
                PushDescriptionText.Text = "Push merged changes and tag to origin";
                break;

            case GitFlowBranchType.Hotfix:
                HeaderText.Text = "Finish Hotfix";
                BranchTypeIndicator.Background = new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E));
                MergeInfoText.Text = $"This hotfix will be merged into {_config.MainBranch} and {_config.DevelopBranch}, and a tag will be created.";
                TagOptionsSection.Visibility = Visibility.Visible;
                ChangelogSection.Visibility = Visibility.Visible;
                TagNameTextBox.Text = $"{_config.VersionTagPrefix}{_flowName}";
                TagMessageTextBox.Text = $"Hotfix {_flowName}";
                PushDescriptionText.Text = "Push merged changes and tag to origin";
                break;

            default:
                HeaderText.Text = "Finish Branch";
                BranchTypeIndicator.Background = new SolidColorBrush(Color.FromRgb(0x57, 0x60, 0x6A));
                TagOptionsSection.Visibility = Visibility.Collapsed;
                ChangelogSection.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private async Task LoadChangelog()
    {
        if (_branchType != GitFlowBranchType.Release && _branchType != GitFlowBranchType.Hotfix)
            return;

        try
        {
            ChangelogPreviewText.Text = "Loading changelog...";

            // Get current version to generate changelog from
            var currentVersion = await _gitFlowService.DetectCurrentVersionAsync(_repoPath);
            var fromVersion = currentVersion?.ToString();

            _changelogContent = await _gitFlowService.GenerateChangelogAsync(_repoPath, fromVersion, _flowName);

            if (string.IsNullOrWhiteSpace(_changelogContent))
            {
                ChangelogPreviewText.Text = "No conventional commits found for changelog generation.";
            }
            else
            {
                ChangelogPreviewText.Text = _changelogContent;
            }
        }
        catch (Exception ex)
        {
            ChangelogPreviewText.Text = $"Failed to generate changelog:\n{ex.Message}";
        }
    }

    private async void RefreshChangelog_Click(object sender, RoutedEventArgs e)
    {
        await LoadChangelog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        // Get selected merge strategy
        MergeStrategy strategy;
        if (MergeStrategySquash.IsChecked == true)
            strategy = MergeStrategy.Squash;
        else if (MergeStrategyRebase.IsChecked == true)
            strategy = MergeStrategy.Rebase;
        else
            strategy = MergeStrategy.Merge;

        bool deleteBranch = DeleteBranchCheckBox.IsChecked == true;
        bool push = PushCheckBox.IsChecked == true;
        string? tagMessage = TagMessageTextBox.Text.Trim();
        if (string.IsNullOrEmpty(tagMessage)) tagMessage = null;

        ProgressSection.Visibility = Visibility.Visible;
        FinishButton.IsEnabled = false;

        try
        {
            var progress = new Progress<string>(msg => ProgressText.Text = msg);

            switch (_branchType)
            {
                case GitFlowBranchType.Feature:
                    ProgressText.Text = "Finishing feature...";
                    await _gitFlowService.FinishFeatureAsync(_repoPath, _flowName, strategy, deleteBranch, progress);
                    break;

                case GitFlowBranchType.Release:
                    ProgressText.Text = "Finishing release...";
                    await _gitFlowService.FinishReleaseAsync(_repoPath, _flowName, strategy, deleteBranch, tagMessage, progress);

                    // Append changelog if requested
                    if (AppendToChangelogCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(_changelogContent))
                    {
                        ProgressText.Text = "Updating CHANGELOG.md...";
                        await _gitFlowService.AppendToChangelogFileAsync(_repoPath, _changelogContent);
                    }
                    break;

                case GitFlowBranchType.Hotfix:
                    ProgressText.Text = "Finishing hotfix...";
                    await _gitFlowService.FinishHotfixAsync(_repoPath, _flowName, strategy, deleteBranch, tagMessage, progress);

                    // Append changelog if requested
                    if (AppendToChangelogCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(_changelogContent))
                    {
                        ProgressText.Text = "Updating CHANGELOG.md...";
                        await _gitFlowService.AppendToChangelogFileAsync(_repoPath, _changelogContent);
                    }
                    break;
            }

            SelectedMergeStrategy = strategy;
            DeleteBranch = deleteBranch;
            PushAfterFinish = push;
            TagMessage = tagMessage;

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to finish branch:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            ProgressSection.Visibility = Visibility.Collapsed;
            FinishButton.IsEnabled = true;
        }
    }
}
