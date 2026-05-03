using System.Diagnostics;
using System.Text;

namespace Leaf.Services.Signing;

/// <inheritdoc />
public sealed class SigningToolDetector : ISigningToolDetector
{
    private readonly object _lock = new();
    private SigningToolAvailability? _cachedAvailability;
    private IReadOnlyList<GpgSecretKey>? _cachedGpgKeys;

    public async Task<SigningToolAvailability> DetectAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cachedAvailability is not null) return _cachedAvailability;
        }

        var gpg = await ProbeAsync("gpg", "--version", cancellationToken).ConfigureAwait(false);
        var ssh = await ProbeAsync("ssh-keygen", "-V", cancellationToken).ConfigureAwait(false);

        // ssh-keygen -V is the closest "is this installed?" check that
        // doesn't require a key argument. Modern OpenSSH responds with a
        // usage error on stderr but exits non-zero — both behaviours
        // confirm the binary exists. We treat any output (stdout OR
        // stderr) as proof of installation.
        var availability = new SigningToolAvailability(
            GpgAvailable: gpg.Found,
            GpgVersion: gpg.FirstLine,
            SshAvailable: ssh.Found,
            SshVersion: ssh.FirstLine);

        lock (_lock) _cachedAvailability = availability;
        return availability;
    }

    public async Task<IReadOnlyList<GpgSecretKey>> ListGpgSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cachedGpgKeys is not null) return _cachedGpgKeys;
        }

        var availability = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!availability.GpgAvailable)
        {
            lock (_lock) _cachedGpgKeys = [];
            return [];
        }

        var (success, output) = await RunCapturingAsync(
            "gpg",
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
    /// Run a command and capture (stdout + stderr). Returns whether the
    /// process started at all — we use this for both "is it installed"
    /// (any output is positive) and "what's the output" (parser callers
    /// inspect the captured string).
    /// </summary>
    private static async Task<(bool Found, string FirstLine)> ProbeAsync(string exe, string args, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return (false, string.Empty);

            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await proc.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            // Any text output is proof the binary exists. ssh-keygen prints
            // its usage on stderr with no -V handling, but the binary is
            // installed — that's what we want to know.
            var combined = string.IsNullOrEmpty(stdout) ? stderr : stdout;
            var firstLine = combined.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
            return (combined.Length > 0, firstLine);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The exe wasn't found on PATH — the most common "not
            // installed" signal on Windows.
            return (false, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (false, string.Empty);
        }
    }

    private static async Task<(bool Success, string Output)> RunCapturingAsync(string exe, string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc == null) return (false, string.Empty);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            return (proc.ExitCode == 0, stdout);
        }
        catch (System.ComponentModel.Win32Exception) { return (false, string.Empty); }
        catch (OperationCanceledException) { return (false, string.Empty); }
    }
}
