using System;
using System.Diagnostics;
using System.Windows;
using Leaf.Services;

namespace Leaf.Views;

public partial class AboutDialog : Window
{
    private const string GitHubUrl = "https://github.com/TK22-26/Leaf";
    private const string WikiUrl = "https://github.com/TK22-26/Leaf/wiki";
    private const string ReleasesUrl = "https://github.com/TK22-26/Leaf/releases";
    private const string IssuesUrl = "https://github.com/TK22-26/Leaf/issues";

    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = UpdateService.CurrentVersionString;
        CopyrightText.Text = $"© {DateTime.Now.Year} Tim Kostka";
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(GitHubUrl);
    private void OpenWiki_Click(object sender, RoutedEventArgs e) => OpenUrl(WikiUrl);
    private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenUrl(ReleasesUrl);
    private void OpenIssues_Click(object sender, RoutedEventArgs e) => OpenUrl(IssuesUrl);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open link: {ex.Message}", "Open URL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
