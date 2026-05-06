using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Leaf.Models;
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
        VersionText.Text = FormatVersion();
        CopyrightText.Text = $"© {DateTime.Now.Year} Tim Kostka";
    }

    /// <summary>
    /// CI release builds embed a date-based version
    /// (<c>yyyy.MM.dd.minutesSinceMidnight</c>) via <c>/p:Version=...</c>.
    /// Local <c>dotnet build</c> / <c>dotnet run</c> don't pass that override,
    /// so debug builds carry the literal csproj default of 1.0.0.0 — which
    /// reads as "the released version is v1.0.0" if surfaced verbatim. Detect
    /// that case and label it as a development build, with the assembly's
    /// last-write timestamp for grounding (the closest stand-in for "when
    /// was this binary actually built" without baking a build date into the
    /// assembly proper).
    /// </summary>
    private static string FormatVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        if (version == null || (version.Major == 1 && version.Minor == 0 && version.Build == 0))
        {
            var when = TryGetAssemblyBuildTime(assembly);
            return when is { } ts
                ? $"Development build · {ts:yyyy-MM-dd HH:mm}"
                : "Development build";
        }

        // Real releases use yyyy.M.d.minutes — show all four parts so
        // collaborators reporting an issue can pin down the exact build.
        return version.Revision > 0
            ? $"v{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private static DateTime? TryGetAssemblyBuildTime(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location))
                return null;
            return File.GetLastWriteTime(location);
        }
        catch
        {
            return null;
        }
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
            FluentMessageBox.Show($"Could not open link: {ex.Message}", "Open URL",
                MessageBoxButton.OK, FluentMessageBoxIcon.Warning);
        }
    }
}
