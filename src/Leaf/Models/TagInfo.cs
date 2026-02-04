using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Represents information about a Git tag.
/// </summary>
public partial class TagInfo : ObservableObject
{
    /// <summary>
    /// Whether this tag is selected in the UI.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Tags are leaf items and don't expand (silences TreeView binding warnings).
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// The tag name (e.g., "v1.0.0").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The SHA of the commit the tag points to.
    /// </summary>
    public string TargetSha { get; set; } = string.Empty;

    /// <summary>
    /// The tag message (for annotated tags).
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Whether this is an annotated tag (vs lightweight).
    /// </summary>
    public bool IsAnnotated { get; set; }

    /// <summary>
    /// The tagger name (for annotated tags).
    /// </summary>
    public string? TaggerName { get; set; }

    /// <summary>
    /// The tagger email (for annotated tags).
    /// </summary>
    public string? TaggerEmail { get; set; }

    /// <summary>
    /// When the tag was created (for annotated tags).
    /// </summary>
    public DateTimeOffset? TaggedAt { get; set; }

    /// <summary>
    /// Parses the tag name as a semantic version.
    /// Returns null if the tag name doesn't represent a valid version.
    /// </summary>
    public SemanticVersion? GetSemanticVersion() => SemanticVersion.TryParse(Name);
}
