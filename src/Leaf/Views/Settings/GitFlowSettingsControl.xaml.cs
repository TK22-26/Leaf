using System.Windows.Controls;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views.Settings;

/// <summary>
/// Settings control for GitFlow default configuration.
/// </summary>
public partial class GitFlowSettingsControl : UserControl, ISettingsSectionControl
{
    public GitFlowSettingsControl()
    {
        InitializeComponent();
    }

    public void LoadSettings(AppSettings settings, CredentialService credentialService)
    {
        // Load defaults from settings
        GitFlowDefaultMainBranch.Text = settings.GitFlowDefaultMainBranch;
        GitFlowDefaultDevelopBranch.Text = settings.GitFlowDefaultDevelopBranch;
        GitFlowDefaultFeaturePrefix.Text = settings.GitFlowDefaultFeaturePrefix;
        GitFlowDefaultReleasePrefix.Text = settings.GitFlowDefaultReleasePrefix;
        GitFlowDefaultHotfixPrefix.Text = settings.GitFlowDefaultHotfixPrefix;
        GitFlowDefaultVersionTagPrefix.Text = settings.GitFlowDefaultVersionTagPrefix;
        GitFlowDefaultDeleteBranch.IsChecked = settings.GitFlowDefaultDeleteBranch;
        GitFlowDefaultGenerateChangelog.IsChecked = settings.GitFlowDefaultGenerateChangelog;
    }

    public void SaveSettings(AppSettings settings, CredentialService credentialService)
    {
        // Save defaults to settings
        settings.GitFlowDefaultMainBranch = GitFlowDefaultMainBranch.Text;
        settings.GitFlowDefaultDevelopBranch = GitFlowDefaultDevelopBranch.Text;
        settings.GitFlowDefaultFeaturePrefix = GitFlowDefaultFeaturePrefix.Text;
        settings.GitFlowDefaultReleasePrefix = GitFlowDefaultReleasePrefix.Text;
        settings.GitFlowDefaultHotfixPrefix = GitFlowDefaultHotfixPrefix.Text;
        settings.GitFlowDefaultVersionTagPrefix = GitFlowDefaultVersionTagPrefix.Text;
        settings.GitFlowDefaultDeleteBranch = GitFlowDefaultDeleteBranch.IsChecked == true;
        settings.GitFlowDefaultGenerateChangelog = GitFlowDefaultGenerateChangelog.IsChecked == true;
    }
}
