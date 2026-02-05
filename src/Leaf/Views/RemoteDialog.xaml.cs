using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Leaf.Views;

/// <summary>
/// Dialog for adding or editing a remote repository.
/// </summary>
public partial class RemoteDialog : Window
{
    private readonly HashSet<string> _existingRemoteNames;
    private readonly bool _isEditing;
    private readonly string? _originalName;

    /// <summary>
    /// The remote name entered by the user.
    /// </summary>
    public string RemoteName => RemoteNameTextBox.Text.Trim();

    /// <summary>
    /// The fetch URL entered by the user.
    /// </summary>
    public string FetchUrl => FetchUrlTextBox.Text.Trim();

    /// <summary>
    /// The push URL entered by the user (null if same as fetch URL).
    /// </summary>
    public string? PushUrl => UseSeparatePushUrlCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(PushUrlTextBox.Text)
        ? PushUrlTextBox.Text.Trim()
        : null;

    /// <summary>
    /// Creates a new RemoteDialog for adding a remote.
    /// </summary>
    /// <param name="existingRemoteNames">Names of existing remotes (for duplicate checking)</param>
    public RemoteDialog(IEnumerable<string> existingRemoteNames)
    {
        InitializeComponent();
        _existingRemoteNames = new HashSet<string>(existingRemoteNames, StringComparer.OrdinalIgnoreCase);
        _isEditing = false;
        _originalName = null;
    }

    /// <summary>
    /// Creates a new RemoteDialog for editing an existing remote.
    /// </summary>
    /// <param name="existingRemoteNames">Names of existing remotes</param>
    /// <param name="currentName">Current name of the remote being edited</param>
    /// <param name="currentFetchUrl">Current fetch URL</param>
    /// <param name="currentPushUrl">Current push URL (null if same as fetch)</param>
    public RemoteDialog(IEnumerable<string> existingRemoteNames, string currentName, string currentFetchUrl, string? currentPushUrl)
        : this(existingRemoteNames)
    {
        _isEditing = true;
        _originalName = currentName;

        HeaderText.Text = "Edit Remote";
        SubheaderText.Text = "Modify remote repository settings";
        Title = "Edit Remote";

        RemoteNameTextBox.Text = currentName;
        FetchUrlTextBox.Text = currentFetchUrl;

        if (!string.IsNullOrEmpty(currentPushUrl) && currentPushUrl != currentFetchUrl)
        {
            UseSeparatePushUrlCheckBox.IsChecked = true;
            PushUrlTextBox.Text = currentPushUrl;
            PushUrlSection.Visibility = Visibility.Visible;
        }
    }

    private void RemoteNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateInput();
    }

    private void FetchUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateInput();
    }

    private void PushUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateInput();
    }

    private void UseSeparatePushUrlCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        PushUrlSection.Visibility = UseSeparatePushUrlCheckBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        ValidateInput();
    }

    private void ValidateInput()
    {
        var remoteName = RemoteNameTextBox.Text.Trim();
        var fetchUrl = FetchUrlTextBox.Text.Trim();
        var pushUrl = PushUrlTextBox.Text.Trim();

        // Validate remote name
        if (string.IsNullOrWhiteSpace(remoteName))
        {
            SaveButton.IsEnabled = false;
            ValidationErrorBorder.Visibility = Visibility.Collapsed;
            return;
        }

        // Check for invalid characters in remote name
        if (!IsValidRemoteName(remoteName))
        {
            ShowValidationError("Remote name can only contain letters, numbers, hyphens, and underscores.");
            return;
        }

        // Check for duplicate remote name (skip if editing and name unchanged)
        var isNameUnchanged = _isEditing && string.Equals(remoteName, _originalName, StringComparison.OrdinalIgnoreCase);
        if (!isNameUnchanged && _existingRemoteNames.Contains(remoteName))
        {
            ShowValidationError($"A remote named '{remoteName}' already exists.");
            return;
        }

        // Validate fetch URL
        if (string.IsNullOrWhiteSpace(fetchUrl))
        {
            SaveButton.IsEnabled = false;
            ValidationErrorBorder.Visibility = Visibility.Collapsed;
            return;
        }

        if (!IsValidGitUrl(fetchUrl))
        {
            ShowValidationError("Invalid fetch URL. Use HTTPS (https://...) or SSH (git@...) format.");
            return;
        }

        // Validate push URL if using separate
        if (UseSeparatePushUrlCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(pushUrl))
        {
            if (!IsValidGitUrl(pushUrl))
            {
                ShowValidationError("Invalid push URL. Use HTTPS (https://...) or SSH (git@...) format.");
                return;
            }
        }

        // All valid
        ValidationErrorBorder.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = true;
    }

    private void ShowValidationError(string message)
    {
        ValidationErrorText.Text = message;
        ValidationErrorBorder.Visibility = Visibility.Visible;
        SaveButton.IsEnabled = false;
    }

    private static bool IsValidRemoteName(string name)
    {
        // Remote names: letters, numbers, hyphens, underscores only
        // Cannot start with hyphen
        return Regex.IsMatch(name, @"^[a-zA-Z0-9_][a-zA-Z0-9_\-]*$");
    }

    private static bool IsValidGitUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // HTTPS URL
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }

        // SSH URL (git@host:path format)
        if (url.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            return url.Contains(':') && url.Length > 10;
        }

        // SSH URL (ssh://git@host/path format)
        if (url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }

        return false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
