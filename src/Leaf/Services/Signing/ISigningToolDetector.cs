namespace Leaf.Services.Signing;

/// <summary>
/// §5.8 — probe whether GPG / SSH signing tooling is available on the
/// host. Single instance per app lifetime; results are cached for the
/// process's life because installing tools while Leaf is running is
/// rare enough that a re-probe per call would be wasteful.
/// </summary>
public interface ISigningToolDetector
{
    /// <summary>
    /// Detect installed signing tools. Returns a snapshot — callers
    /// don't have to worry about the result mutating mid-render.
    /// </summary>
    Task<SigningToolAvailability> DetectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// List the GPG secret keys available for signing, parsed from
    /// <c>gpg --list-secret-keys --keyid-format LONG --with-colons</c>.
    /// Returns an empty list when GPG isn't installed or has no secret
    /// keys.
    /// </summary>
    Task<IReadOnlyList<GpgSecretKey>> ListGpgSecretKeysAsync(CancellationToken cancellationToken = default);
}

/// <summary>Snapshot of which signing tools are usable on the host.</summary>
public sealed record SigningToolAvailability(
    bool GpgAvailable,
    string? GpgVersion,
    bool SshAvailable,
    string? SshVersion);

/// <summary>
/// One row from <c>gpg --list-secret-keys --with-colons</c>. Long key
/// id and primary uid are the two fields users recognise — the key id
/// is what goes into <c>user.signingkey</c> in git config, the uid is
/// what we show in the picker.
/// </summary>
public sealed record GpgSecretKey(string LongKeyId, string PrimaryUid, string Fingerprint);
