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
    /// §5.8 verification status for the tag's GPG/SSH signature, parsed
    /// from <c>git for-each-ref --format=%(signature:grade)</c>. Defaults
    /// to <see cref="CommitSignatureStatus.None"/> for unsigned tags and
    /// for tags where signature parsing was skipped (test fixtures, tags
    /// constructed by client code without a git enrich pass).
    /// </summary>
    public CommitSignatureStatus SignatureStatus { get; set; } = CommitSignatureStatus.None;

    /// <summary>The signer's name from <c>%(signature:signer)</c>. Empty for unsigned tags.</summary>
    public string SignerName { get; set; } = string.Empty;

    /// <summary>The signer's email parsed out of <c>%(signature:signer)</c>'s "Name &lt;email&gt;" form. Empty when not available.</summary>
    public string SignerEmail { get; set; } = string.Empty;

    /// <summary>The signing key's fingerprint from <c>%(signature:key)</c>. Empty for unsigned tags.</summary>
    public string SignerKeyFingerprint { get; set; } = string.Empty;

    /// <summary>True when the tag has any signature regardless of trust.</summary>
    public bool IsSigned => SignatureStatus != CommitSignatureStatus.None;

    /// <summary>
    /// Single-line human-readable signature summary, mirrored from
    /// <see cref="CommitInfo.SignatureSummary"/> so commits and tags
    /// speak the same vocabulary in the UI.
    /// </summary>
    public string SignatureSummary => SignatureStatus switch
    {
        CommitSignatureStatus.Valid => string.IsNullOrWhiteSpace(SignerEmail)
            ? "Verified signature"
            : $"Verified signature by {SignerEmail}",
        CommitSignatureStatus.UnknownKey => "Couldn't verify — signing key isn't in the local keyring",
        CommitSignatureStatus.UntrustedKey => "Signed — key is in the keyring but not yet trusted",
        CommitSignatureStatus.Expired => "Signature has expired",
        CommitSignatureStatus.ExpiredKey => "Signing key has expired",
        CommitSignatureStatus.RevokedKey => "Signing key has been revoked",
        CommitSignatureStatus.Bad => "Bad signature — content may have been tampered with",
        // Unsigned: surface the tag KIND instead of "No signature" — for
        // a lightweight tag the user would otherwise see a tooltip about
        // the absence of a thing they didn't expect to be there.
        CommitSignatureStatus.None => IsAnnotated ? "Annotated tag" : "Lightweight tag",
        _ => "Unknown signature status",
    };

    /// <summary>
    /// Parses the tag name as a semantic version.
    /// Returns null if the tag name doesn't represent a valid version.
    /// </summary>
    public SemanticVersion? GetSemanticVersion() => SemanticVersion.TryParse(Name);
}
