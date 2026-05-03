using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// POCO representing a Git commit, mapped from LibGit2Sharp.Commit.
/// Thread-safe and serializable - no LibGit2Sharp types exposed.
/// </summary>
public partial class CommitInfo : ObservableObject
{
    public string Sha { get; set; } = string.Empty;

    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    public string Message { get; set; } = string.Empty;

    public string MessageShort { get; set; } = string.Empty;

    /// <summary>
    /// The commit description (full body after the first line, if any).
    /// </summary>
    public string Description
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return string.Empty;

            // Find the first newline
            var firstNewline = Message.IndexOf('\n');
            if (firstNewline < 0)
                return string.Empty;

            // Skip the first line and any blank lines after it, return full body
            return Message[(firstNewline + 1)..].TrimStart('\r', '\n').Trim();
        }
    }

    public string Author { get; set; } = string.Empty;

    public string AuthorEmail { get; set; } = string.Empty;

    public string AvatarKey
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AuthorEmail))
            {
                return AuthorEmail.Trim();
            }

            if (!string.IsNullOrWhiteSpace(Author))
            {
                return Author.Trim();
            }

            return Sha;
        }
    }

    public DateTimeOffset Date { get; set; }

    public List<string> ParentShas { get; set; } = [];

    public bool IsMerge => ParentShas.Count > 1;

    /// <summary>
    /// Branch names that point to this commit (for branch labels).
    /// </summary>
    public List<string> BranchNames { get; set; } = [];

    /// <summary>
    /// Branch labels with local/remote info for display on graph.
    /// </summary>
    public List<BranchLabel> BranchLabels { get; set; } = [];

    /// <summary>
    /// Tag names that point to this commit.
    /// </summary>
    public List<string> TagNames { get; set; } = [];

    /// <summary>
    /// True if this commit is the current HEAD.
    /// </summary>
    public bool IsHead { get; set; }

    /// <summary>
    /// Friendly date string for display.
    /// </summary>
    public string DateDisplay
    {
        get
        {
            var now = DateTimeOffset.Now;
            var diff = now - Date;

            if (diff.TotalMinutes < 1)
                return "Just now";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}d ago";
            if (Date.Year == now.Year)
                return Date.ToString("MMM d");
            return Date.ToString("MMM d, yyyy");
        }
    }

    /// <summary>
    /// True if this entry represents a stash (pseudo-commit).
    /// </summary>
    public bool IsStash { get; set; }

    /// <summary>
    /// The stash index (0 = most recent) when IsStash is true, otherwise -1.
    /// </summary>
    public int StashIndex { get; set; } = -1;

    /// <summary>
    /// True if this commit is currently selected.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHighlighted))]
    private bool _isSelected;

    /// <summary>
    /// True if this commit matches the current search (when search is active).
    /// False when no search is active or doesn't match.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHighlighted))]
    private bool _isSearchHighlighted;

    /// <summary>
    /// True if this commit should be dimmed (search active but doesn't match).
    /// </summary>
    [ObservableProperty]
    private bool _isDimmed;

    /// <summary>
    /// True if this commit should have highlighted background (selected OR search match).
    /// </summary>
    public bool IsHighlighted => IsSelected || IsSearchHighlighted;

    public record CoAuthorInfo(string Name, string Email)
    {
        public string AvatarKey => !string.IsNullOrWhiteSpace(Email) ? Email.Trim() : Name.Trim();
    }

    private static readonly Regex CoAuthorRegex = new(
        @"Co-Authored-By:\s*(.+?)\s*<(.+?)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public List<CoAuthorInfo> CoAuthors
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return [];

            var results = new List<CoAuthorInfo>();
            foreach (Match match in CoAuthorRegex.Matches(Message))
            {
                results.Add(new CoAuthorInfo(match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()));
            }
            return results;
        }
    }

    /// <summary>
    /// §5.8 — verification status for the commit's GPG/SSH signature, as
    /// reported by <c>git log %G?</c>. Defaults to
    /// <see cref="CommitSignatureStatus.None"/> for unsigned commits and
    /// for commits where signature parsing was skipped (test fixtures,
    /// commits constructed by client code without a git enrich pass).
    /// </summary>
    public CommitSignatureStatus SignatureStatus { get; set; } = CommitSignatureStatus.None;

    /// <summary>The signer's name (<c>%GS</c>). Empty for unsigned commits.</summary>
    public string SignerName { get; set; } = string.Empty;

    /// <summary>The signer's email (<c>%GE</c>). Empty for unsigned commits.</summary>
    public string SignerEmail { get; set; } = string.Empty;

    /// <summary>
    /// The signing key's fingerprint (<c>%GF</c>). For GPG this is the
    /// 40-char hex fingerprint; for SSH it's the algorithm-prefixed
    /// fingerprint string. Empty for unsigned commits.
    /// </summary>
    public string SignerKeyFingerprint { get; set; } = string.Empty;

    /// <summary>True when the commit has any signature regardless of trust.</summary>
    public bool IsSigned => SignatureStatus != CommitSignatureStatus.None;

    /// <summary>
    /// Single-line human-readable summary of the signature status, used by
    /// the graph badge tooltip and the commit detail view. Delegates to
    /// <see cref="SignatureSummaryFormatter"/> so commit / tag / tooltip
    /// surfaces all read the same string for the same status.
    /// </summary>
    public string SignatureSummary => SignatureSummaryFormatter.Format(SignatureStatus, SignerEmail);
}

/// <summary>
/// §5.8 mapping of <c>git log %G?</c> trust codes to typed values. The
/// upstream codes (<c>G/U/B/X/Y/R/E/N</c>) collapse a fair amount of
/// nuance into one byte; this enum keeps each meaningful state distinct
/// so the UI can pick a colour and message per status.
/// <list type="bullet">
/// <item><c>G</c> → <see cref="Valid"/> — good signature with a fully-trusted key.</item>
/// <item><c>U</c> → <see cref="UntrustedKey"/> — good signature, key is in the keyring but the web of trust hasn't reached it.</item>
/// <item><c>X</c> → <see cref="Expired"/> — good signature that has since expired.</item>
/// <item><c>Y</c> → <see cref="ExpiredKey"/> — good signature made with an expired key.</item>
/// <item><c>R</c> → <see cref="RevokedKey"/> — good signature made with a revoked key.</item>
/// <item><c>B</c> → <see cref="Bad"/> — bad (forged or corrupt) signature.</item>
/// <item><c>E</c> → <see cref="UnknownKey"/> — signature couldn't be checked, typically because the key isn't in the local keyring.</item>
/// <item><c>N</c> → <see cref="None"/> — no signature.</item>
/// </list>
/// </summary>
public enum CommitSignatureStatus
{
    None = 0,
    Valid = 1,
    UnknownKey = 2,
    UntrustedKey = 3,
    Expired = 4,
    ExpiredKey = 5,
    RevokedKey = 6,
    Bad = 7,
}
