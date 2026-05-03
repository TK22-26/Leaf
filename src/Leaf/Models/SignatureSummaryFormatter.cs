namespace Leaf.Models;

/// <summary>
/// Single source of truth for the human-readable text that summarises a
/// signature status. Three call sites used to maintain their own switch
/// statement (commit detail tooltip, tag detail tooltip, graph badge
/// tooltip) and the wording had already started to drift — this
/// formatter keeps them aligned.
///
/// <para>The signer's email is optional and only changes the
/// <see cref="CommitSignatureStatus.Valid"/> wording. Pass it through
/// when you have it; pass <c>null</c> when the caller is summarising in
/// a context where attribution doesn't fit (e.g. the small graph badge
/// tooltip).</para>
/// </summary>
internal static class SignatureSummaryFormatter
{
    /// <summary>
    /// Build the summary line for <paramref name="status"/>. Returns
    /// <c>"No signature"</c> for <see cref="CommitSignatureStatus.None"/>;
    /// callers that want a different wording for that case (e.g. a tag's
    /// kind label) should branch before calling.
    /// </summary>
    /// <summary>
    /// Strip the trailing PGP / SSH signature block from a signed-tag
    /// (or signed-commit) message body. Tags store the signature inline
    /// in the raw object, but only the message above the
    /// <c>-----BEGIN PGP SIGNATURE-----</c> marker is human-facing.
    /// Returns the input verbatim (just trimmed) when no block is present.
    /// </summary>
    public static string StripSignatureBlock(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var idx = raw.IndexOf("-----BEGIN PGP SIGNATURE-----", System.StringComparison.Ordinal);
        if (idx < 0) idx = raw.IndexOf("-----BEGIN SSH SIGNATURE-----", System.StringComparison.Ordinal);
        return idx > 0 ? raw[..idx].TrimEnd() : raw.TrimEnd();
    }

    public static string Format(CommitSignatureStatus status, string? signerEmail = null) => status switch
    {
        CommitSignatureStatus.Valid => string.IsNullOrWhiteSpace(signerEmail)
            ? "Verified signature"
            : $"Verified signature by {signerEmail}",
        // %G? code E — typically: signing key isn't in the local keyring,
        // so git can't verify the signature at all.
        CommitSignatureStatus.UnknownKey => "Couldn't verify — signing key isn't in the local keyring",
        // %G? code U — key IS in the keyring, but the web of trust hasn't
        // reached it (you haven't signed it yourself or trusted a path).
        CommitSignatureStatus.UntrustedKey => "Signed — key is in the keyring but not yet trusted",
        CommitSignatureStatus.Expired => "Signature has expired",
        CommitSignatureStatus.ExpiredKey => "Signing key has expired",
        CommitSignatureStatus.RevokedKey => "Signing key has been revoked",
        CommitSignatureStatus.Bad => "Bad signature — content may have been tampered with",
        CommitSignatureStatus.None => "No signature",
        _ => "Unknown signature status",
    };
}
