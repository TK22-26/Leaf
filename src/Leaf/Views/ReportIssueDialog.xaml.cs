using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Services;

namespace Leaf.Views;

public partial class ReportIssueDialog : Window
{
    private const string GitHubOwner = "TK22-26";
    private const string GitHubRepo = "Leaf";

    public ReportIssueDialog()
    {
        InitializeComponent();
        BodyTextBox.Text = GetDefaultBody();
    }

    private static string GetDefaultBody()
    {
        return $"""
## Description

[Describe the issue here]

## Steps to Reproduce

1.
2.
3.

## Expected Behavior

[What you expected to happen]

## Actual Behavior

[What actually happened]

## Environment

- Leaf Version: {UpdateService.CurrentVersionString}
- OS: Windows {Environment.OSVersion.Version}
""";
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SubmitButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleTextBox.Text);
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();
        var body = BodyTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            ShowStatus("Please enter a title for the issue.", isError: true);
            return;
        }

        // Disable inputs while submitting
        TitleTextBox.IsEnabled = false;
        BodyTextBox.IsEnabled = false;
        SubmitButton.IsEnabled = false;

        ShowStatus("Creating issue...", isError: false, isProgress: true);

        try
        {
            var (success, output, error) = await CreateGitHubIssueAsync(title, body);

            if (success)
            {
                ShowStatus("Issue created successfully!", isError: false);

                // Try to extract the issue URL from the output and open it
                var issueUrl = ExtractIssueUrl(output);
                if (!string.IsNullOrEmpty(issueUrl))
                {
                    var result = MessageBox.Show(
                        this,
                        $"Issue created successfully!\n\nWould you like to open it in your browser?\n\n{issueUrl}",
                        "Issue Created",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        OpenUrl(issueUrl);
                    }
                }

                DialogResult = true;
            }
            else
            {
                ShowStatus($"Failed to create issue: {error}", isError: true);

                // Re-enable inputs on failure
                TitleTextBox.IsEnabled = true;
                BodyTextBox.IsEnabled = true;
                SubmitButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", isError: true);

            // Re-enable inputs on error
            TitleTextBox.IsEnabled = true;
            BodyTextBox.IsEnabled = true;
            SubmitButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowStatus(string message, bool isError, bool isProgress = false)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusText.Text = message;

        if (isProgress)
        {
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x4A, 0x28));
            StatusIcon.Symbol = FluentIcons.Common.Symbol.ArrowSync;
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0xC9, 0x9A));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0xC9, 0x9A));
        }
        else if (isError)
        {
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x1D, 0x1D));
            StatusIcon.Symbol = FluentIcons.Common.Symbol.ErrorCircle;
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x8B, 0x8B));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0x8B, 0x8B));
        }
        else
        {
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x4A, 0x28));
            StatusIcon.Symbol = FluentIcons.Common.Symbol.CheckmarkCircle;
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0xC9, 0x9A));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0xC9, 0x9A));
        }
    }

    private static async Task<(bool Success, string Output, string Error)> CreateGitHubIssueAsync(string title, string body)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"issue create --repo {GitHubOwner}/{GitHubRepo} --title \"{EscapeArg(title)}\" --body \"{EscapeArg(body)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "", "Failed to start gh process. Is GitHub CLI installed?");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Wait with timeout (30 seconds)
            var completed = await Task.WhenAny(
                process.WaitForExitAsync(),
                Task.Delay(TimeSpan.FromSeconds(30)));

            if (!process.HasExited)
            {
                process.Kill();
                return (false, "", "Command timed out after 30 seconds.");
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                // Check for common errors
                if (error.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("authentication", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "", "Not logged in to GitHub CLI. Run 'gh auth login' in a terminal first.");
                }

                return (false, "", string.IsNullOrEmpty(error) ? $"gh exited with code {process.ExitCode}" : error);
            }

            return (true, output, "");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, "", "GitHub CLI (gh) not found. Please install it from https://cli.github.com/");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static string EscapeArg(string arg)
    {
        // Escape quotes and backslashes for command line
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string? ExtractIssueUrl(string output)
    {
        // The gh CLI typically outputs the issue URL on success
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains("/issues/"))
            {
                return trimmed;
            }
        }

        return null;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening browser
        }
    }
}
