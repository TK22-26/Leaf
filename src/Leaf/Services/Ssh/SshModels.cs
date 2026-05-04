namespace Leaf.Services.Ssh;

/// <summary>
/// One <c>~/.ssh/*.pub</c> file with its parsed metadata. The
/// <see cref="PrivateKeyPath"/> is the same path with the <c>.pub</c>
/// suffix stripped — convention, not a guarantee that the private key
/// exists, since a user may keep only the public half on a workstation.
/// </summary>
public sealed record SshPublicKey(
    string PublicKeyPath,
    string PrivateKeyPath,
    SshKeyAlgorithm Algorithm,
    string Comment,
    string Fingerprint,
    int? KeyBits)
{
    /// <summary>Filename without extension — what users recognise the key by.</summary>
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(PublicKeyPath);

    /// <summary>True when the matching private key file is on disk.</summary>
    public bool HasPrivateKey => System.IO.File.Exists(PrivateKeyPath);
}

/// <summary>
/// SSH key algorithm. Mirrors the algorithms ssh-keygen accepts; values
/// are picked so <c>.ToString().ToLowerInvariant()</c> matches the
/// <c>-t</c> flag passed to ssh-keygen ("ed25519", "rsa", "ecdsa", "dsa").
/// </summary>
public enum SshKeyAlgorithm
{
    Unknown = 0,
    Ed25519,
    Rsa,
    Ecdsa,
    Dsa,
}

/// <summary>
/// Request body for <see cref="ISshKeyService.GenerateKeyAsync"/>.
/// <para><see cref="Bits"/> is only honoured for RSA / DSA / ECDSA — Ed25519
/// has a fixed 256-bit key size and ssh-keygen rejects a <c>-b</c> flag
/// for it.</para>
/// </summary>
public sealed record SshKeyGenerationRequest(
    SshKeyAlgorithm Algorithm,
    int? Bits,
    string Comment,
    string OutputPath,
    string? Passphrase);

/// <summary>
/// Outcome of <see cref="ISshKeyService.GenerateKeyAsync"/>. <see cref="Success"/>
/// false means ssh-keygen reported a failure; the message is intended
/// to be shown to the user verbatim because ssh-keygen's wording is
/// already user-facing ("Saving key … failed: file exists").
/// </summary>
public sealed record SshKeyGenerationResult(
    bool Success,
    string? Message,
    SshPublicKey? GeneratedKey);

/// <summary>
/// Result of <see cref="ISshKeyService.TestConnectionAsync"/>. Most SSH
/// hosts reject interactive shell access but greet authenticated
/// sessions on stderr — the greeting (e.g. "Hi user! You've successfully
/// authenticated…") IS the success signal. We treat a non-zero exit
/// code combined with an "authenticated" / "Hi" message as success;
/// anything else is a genuine failure.
/// </summary>
public sealed record SshConnectionTestResult(
    bool Authenticated,
    string Output,
    string? Identity);

/// <summary>
/// Snapshot of which SSH tools are usable on the host. The settings
/// panel disables unsupported actions based on this — e.g. the
/// "Generate key" button is disabled when <see cref="HasSshKeygen"/> is
/// false rather than throwing on click.
/// </summary>
public sealed record SshToolingAvailability(
    bool HasSsh,
    bool HasSshKeygen,
    bool HasSshAdd,
    SshAgentStatus AgentStatus);

/// <summary>
/// Tri-state for the running ssh-agent. <see cref="NotRunning"/> and
/// <see cref="Unavailable"/> are different intentionally: an agent that
/// isn't running can be started; an agent that's unreachable (Pageant,
/// non-OpenSSH agent) needs a different remediation.
/// </summary>
public enum SshAgentStatus
{
    /// <summary>OpenSSH ssh-agent is running and ssh-add can talk to it.</summary>
    Running,
    /// <summary>ssh-add reports the agent isn't running on this user account.</summary>
    NotRunning,
    /// <summary>ssh-add itself is missing — agent integration is unavailable.</summary>
    Unavailable,
}

/// <summary>
/// One key currently loaded into ssh-agent. The bit count is only
/// reliable for RSA / DSA — Ed25519 / Ecdsa keys produce algorithm-
/// dependent values that we surface verbatim.
/// </summary>
public sealed record SshAgentKey(
    int? Bits,
    string Fingerprint,
    string Comment,
    SshKeyAlgorithm Algorithm);

/// <summary>
/// Outcome of an ssh-agent add/remove. The agent's stderr text is
/// preserved so the panel can surface "Bad passphrase" or "Could not
/// open a connection to your authentication agent" verbatim.
/// </summary>
public sealed record SshAgentOperationResult(bool Success, string Message);

/// <summary>
/// One <c>Host</c> stanza in <c>~/.ssh/config</c>. We model the most
/// common keys as first-class properties; everything else lives in
/// <see cref="ExtraOptions"/> and round-trips through the writer
/// untouched. <c>HostPattern</c> retains the original <c>Host </c> value
/// (which may itself be a list of patterns separated by whitespace, e.g.
/// <c>Host github.com gitlab.com</c>); we don't try to normalise it.
/// </summary>
public sealed record SshConfigEntry
{
    public string HostPattern { get; init; } = string.Empty;
    public string? HostName { get; init; }
    public string? User { get; init; }
    public int? Port { get; init; }
    public string? IdentityFile { get; init; }
    public string? ProxyCommand { get; init; }
    public IReadOnlyList<SshConfigOption> ExtraOptions { get; init; } = [];

    /// <summary>True when no whitelisted property is set — the editor uses this to flag empty hosts.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(HostName)
        && string.IsNullOrWhiteSpace(User)
        && Port is null
        && string.IsNullOrWhiteSpace(IdentityFile)
        && string.IsNullOrWhiteSpace(ProxyCommand)
        && ExtraOptions.Count == 0;
}

/// <summary>
/// One key/value pair from a <c>Host</c> stanza that we don't promote
/// to a property. Preserved verbatim by the writer so editing one host
/// doesn't lose obscure options like <c>ServerAliveInterval</c>.
/// </summary>
public sealed record SshConfigOption(string Key, string Value);
