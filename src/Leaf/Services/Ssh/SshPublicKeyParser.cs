namespace Leaf.Services.Ssh;

/// <summary>
/// Pure-text helpers for OpenSSH public-key files. A <c>.pub</c> file is
/// a single line in the form <c>algorithm base64key [comment...]</c>;
/// the comment is everything after the first whitespace following the
/// key blob and is allowed to contain spaces.
///
/// <para>Keeping this separate from <see cref="ISshKeyService"/> means
/// fingerprint-by-ssh-keygen and parse-by-text can be tested
/// independently — and the parser doesn't take a dependency on a
/// process spawn.</para>
/// </summary>
internal static class SshPublicKeyParser
{
    /// <summary>
    /// Parse the algorithm + comment from a single-line public-key text.
    /// Returns false when the line doesn't match the OpenSSH shape — the
    /// listing layer treats that as "skip this file" rather than crash.
    /// The base64 key blob itself is intentionally not surfaced — its
    /// only consumer is ssh-keygen for the fingerprint, which reads the
    /// file directly.
    /// </summary>
    public static bool TryParse(string text, out SshKeyAlgorithm algorithm, out string comment)
    {
        algorithm = SshKeyAlgorithm.Unknown;
        comment = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // First non-comment, non-blank line wins. Editors sometimes leave
        // a trailing newline; we tolerate \r\n and Linux line endings.
        var line = text;
        var firstNewline = text.IndexOfAny(['\r', '\n']);
        if (firstNewline >= 0) line = text[..firstNewline];
        line = line.Trim();
        if (line.Length == 0 || line.StartsWith('#')) return false;

        var firstSpace = line.IndexOf(' ');
        if (firstSpace <= 0) return false;
        var algoToken = line[..firstSpace];

        algorithm = MapAlgorithm(algoToken);
        if (algorithm == SshKeyAlgorithm.Unknown) return false;

        var afterAlgo = line[(firstSpace + 1)..].TrimStart();
        var secondSpace = afterAlgo.IndexOf(' ');
        comment = secondSpace > 0 ? afterAlgo[(secondSpace + 1)..].Trim() : string.Empty;
        return true;
    }

    /// <summary>
    /// Map an OpenSSH key-type token (the first field of a <c>.pub</c>
    /// file) onto our enum. The list mirrors what ssh-keygen produces;
    /// older types like <c>ssh-dss</c> map to <see cref="SshKeyAlgorithm.Dsa"/>
    /// even though we don't generate them — listing them correctly lets
    /// users see legacy keys still on disk.
    /// </summary>
    public static SshKeyAlgorithm MapAlgorithm(string token) => token switch
    {
        "ssh-ed25519" or "ssh-ed25519-cert-v01@openssh.com" => SshKeyAlgorithm.Ed25519,
        "ssh-rsa" or "rsa-sha2-256" or "rsa-sha2-512" or "ssh-rsa-cert-v01@openssh.com" => SshKeyAlgorithm.Rsa,
        "ecdsa-sha2-nistp256" or "ecdsa-sha2-nistp384" or "ecdsa-sha2-nistp521"
            or "ecdsa-sha2-nistp256-cert-v01@openssh.com"
            or "ecdsa-sha2-nistp384-cert-v01@openssh.com"
            or "ecdsa-sha2-nistp521-cert-v01@openssh.com" => SshKeyAlgorithm.Ecdsa,
        "ssh-dss" or "ssh-dss-cert-v01@openssh.com" => SshKeyAlgorithm.Dsa,
        _ => SshKeyAlgorithm.Unknown,
    };

    /// <summary>
    /// Parse a single line of <c>ssh-keygen -lf KEY</c> output. Format:
    /// <c>BITS FINGERPRINT COMMENT (TYPE)</c> — three known fields plus
    /// an optional parenthesised type at the end. Returns false on lines
    /// ssh-keygen rejected (writes "key is not a public key" to stderr,
    /// stdout still emits something or nothing depending on version).
    /// </summary>
    public static bool TryParseFingerprintLine(string line, out int? bits, out string fingerprint, out string comment)
    {
        bits = null;
        fingerprint = string.Empty;
        comment = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;

        // ssh-keygen on Windows still emits ASCII output, so a simple
        // whitespace tokenise is safe.
        var trimmed = line.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0) return false;

        if (int.TryParse(trimmed[..firstSpace], out var parsedBits))
            bits = parsedBits;

        var rest = trimmed[(firstSpace + 1)..].TrimStart();
        var secondSpace = rest.IndexOf(' ');
        if (secondSpace < 0) return false;

        fingerprint = rest[..secondSpace];
        var afterFingerprint = rest[(secondSpace + 1)..].TrimStart();

        // The trailing parenthesised type is optional; strip it if present.
        var typeStart = afterFingerprint.LastIndexOf('(');
        if (typeStart > 0
            && afterFingerprint.EndsWith(")", StringComparison.Ordinal))
        {
            comment = afterFingerprint[..typeStart].TrimEnd();
        }
        else
        {
            comment = afterFingerprint;
        }

        return fingerprint.Length > 0;
    }
}
