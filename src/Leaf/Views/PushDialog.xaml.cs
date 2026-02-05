using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Leaf.Models;

namespace Leaf.Views;

/// <summary>
/// Dialog for selecting which remotes to push to.
/// </summary>
public partial class PushDialog : Window
{
    /// <summary>
    /// The branch name being pushed.
    /// </summary>
    public string BranchName { get; }

    /// <summary>
    /// Collection of remotes with selection state.
    /// </summary>
    public ObservableCollection<RemoteSelectionItem> Remotes { get; }

    /// <summary>
    /// Whether to push to all remotes.
    /// </summary>
    public bool PushToAll => PushToAllCheckBox.IsChecked == true;

    /// <summary>
    /// Gets the list of selected remote names.
    /// </summary>
    public IEnumerable<string> SelectedRemoteNames => PushToAll
        ? Remotes.Select(r => r.Name)
        : Remotes.Where(r => r.IsSelected).Select(r => r.Name);

    /// <summary>
    /// Creates a new PushDialog.
    /// </summary>
    /// <param name="branchName">The branch being pushed</param>
    /// <param name="remotes">List of available remotes</param>
    /// <param name="defaultRemoteName">Name of the default remote (will be pre-selected)</param>
    public PushDialog(string branchName, IEnumerable<RemoteInfo> remotes, string? defaultRemoteName = null)
    {
        InitializeComponent();

        BranchName = branchName;
        BranchNameText.Text = branchName;

        // Build remote selection items
        Remotes = new ObservableCollection<RemoteSelectionItem>(
            remotes.Select(r => new RemoteSelectionItem
            {
                Name = r.Name,
                Url = r.Url,
                IsSelected = string.Equals(r.Name, defaultRemoteName ?? "origin", StringComparison.OrdinalIgnoreCase),
                IsDefault = string.Equals(r.Name, defaultRemoteName ?? "origin", StringComparison.OrdinalIgnoreCase)
            }));

        RemotesList.ItemsSource = Remotes;
        UpdatePushButtonState();
    }

    private void RemoteCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePushButtonState();
    }

    private void PushToAllCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        // Disable individual selection when "push to all" is checked
        var isEnabled = PushToAllCheckBox.IsChecked != true;

        foreach (var remote in Remotes)
        {
            remote.IsEnabled = isEnabled;
        }

        UpdatePushButtonState();
    }

    private void UpdatePushButtonState()
    {
        var canPush = PushToAll || Remotes.Any(r => r.IsSelected);
        PushButton.IsEnabled = canPush;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Push_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}

/// <summary>
/// Represents a remote with selection state for the push dialog.
/// </summary>
public class RemoteSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isEnabled = true;

    /// <summary>
    /// Remote name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Remote URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Display URL (shortened for UI).
    /// </summary>
    public string DisplayUrl
    {
        get
        {
            if (string.IsNullOrEmpty(Url))
                return string.Empty;

            // Try to extract host from URL
            try
            {
                if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
                {
                    return uri.Host;
                }

                // Handle git@host:path format
                if (Url.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
                {
                    var colonIndex = Url.IndexOf(':');
                    if (colonIndex > 4)
                    {
                        return Url[4..colonIndex];
                    }
                }
            }
            catch
            {
                // Fall back to full URL
            }

            return Url.Length > 40 ? Url[..40] + "..." : Url;
        }
    }

    /// <summary>
    /// Whether this remote is selected for push.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    /// <summary>
    /// Whether selection is enabled (disabled when "push to all" is checked).
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }
    }

    /// <summary>
    /// Whether this is the default remote.
    /// </summary>
    public bool IsDefault { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
