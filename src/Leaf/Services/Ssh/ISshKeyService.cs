namespace Leaf.Services.Ssh;

/// <summary>
/// §5.13 — SSH key management. Centralises the read/write surface for
/// <c>~/.ssh</c>, <c>~/.ssh/config</c>, ssh-keygen, and ssh-agent so
/// settings UI and any future feature (clone-with-ssh, push diagnostics)
/// share one entry point.
///
/// <para>All methods are async because the underlying calls are either
/// process spawns (ssh-keygen, ssh-add, ssh) or filesystem reads that
/// can stall on slow disks. Errors that mean "tooling missing" return
/// empty results / a status object — they don't throw, because the
/// settings panel needs to render a "tooling unavailable" state instead
/// of crashing on first open. Genuine failures (corrupt config files,
/// process crashes) propagate so the UI can surface them.</para>
/// </summary>
public interface ISshKeyService
{
    /// <summary>
    /// List <c>~/.ssh/*.pub</c> files paired with their parsed metadata
    /// (algorithm, comment, fingerprint via <c>ssh-keygen -lf</c>). Files
    /// that don't parse as valid public keys are skipped silently — those
    /// are typically backup/swap files left by editors.
    /// </summary>
    Task<IReadOnlyList<SshPublicKey>> ListPublicKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Read the raw text of a public key file (one line, OpenSSH-format).</summary>
    Task<string> ReadPublicKeyTextAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run <c>ssh -T -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 git@host</c>
    /// to verify the agent / private key can authenticate to a known host.
    /// Most providers (GitHub, GitLab, Bitbucket) reject the shell session
    /// but emit a "Hi <user>" greeting on stderr — that greeting IS the
    /// success signal. Returns the raw output verbatim so the panel can
    /// show whatever ssh said.
    /// </summary>
    Task<SshConnectionTestResult> TestConnectionAsync(string sshTarget, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a new SSH key pair via <c>ssh-keygen</c>. Refuses to
    /// overwrite an existing key file — caller should validate the
    /// destination first.
    /// </summary>
    Task<SshKeyGenerationResult> GenerateKeyAsync(SshKeyGenerationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read and parse <c>~/.ssh/config</c> into a list of host entries.
    /// Returns an empty list when the file doesn't exist; the editor
    /// treats that the same as "no entries yet".
    /// </summary>
    Task<IReadOnlyList<SshConfigEntry>> ReadSshConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Write the given entries back to <c>~/.ssh/config</c>, replacing
    /// the file's contents. Preserves the global / Host * leading section
    /// when the editor leaves it untouched. Creates <c>~/.ssh</c> with
    /// 700 perms (Windows-equivalent: owner-only ACL) when the directory
    /// is missing.
    /// </summary>
    Task WriteSshConfigAsync(IReadOnlyList<SshConfigEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the keys currently loaded into ssh-agent via
    /// <c>ssh-add -l -E sha256</c>. Returns an empty list when the agent
    /// isn't running OR when it's running but has no keys (the two states
    /// are distinguishable via the <see cref="SshAgentStatus"/> on the
    /// availability snapshot, not by inspecting the list).
    /// </summary>
    Task<IReadOnlyList<SshAgentKey>> ListAgentKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a private key to the running ssh-agent. The passphrase, if
    /// non-empty, is delivered via the SSH_ASKPASS protocol so the agent
    /// never reads from a console that doesn't exist in a WPF host.
    /// </summary>
    Task<SshAgentOperationResult> AddKeyToAgentAsync(string privateKeyPath, string? passphrase, CancellationToken cancellationToken = default);

    /// <summary>Remove a single key from the running ssh-agent (<c>ssh-add -d</c>).</summary>
    Task<SshAgentOperationResult> RemoveKeyFromAgentAsync(string privateKeyPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Probe the health of the SSH ecosystem (ssh-keygen present, ssh-add
    /// present, ssh-agent reachable). The settings panel shows a banner
    /// based on this status; downstream operations short-circuit to a
    /// helpful error rather than spawning a non-existent binary.
    /// </summary>
    Task<SshToolingAvailability> DetectToolingAsync(CancellationToken cancellationToken = default);
}
