using System.Diagnostics;
using System.IO;
using System.Text;

namespace Leaf.Services.Signing;

/// <inheritdoc />
public sealed class SigningToolDetector : ISigningToolDetector
{
    private readonly object _lock = new();
    private SigningToolAvailability? _cachedAvailability;
    private IReadOnlyList<GpgSecretKey>? _cachedGpgKeys;
    private string? _cachedGpgPath;

    public async Task<SigningToolAvailability> DetectAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cachedAvailability is not null) return _cachedAvailability;
        }

        var gpgPath = await ResolveToolPathAsync("gpg", "gpg.exe", cancellationToken).ConfigureAwait(false);
        var sshKeygenPath = await ResolveToolPathAsync("ssh-keygen", "ssh-keygen.exe", cancellationToken).ConfigureAwait(false);

        // Prove gpg is real by reading its --version (cheap, also gives us
        // a value to display). ssh-keygen has no version flag; existence
        // alone is the signal — there's nothing further to extract.
        string? gpgVersion = null;
        if (gpgPath is not null)
        {
            var (ok, output) = await RunCapturingAsync(gpgPath, ["--version"], cancellationToken).ConfigureAwait(false);
            if (ok)
                gpgVersion = output.Split('\n').FirstOrDefault()?.Trim();
        }

        var availability = new SigningToolAvailability(
            GpgAvailable: gpgPath is not null,
            GpgVersion: gpgVersion,
            SshAvailable: sshKeygenPath is not null,
            // ssh-keygen has no --version; "OpenSSH" is the user-meaningful
            // label and the only useful version string would require parsing
            // ssh-keygen's help output, which isn't worth the regex.
            SshVersion: sshKeygenPath is not null ? "OpenSSH ssh-keygen" : null);

        lock (_lock)
        {
            _cachedAvailability = availability;
            _cachedGpgPath = gpgPath;
        }
        return availability;
    }

    public async Task<IReadOnlyList<GpgSecretKey>> ListGpgSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cachedGpgKeys is not null) return _cachedGpgKeys;
        }

        await DetectAsync(cancellationToken).ConfigureAwait(false);

        string? gpgPath;
        lock (_lock) gpgPath = _cachedGpgPath;
        if (gpgPath is null)
        {
            lock (_lock) _cachedGpgKeys = [];
            return [];
        }

        var (success, output) = await RunCapturingAsync(
            gpgPath,
            ["--list-secret-keys", "--keyid-format", "LONG", "--with-colons"],
            cancellationToken).ConfigureAwait(false);
        if (!success)
        {
            lock (_lock) _cachedGpgKeys = [];
            return [];
        }

        var keys = ParseGpgColonOutput(output);
        lock (_lock) _cachedGpgKeys = keys;
        return keys;
    }

    /// <summary>
    /// Locate a signing tool. Tries plain PATH first (Gpg4win users, or
    /// anyone who's added the binary to PATH explicitly). Falls back to
    /// the Git for Windows install root via <c>git --exec-path</c>, since
    /// Git ships its own gpg / ssh-keygen at a fixed relative path from
    /// there but doesn't add that directory to the user's PATH.
    /// </summary>
    /// <returns>Resolved absolute path or just the command name (when PATH lookup works), or null when truly missing.</returns>
    private static async Task<string?> ResolveToolPathAsync(string command, string windowsExe, CancellationToken cancellationToken)
    {
        // 1. PATH lookup. The cheapest probe — if it works, we're done.
        if (await CanSpawnAsync(command, cancellationToken).ConfigureAwait(false))
            return command;

        // 2. Git for Windows bundled binary. This is the case the audit
        //    plan §5.8 expects: most Windows users run Git for Windows,
        //    its installer puts git.exe on PATH but NOT the bundled
        //    usr/bin (which has gpg, ssh-keygen, ssh, etc.).
        var gitInstallDir = await FindGitInstallDirAsync(cancellationToken).ConfigureAwait(false);
        if (gitInstallDir is not null)
        {
            var bundled = Path.Combine(gitInstallDir, "usr", "bin", windowsExe);
            if (File.Exists(bundled)) return bundled;
        }

        return null;
    }

    /// <summary>
    /// Discover the Git for Windows install root by asking git itself
    /// for its libexec path and walking up. <c>git --exec-path</c> on
    /// Windows returns something like
    /// <c>C:/Program Files/Git/mingw64/libexec/git-core</c>; the install
    /// root is three levels above that. Returns null if git can't be
    /// spawned or the layout doesn't match — we never assume a path
    /// that doesn't actually contain the bundled binaries.
    /// </summary>
    private static async Task<string?> FindGitInstallDirAsync(CancellationToken cancellationToken)
    {
        var (ok, output) = await RunCapturingAsync("git", ["--exec-path"], cancellationToken).ConfigureAwait(false);
        if (!ok) return null;
        var execPath = output.Trim();
        if (string.IsNullOrEmpty(execPath) || !Directory.Exists(execPath)) return null;

        // libexec/git-core/.. → libexec
        // libexec/.. → mingw64 (or mingw32 on 32-bit installs)
        // mingw64/.. → install root
        var current = execPath;
        for (var i = 0; i < 3; i++)
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent)) return null;
            current = parent;
        }
        // Sanity check: a Git for Windows install root has both `cmd` and
        // `usr` directories. Without this a quirky non-Git binary returning
        // some random path from --exec-path could trick us.
        if (Directory.Exists(Path.Combine(current, "cmd"))
            && Directory.Exists(Path.Combine(current, "usr")))
        {
            return current;
        }
        return null;
    }

    /// <summary>
    /// Cheap "does this binary spawn?" probe via the shared
    /// <see cref="ProcessHelper"/>. Uses <c>--version</c> as the cheapest
    /// args that exit immediately for both gpg and ssh-keygen.
    /// </summary>
    private static Task<bool> CanSpawnAsync(string exe, CancellationToken cancellationToken) =>
        ProcessHelper.CanSpawnAsync(exe, ["--version"], cancellationToken);

    /// <summary>
    /// Parse the colon-separated machine-readable format from
    /// <c>gpg --with-colons</c>. Format reference:
    /// <c>doc/DETAILS</c> in the GnuPG source. We care about three
    /// record types: <c>sec</c> (secret key), <c>fpr</c> (fingerprint
    /// follows the secret key), and <c>uid</c> (user id of the key).
    /// </summary>
    internal static List<GpgSecretKey> ParseGpgColonOutput(string output)
    {
        var keys = new List<GpgSecretKey>();
        if (string.IsNullOrWhiteSpace(output)) return keys;

        string? currentLongKeyId = null;
        string? currentFingerprint = null;
        string? currentUid = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ');
            if (line.Length == 0) continue;

            var parts = line.Split(':');
            if (parts.Length < 1) continue;

            switch (parts[0])
            {
                case "sec":
                    // Flush previous record when we hit a new sec line.
                    if (currentLongKeyId is not null)
                    {
                        keys.Add(new GpgSecretKey(
                            currentLongKeyId,
                            currentUid ?? "(no user id)",
                            currentFingerprint ?? string.Empty));
                    }
                    currentLongKeyId = parts.Length > 4 ? parts[4] : null;
                    currentFingerprint = null;
                    currentUid = null;
                    break;
                case "fpr" when currentLongKeyId is not null && currentFingerprint is null:
                    // First fpr after sec is the primary key fingerprint.
                    if (parts.Length > 9) currentFingerprint = parts[9];
                    break;
                case "uid" when currentLongKeyId is not null && currentUid is null:
                    // First uid after sec is the primary uid.
                    if (parts.Length > 9)
                    {
                        // gpg escapes colons in the uid as \x3a — undo so
                        // emails like "user@host:port" round-trip cleanly.
                        currentUid = parts[9].Replace("\\x3a", ":");
                    }
                    break;
            }
        }

        // Trailing record.
        if (currentLongKeyId is not null)
        {
            keys.Add(new GpgSecretKey(
                currentLongKeyId,
                currentUid ?? "(no user id)",
                currentFingerprint ?? string.Empty));
        }
        return keys;
    }

    /// <summary>
    /// Run <paramref name="exe"/> via <see cref="ProcessHelper"/> and
    /// return the (exit-zero, combined-output) tuple this class's
    /// callers expect. Stdout-only would be cleaner for gpg parsing,
    /// but combined output is harmless because gpg writes its keylist
    /// to stdout and only diagnostics to stderr — and the parser
    /// ignores anything outside its expected colon-record shape.
    /// </summary>
    private static async Task<(bool Success, string Output)> RunCapturingAsync(string exe, string[] args, CancellationToken cancellationToken)
    {
        var result = await ProcessHelper.RunAsync(exe, args, cancellationToken: cancellationToken).ConfigureAwait(false);
        return (result.Spawned && result.ExitCode == 0, result.Output);
    }
}
