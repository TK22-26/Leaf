using System.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// A reviewer candidate for pull request assignment.
/// Works for GitHub users/teams and Azure DevOps users/groups.
/// </summary>
public class ReviewerInfo : INotifyPropertyChanged
{
    /// <summary>
    /// Provider-specific identifier (login for GitHub, GUID for Azure DevOps).
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Secondary text (slug for teams, email for users).
    /// </summary>
    public string? SecondaryText { get; set; }

    /// <summary>
    /// Avatar URL.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Whether this is a user, team, or group.
    /// </summary>
    public ReviewerKind Kind { get; set; }

    /// <summary>
    /// Whether this reviewer is required (Azure DevOps).
    /// GitHub does not support required reviewers at the API level.
    /// </summary>
    private bool _isRequired;
    public bool IsRequired
    {
        get => _isRequired;
        set
        {
            if (_isRequired == value) return;
            _isRequired = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRequired)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Kind of reviewer entity.
/// </summary>
public enum ReviewerKind
{
    User,
    Team,
    Group
}
