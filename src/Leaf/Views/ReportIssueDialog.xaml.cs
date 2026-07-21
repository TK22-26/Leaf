using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.Views;

public partial class ReportIssueDialog : Window
{
    private const string GitHubOwner = "TK22-26";
    private const string GitHubRepo = "Leaf";

    /// <summary>
    /// Cancels an in-flight `gh issue create`. Non-null exactly while a
    /// submission is running; the Cancel button cancels the submission
    /// instead of closing the dialog during that window.
    /// </summary>
    private System.Threading.CancellationTokenSource? _submitCts;

    public ReportIssueDialog()
    {
        InitializeComponent();
        BodyTextBox.Text = GetDefaultBody();
    }

    /// <summary>
    /// Closing via X / Alt+F4 while a submission is in flight: abort the
    /// gh call so its continuation doesn't resume against a closed
    /// window (spurious owner/DialogResult errors, lost prompts) and no
    /// process lingers unobserved. Escape already routes through
    /// <see cref="CancelButton_Click"/>.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _submitCts?.Cancel();
        base.OnClosing(e);
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

        SetSubmitting(true);

        _submitCts = new System.Threading.CancellationTokenSource();
        try
        {
            var (success, output, error) = await CreateGitHubIssueAsync(title, body, _submitCts.Token);

            if (success)
            {
                ShowStatus("Issue created successfully!", isError: false);

                // Try to extract the issue URL from the output and open it
                var issueUrl = ExtractIssueUrl(output);
                if (!string.IsNullOrEmpty(issueUrl))
                {
                    // Suppressible: "I will NEVER want to view it in
                    // GitHub" is a remembered No; a remembered Yes
                    // auto-opens the browser (#36).
                    var result = FluentMessageBox.ShowSuppressible(
                        $"Issue created successfully!\n\nWould you like to open it in your browser?\n\n{issueUrl}",
                        "Issue Created",
                        suppressionKey: "reportIssue.openInBrowser",
                        MessageBoxButton.YesNo,
                        FluentMessageBoxIcon.Information,
                        owner: this);

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
                SetSubmitting(false);
            }
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Submission cancelled.", isError: true);
            SetSubmitting(false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", isError: true);
            SetSubmitting(false);

            // Also log — ShowStatus is a UI-only indicator.
            AsyncErrorHandler.Handle(ex, nameof(SubmitButton_Click), isUserAction: false);
        }
        finally
        {
            _submitCts?.Dispose();
            _submitCts = null;
        }
    }

    /// <summary>
    /// Toggle the submitting visuals: inputs disabled, Submit reads
    /// "Creating…", and the status strip shows the spinning sync icon.
    /// </summary>
    private void SetSubmitting(bool submitting)
    {
        TitleTextBox.IsEnabled = !submitting;
        BodyTextBox.IsEnabled = !submitting;
        OpenInBrowserButton.IsEnabled = !submitting;
        SubmitButton.IsEnabled = !submitting && !string.IsNullOrWhiteSpace(TitleTextBox.Text);
        SubmitButtonText.Text = submitting ? "Creating…" : "Submit Issue";
        if (submitting)
        {
            ShowStatus("Creating issue...", isError: false, isProgress: true);
        }
    }

    /// <summary>
    /// Maximum length for the pre-filled new-issue URL. Browsers and
    /// GitHub's server reject very long URLs; past this we fail loudly
    /// and ask the user to shorten the description rather than silently
    /// truncating what they wrote.
    /// </summary>
    private const int MaxIssueUrlLength = 8000;

    /// <summary>
    /// Open GitHub's new-issue form pre-filled with the current title
    /// and body. The web form supports drag-and-drop image attachments,
    /// which no CLI/API path offers (#34).
    /// </summary>
    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleTextBox.Text.Trim();
        var body = BodyTextBox.Text.Trim();

        var url = $"https://github.com/{GitHubOwner}/{GitHubRepo}/issues/new" +
                  $"?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        if (url.Length > MaxIssueUrlLength)
        {
            ShowStatus(
                $"The issue text is too long to pre-fill a browser form ({url.Length:N0} of {MaxIssueUrlLength:N0} characters). " +
                "Shorten the description, or submit in-app and add screenshots as a comment afterwards.",
                isError: true);
            return;
        }

        if (OpenUrl(url))
        {
            DialogResult = true;
        }
        else
        {
            ShowStatus("Could not open the browser — no handler for https URLs?", isError: true);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // While a submission is in flight, Cancel aborts the gh call and
        // returns to the editable form instead of closing the window
        // with an orphaned process behind it.
        if (_submitCts is { } cts)
        {
            cts.Cancel();
            return;
        }
        DialogResult = false;
    }

    private void ShowStatus(string message, bool isError, bool isProgress = false)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusText.Text = message;

        // Spin the sync icon while in progress; clear the transform on
        // any terminal state so success/error icons render upright.
        if (isProgress)
        {
            var rotate = new RotateTransform();
            StatusIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            StatusIcon.RenderTransform = rotate;
            rotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2))
                {
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                });
        }
        else
        {
            StatusIcon.RenderTransform = null;
        }

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

    private static async Task<(bool Success, string Output, string Error)> CreateGitHubIssueAsync(
        string title, string body, System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Same rule as every git spawn: never let a child inherit
                // this process's stdin; close the pipe right after start.
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // ArgumentList — no hand-rolled escaping; titles/bodies with
            // quotes, backslashes, or newlines pass through verbatim.
            psi.ArgumentList.Add("issue");
            psi.ArgumentList.Add("create");
            psi.ArgumentList.Add("--repo");
            psi.ArgumentList.Add($"{GitHubOwner}/{GitHubRepo}");
            psi.ArgumentList.Add("--title");
            psi.ArgumentList.Add(title);
            psi.ArgumentList.Add("--body");
            psi.ArgumentList.Add(body);

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "", "Failed to start gh process. Is GitHub CLI installed?");
            }

            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Wait for exit, bounded by the 30s timeout and the user's
            // Cancel button. Either trigger kills the process; only the
            // user's token surfaces as OperationCanceledException.
            using var timeoutCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                // Observe the reader tasks — the kill closes the pipes so
                // they complete promptly; leaving them unobserved lets the
                // `using var process` dispose the streams mid-read and
                // surface ObjectDisposedException as unobserved-task noise.
                try
                {
                    await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (Exception drainEx) when (drainEx is OperationCanceledException
                                             or TimeoutException
                                             or InvalidOperationException
                                             or System.IO.IOException
                                             or ObjectDisposedException
                                             or AggregateException)
                {
                    // Best-effort drain; the caller only needs the cancel/timeout outcome.
                }

                cancellationToken.ThrowIfCancellationRequested();
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
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

    private static bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                or System.IO.FileNotFoundException
                                or InvalidOperationException)
        {
            // No registered handler for URL scheme or shell execution blocked.
            Log.Info("ReportIssue", $"OpenUrl('{url}') failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
