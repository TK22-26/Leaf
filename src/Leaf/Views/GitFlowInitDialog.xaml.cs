using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views;

public partial class GitFlowInitDialog : Window
{
    private readonly IGitFlowService _gitFlowService;
    private readonly SettingsService _settingsService;
    private readonly string _repoPath;
    private int _currentStep = 1;
    private const int TotalSteps = 5;

    public GitFlowConfig? Result { get; private set; }

    public GitFlowInitDialog(IGitFlowService gitFlowService, SettingsService settingsService, string repoPath)
    {
        InitializeComponent();
        _gitFlowService = gitFlowService;
        _settingsService = settingsService;
        _repoPath = repoPath;

        LoadExistingConfigOrDefaults();
        UpdateStepVisuals();
    }

    private async void LoadExistingConfigOrDefaults()
    {
        try
        {
            var existingConfig = await _gitFlowService.GetConfigAsync(_repoPath);
            if (existingConfig != null && existingConfig.IsInitialized)
            {
                // Use existing repo config
                MainBranchTextBox.Text = existingConfig.MainBranch;
                DevelopBranchTextBox.Text = existingConfig.DevelopBranch;
                FeaturePrefixTextBox.Text = existingConfig.FeaturePrefix;
                ReleasePrefixTextBox.Text = existingConfig.ReleasePrefix;
                HotfixPrefixTextBox.Text = existingConfig.HotfixPrefix;
                SupportPrefixTextBox.Text = existingConfig.SupportPrefix;
                VersionTagPrefixTextBox.Text = existingConfig.VersionTagPrefix;

                switch (existingConfig.DefaultMergeStrategy)
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

                DeleteBranchCheckBox.IsChecked = existingConfig.DeleteBranchAfterFinish;
                AutoPushCheckBox.IsChecked = existingConfig.AutoPushAfterFinish;
            }
            else
            {
                // Load defaults from settings
                LoadDefaultsFromSettings();
            }
        }
        catch
        {
            // Load defaults from settings on error
            LoadDefaultsFromSettings();
        }
    }

    private void LoadDefaultsFromSettings()
    {
        var settings = _settingsService.LoadSettings();
        MainBranchTextBox.Text = settings.GitFlowDefaultMainBranch;
        DevelopBranchTextBox.Text = settings.GitFlowDefaultDevelopBranch;
        FeaturePrefixTextBox.Text = settings.GitFlowDefaultFeaturePrefix;
        ReleasePrefixTextBox.Text = settings.GitFlowDefaultReleasePrefix;
        HotfixPrefixTextBox.Text = settings.GitFlowDefaultHotfixPrefix;
        SupportPrefixTextBox.Text = "support/"; // Not in settings yet
        VersionTagPrefixTextBox.Text = settings.GitFlowDefaultVersionTagPrefix;
        DeleteBranchCheckBox.IsChecked = settings.GitFlowDefaultDeleteBranch;
        // AutoPush defaults to false as it's a more advanced option
    }

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out int step))
        {
            if (step <= _currentStep)
            {
                _currentStep = step;
                UpdateStepVisuals();
            }
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateStepVisuals();
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < TotalSteps)
        {
            _currentStep++;
            UpdateStepVisuals();

            if (_currentStep == TotalSteps)
            {
                UpdateSummary();
                await CheckDevelopBranchExists();
            }
        }
        else
        {
            await InitializeGitFlow();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateStepVisuals()
    {
        // Update step circles
        UpdateStepCircle(Step1Circle, 1);
        UpdateStepCircle(Step2Circle, 2);
        UpdateStepCircle(Step3Circle, 3);
        UpdateStepCircle(Step4Circle, 4);
        UpdateStepCircle(Step5Circle, 5);

        // Update content visibility
        Step1Content.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Content.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Content.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Content.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step5Content.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;

        // Update navigation buttons
        BackButton.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;

        if (_currentStep == TotalSteps)
        {
            NextButton.Content = "Initialize";
        }
        else
        {
            NextButton.Content = "Next";
        }
    }

    private void UpdateStepCircle(Border circle, int stepNumber)
    {
        var textBlock = circle.Child as TextBlock;
        if (textBlock == null) return;

        if (stepNumber < _currentStep)
        {
            // Completed step
            circle.Background = (Brush)FindResource("AccentFillColorDefaultBrush");
            textBlock.Text = "✓";
            textBlock.Foreground = Brushes.White;
        }
        else if (stepNumber == _currentStep)
        {
            // Current step
            circle.Background = (Brush)FindResource("AccentFillColorDefaultBrush");
            textBlock.Text = stepNumber.ToString();
            textBlock.Foreground = Brushes.White;
        }
        else
        {
            // Future step
            circle.Background = (Brush)FindResource("ControlFillColorDefaultBrush");
            textBlock.Text = stepNumber.ToString();
            textBlock.Foreground = (Brush)FindResource("TextFillColorSecondaryBrush");
        }
    }

    private void UpdateSummary()
    {
        SummaryMainBranch.Text = MainBranchTextBox.Text;
        SummaryDevelopBranch.Text = DevelopBranchTextBox.Text;
        SummaryFeaturePrefix.Text = FeaturePrefixTextBox.Text;
        SummaryReleasePrefix.Text = ReleasePrefixTextBox.Text;
        SummaryHotfixPrefix.Text = HotfixPrefixTextBox.Text;
        SummaryVersionTagPrefix.Text = VersionTagPrefixTextBox.Text;

        if (MergeStrategyMerge.IsChecked == true)
            SummaryMergeStrategy.Text = "Merge";
        else if (MergeStrategySquash.IsChecked == true)
            SummaryMergeStrategy.Text = "Squash";
        else if (MergeStrategyRebase.IsChecked == true)
            SummaryMergeStrategy.Text = "Rebase";

        SummaryDeleteBranch.Text = DeleteBranchCheckBox.IsChecked == true ? "Yes" : "No";
        SummaryAutoPush.Text = AutoPushCheckBox.IsChecked == true ? "Yes" : "No";
    }

    private async Task CheckDevelopBranchExists()
    {
        try
        {
            var status = await _gitFlowService.GetStatusAsync(_repoPath);
            CreateDevelopWarning.Visibility = !status.IsInitialized ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            CreateDevelopWarning.Visibility = Visibility.Visible;
        }
    }

    private GitFlowConfig BuildConfig()
    {
        var strategy = MergeStrategy.Merge;
        if (MergeStrategySquash.IsChecked == true)
            strategy = MergeStrategy.Squash;
        else if (MergeStrategyRebase.IsChecked == true)
            strategy = MergeStrategy.Rebase;

        return new GitFlowConfig
        {
            IsInitialized = true,
            MainBranch = MainBranchTextBox.Text.Trim(),
            DevelopBranch = DevelopBranchTextBox.Text.Trim(),
            FeaturePrefix = FeaturePrefixTextBox.Text.Trim(),
            ReleasePrefix = ReleasePrefixTextBox.Text.Trim(),
            HotfixPrefix = HotfixPrefixTextBox.Text.Trim(),
            SupportPrefix = SupportPrefixTextBox.Text.Trim(),
            VersionTagPrefix = VersionTagPrefixTextBox.Text.Trim(),
            DefaultMergeStrategy = strategy,
            DeleteBranchAfterFinish = DeleteBranchCheckBox.IsChecked == true,
            AutoPushAfterFinish = AutoPushCheckBox.IsChecked == true
        };
    }

    private async Task InitializeGitFlow()
    {
        var config = BuildConfig();

        // Validate
        if (string.IsNullOrWhiteSpace(config.MainBranch))
        {
            MessageBox.Show("Main branch name is required.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _currentStep = 2;
            UpdateStepVisuals();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.DevelopBranch))
        {
            MessageBox.Show("Develop branch name is required.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _currentStep = 2;
            UpdateStepVisuals();
            return;
        }

        // Show progress
        InitProgressSection.Visibility = Visibility.Visible;
        NextButton.IsEnabled = false;
        BackButton.IsEnabled = false;

        try
        {
            InitProgressText.Text = "Initializing GitFlow...";
            await _gitFlowService.InitializeAsync(_repoPath, config);

            Result = config;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize GitFlow:\n\n{ex.Message}",
                "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);

            InitProgressSection.Visibility = Visibility.Collapsed;
            NextButton.IsEnabled = true;
            BackButton.IsEnabled = true;
        }
    }
}
