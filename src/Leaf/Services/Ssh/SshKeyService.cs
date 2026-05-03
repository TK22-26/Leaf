using System.Diagnostics;
using System.IO;
using System.Text;

namespace Leaf.Services.Ssh;

/// <summary>
/// Concrete <see cref="ISshKeyService"/>. Wraps OpenSSH binaries
/// (<c>ssh</c>, <c>ssh-keygen</c>, <c>ssh-add</c>) plus a small parser
/// for <c>~/.ssh/config</c>. Caches nothing — these calls are cheap and
/// the user expects "what's on disk now" semantics in the settings panel.
/// </summary>
public sealed class SshKeyService : ISshKeyService
{
    private static string SshDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    private static string SshConfigPath => Path.Combine(SshDirectory, "config");

    public async Task<IReadOnlyList<SshPublicKey>> ListPublicKeysAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(SshDirectory)) return [];

        var pubFiles = Directory.EnumerateFiles(SshDirectory, "*.pub", SearchOption.TopDirectoryOnly).ToList();
        if (pubFiles.Count == 0) return [];

        var keys = new List<SshPublicKey>(pubFiles.Count);
        foreach (var pubPath in pubFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try
            {
                text = await File.ReadAllTextAsync(pubPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Ssh", $"Could not read {pubPath}: {ex.Message}");
                continue;
            }

            if (!SshPublicKeyParser.TryParse(text, out var algorithm, out var commentFromFile))
            {
                continue; // not a real public key — backup files, etc.
            }

            // Fingerprint via ssh-keygen — the canonical source. If
            // ssh-keygen is missing we still return the entry, just with
            // an empty fingerprint; the panel labels that state clearly.
            var fingerprint = string.Empty;
            int? bits = null;
            var fingerprintComment = commentFromFile;
            var (ok, output) = await RunCapturingAsync("ssh-keygen", ["-l", "-f", pubPath], cancellationToken).ConfigureAwait(false);
            if (ok)
            {
                if (SshPublicKeyParser.TryParseFingerprintLine(output, out var parsedBits, out var parsedFp, out var parsedComment))
                {
                    bits = parsedBits;
                    fingerprint = parsedFp;
                    if (!string.IsNullOrWhiteSpace(parsedComment)) fingerprintComment = parsedComment;
                }
            }

            var privatePath = StripPubExtension(pubPath);
            keys.Add(new SshPublicKey(
                PublicKeyPath: pubPath,
                PrivateKeyPath: privatePath,
                Algorithm: algorithm,
                Comment: fingerprintComment,
                Fingerprint: fingerprint,
                KeyBits: bits));
        }

        // Stable, alphabetic ordering — keeps the list predictable across reopens.
        keys.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return keys;
    }

    public async Task<string> ReadPublicKeyTextAsync(string path, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return text.TrimEnd('\r', '\n');
    }

    public async Task<SshConnectionTestResult> TestConnectionAsync(string sshTarget, CancellationToken cancellationToken = default)
    {
        // -T disables PTY allocation (no shell prompt).
        // -o BatchMode=yes refuses to fall back to password prompts.
        // -o StrictHostKeyChecking=accept-new auto-trusts unknown hosts on first contact.
        // -o ConnectTimeout=10 keeps the test from hanging indefinitely.
        var (_, output) = await RunCapturingBothStreamsAsync(
            "ssh",
            ["-T", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=accept-new", "-o", "ConnectTimeout=10", sshTarget],
            cancellationToken).ConfigureAwait(false);

        var authenticated =
            output.Contains("successfully authenticated", StringComparison.OrdinalIgnoreCase)
            || output.Contains("does not provide shell access", StringComparison.OrdinalIgnoreCase)
            || output.Contains("logged in as", StringComparison.OrdinalIgnoreCase)
            || output.StartsWith("Hi ", StringComparison.Ordinal)
            || output.Contains("\nHi ", StringComparison.Ordinal);

        // Try to extract the GitHub-style "Hi <username>!" identity from
        // the greeting. Only lifts a pure-ASCII username — anything more
        // ambitious risks misparsing a GitLab welcome line.
        string? identity = null;
        var hiMarker = output.IndexOf("Hi ", StringComparison.Ordinal);
        if (hiMarker >= 0)
        {
            var slice = output[(hiMarker + 3)..];
            var bang = slice.IndexOfAny(['!', ',', '\n', '\r']);
            if (bang > 0) identity = slice[..bang].Trim();
        }

        return new SshConnectionTestResult(authenticated, output.TrimEnd(), identity);
    }

    public async Task<SshKeyGenerationResult> GenerateKeyAsync(SshKeyGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OutputPath))
            return new SshKeyGenerationResult(false, "Output path is required.", null);

        if (File.Exists(request.OutputPath) || File.Exists(request.OutputPath + ".pub"))
            return new SshKeyGenerationResult(false, "A key already exists at that path. Choose a different filename.", null);

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);

        var args = new List<string>
        {
            "-t", AlgorithmToken(request.Algorithm),
            "-f", request.OutputPath,
            "-C", request.Comment ?? string.Empty,
            // -N "" with empty passphrase is the documented way to skip
            // the prompt; ssh-keygen otherwise blocks reading from a tty.
            "-N", request.Passphrase ?? string.Empty,
            // Quiet output — suppress the ASCII art random-art keeping
            // the dialog's success message clean.
            "-q",
        };
        // Bits flag is only valid for RSA / ECDSA / DSA. Adding it to
        // Ed25519 produces "Invalid key length" exit 1.
        if (request.Bits is { } bits && request.Algorithm is SshKeyAlgorithm.Rsa or SshKeyAlgorithm.Ecdsa or SshKeyAlgorithm.Dsa)
        {
            args.AddRange(["-b", bits.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        var (ok, output) = await RunCapturingBothStreamsAsync("ssh-keygen", args.ToArray(), cancellationToken).ConfigureAwait(false);
        if (!ok)
        {
            var message = string.IsNullOrWhiteSpace(output) ? "ssh-keygen failed." : output.Trim();
            return new SshKeyGenerationResult(false, message, null);
        }

        // Re-list so the caller gets the parsed metadata for the new key.
        var pubPath = request.OutputPath + ".pub";
        if (!File.Exists(pubPath))
            return new SshKeyGenerationResult(false, "ssh-keygen reported success but no public key was written.", null);

        var generated = await BuildPublicKeyRecordAsync(pubPath, cancellationToken).ConfigureAwait(false);
        return new SshKeyGenerationResult(true, null, generated);
    }

    public async Task<IReadOnlyList<SshConfigEntry>> ReadSshConfigAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SshConfigPath)) return [];
        var text = await File.ReadAllTextAsync(SshConfigPath, cancellationToken).ConfigureAwait(false);
        var parsed = SshConfigParser.Parse(text);
        return parsed.Hosts;
    }

    public async Task WriteSshConfigAsync(IReadOnlyList<SshConfigEntry> entries, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(SshDirectory);

        // Preserve preamble + Match tail from the existing file. The
        // editor only modifies Host stanzas, so re-using the leading
        // comments / Include directives keeps round-trips clean.
        string preamble = string.Empty;
        string matchTail = string.Empty;
        if (File.Exists(SshConfigPath))
        {
            var existing = await File.ReadAllTextAsync(SshConfigPath, cancellationToken).ConfigureAwait(false);
            var parsed = SshConfigParser.Parse(existing);
            preamble = parsed.LeadingPreamble;
            matchTail = parsed.MatchTail;
        }

        var rewritten = new SshConfigParser.ParsedConfig(preamble, entries, matchTail);
        var output = SshConfigParser.Write(rewritten);

        // Use UTF8 (no BOM); OpenSSH on Windows tolerates a BOM but it
        // produces a phantom diff if the file was previously plain ASCII.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(output);
        await File.WriteAllBytesAsync(SshConfigPath, bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SshAgentKey>> ListAgentKeysAsync(CancellationToken cancellationToken = default)
    {
        // ssh-add -l with -E sha256 so the fingerprints match
        // ssh-keygen's default. Exit 1 means "no keys"; exit 2 means
        // "agent not running" — caller distinguishes via DetectToolingAsync.
        var (success, exitCode, output) = await RunCapturingWithExitAsync("ssh-add", ["-l", "-E", "sha256"], cancellationToken).ConfigureAwait(false);
        if (!success || exitCode == 2) return [];
        if (exitCode == 1) return []; // "The agent has no identities."

        var keys = new List<SshAgentKey>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            // Format: BITS FINGERPRINT COMMENT (TYPE)
            if (!SshPublicKeyParser.TryParseFingerprintLine(line, out var bits, out var fp, out var comment))
                continue;

            var algo = SshKeyAlgorithm.Unknown;
            var typeStart = line.LastIndexOf('(');
            if (typeStart > 0 && line.EndsWith(')'))
            {
                var typeToken = line[(typeStart + 1)..^1].Trim();
                algo = MapAgentTypeToken(typeToken);
            }
            keys.Add(new SshAgentKey(bits, fp, comment, algo));
        }
        return keys;
    }

    public async Task<SshAgentOperationResult> AddKeyToAgentAsync(string privateKeyPath, string? passphrase, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(privateKeyPath))
            return new SshAgentOperationResult(false, $"Private key not found: {privateKeyPath}");

        // ssh-add reads the passphrase from stdin when SSH_ASKPASS is
        // unset and there's no controlling terminal — which is exactly
        // our situation under WPF. Pipe the passphrase + a newline to
        // stdin so ssh-add picks it up without spawning a prompt window.
        // Empty passphrase is fine (ssh-add prompts but immediately
        // accepts the empty input).
        var (success, exitCode, output) = await RunCapturingWithStdinAsync(
            "ssh-add",
            [privateKeyPath],
            (passphrase ?? string.Empty) + "\n",
            cancellationToken).ConfigureAwait(false);

        if (!success || exitCode != 0)
        {
            return new SshAgentOperationResult(false, string.IsNullOrWhiteSpace(output)
                ? "ssh-add failed without output."
                : output.Trim());
        }
        return new SshAgentOperationResult(true, output.Trim());
    }

    public async Task<SshAgentOperationResult> RemoveKeyFromAgentAsync(string privateKeyPath, CancellationToken cancellationToken = default)
    {
        var (success, exitCode, output) = await RunCapturingWithExitAsync("ssh-add", ["-d", privateKeyPath], cancellationToken).ConfigureAwait(false);
        if (!success || exitCode != 0)
        {
            return new SshAgentOperationResult(false, string.IsNullOrWhiteSpace(output) ? "ssh-add -d failed." : output.Trim());
        }
        return new SshAgentOperationResult(true, output.Trim());
    }

    public async Task<SshToolingAvailability> DetectToolingAsync(CancellationToken cancellationToken = default)
    {
        var sshTask = CanSpawnAsync("ssh", cancellationToken);
        var keygenTask = CanSpawnAsync("ssh-keygen", cancellationToken);
        var addTask = CanSpawnAsync("ssh-add", cancellationToken);
        await Task.WhenAll(sshTask, keygenTask, addTask).ConfigureAwait(false);

        SshAgentStatus agentStatus;
        if (!await addTask.ConfigureAwait(false))
        {
            agentStatus = SshAgentStatus.Unavailable;
        }
        else
        {
            var (success, exitCode, _) = await RunCapturingWithExitAsync("ssh-add", ["-l"], cancellationToken).ConfigureAwait(false);
            if (!success)
                agentStatus = SshAgentStatus.NotRunning;
            else
                agentStatus = exitCode switch
                {
                    0 or 1 => SshAgentStatus.Running, // 0 = some keys, 1 = no keys but agent is alive
                    2 => SshAgentStatus.NotRunning,
                    _ => SshAgentStatus.NotRunning,
                };
        }

        return new SshToolingAvailability(
            HasSsh: await sshTask.ConfigureAwait(false),
            HasSshKeygen: await keygenTask.ConfigureAwait(false),
            HasSshAdd: await addTask.ConfigureAwait(false),
            AgentStatus: agentStatus);
    }

    private async Task<SshPublicKey> BuildPublicKeyRecordAsync(string pubPath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(pubPath, cancellationToken).ConfigureAwait(false);
        SshPublicKeyParser.TryParse(text, out var algorithm, out var comment);

        var fingerprint = string.Empty;
        int? bits = null;
        var (ok, output) = await RunCapturingAsync("ssh-keygen", ["-l", "-f", pubPath], cancellationToken).ConfigureAwait(false);
        if (ok && SshPublicKeyParser.TryParseFingerprintLine(output, out var parsedBits, out var parsedFp, out var parsedComment))
        {
            bits = parsedBits;
            fingerprint = parsedFp;
            if (!string.IsNullOrWhiteSpace(parsedComment)) comment = parsedComment;
        }
        return new SshPublicKey(
            PublicKeyPath: pubPath,
            PrivateKeyPath: StripPubExtension(pubPath),
            Algorithm: algorithm,
            Comment: comment,
            Fingerprint: fingerprint,
            KeyBits: bits);
    }

    private static string StripPubExtension(string pubPath) =>
        pubPath.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) ? pubPath[..^4] : pubPath;

    private static string AlgorithmToken(SshKeyAlgorithm algorithm) => algorithm switch
    {
        SshKeyAlgorithm.Ed25519 => "ed25519",
        SshKeyAlgorithm.Rsa => "rsa",
        SshKeyAlgorithm.Ecdsa => "ecdsa",
        SshKeyAlgorithm.Dsa => "dsa",
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported SSH key algorithm."),
    };

    private static SshKeyAlgorithm MapAgentTypeToken(string token) => token switch
    {
        "ED25519" => SshKeyAlgorithm.Ed25519,
        "RSA" => SshKeyAlgorithm.Rsa,
        "ECDSA" => SshKeyAlgorithm.Ecdsa,
        "DSA" => SshKeyAlgorithm.Dsa,
        _ => SshPublicKeyParser.MapAlgorithm(token),
    };

    private static async Task<bool> CanSpawnAsync(string exe, CancellationToken cancellationToken)
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
            // ssh / ssh-add / ssh-keygen all support -V (version) and exit
            // immediately. Spawning with no args would block ssh on
            // input.
            psi.ArgumentList.Add("-V");
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // not on PATH
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<(bool Success, string Output)> RunCapturingAsync(string exe, string[] args, CancellationToken cancellationToken)
    {
        var (ok, _, output) = await RunCapturingWithExitAsync(exe, args, cancellationToken).ConfigureAwait(false);
        return (ok && output.Length > 0, output);
    }

    private static async Task<(bool Spawned, int ExitCode, string Output)> RunCapturingWithExitAsync(string exe, string[] args, CancellationToken cancellationToken)
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
            if (proc == null) return (false, -1, string.Empty);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            // Some commands (ssh-add, ssh-keygen -lf) write the user-
            // facing line to stdout; others (ssh -T) write the success
            // greeting to stderr. Concatenate so callers don't have to
            // care which stream produced the relevant text.
            var combined = stdout + (stdout.Length > 0 && stderr.Length > 0 ? "\n" : string.Empty) + stderr;
            return (true, proc.ExitCode, combined);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, -1, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (false, -1, string.Empty);
        }
    }

    private static async Task<(bool Spawned, string Output)> RunCapturingBothStreamsAsync(string exe, string[] args, CancellationToken cancellationToken)
    {
        var (ok, _, output) = await RunCapturingWithExitAsync(exe, args, cancellationToken).ConfigureAwait(false);
        return (ok, output);
    }

    private static async Task<(bool Spawned, int ExitCode, string Output)> RunCapturingWithStdinAsync(
        string exe, string[] args, string stdinText, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            // Defeat the OpenSSH askpass / DISPLAY heuristic so it reads
            // from the redirected stdin rather than spawning a GUI prompt.
            psi.Environment["SSH_ASKPASS"] = string.Empty;
            psi.Environment["DISPLAY"] = string.Empty;

            using var proc = Process.Start(psi);
            if (proc == null) return (false, -1, string.Empty);

            await proc.StandardInput.WriteAsync(stdinText.AsMemory(), cancellationToken).ConfigureAwait(false);
            await proc.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            proc.StandardInput.Close();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var combined = stdout + (stdout.Length > 0 && stderr.Length > 0 ? "\n" : string.Empty) + stderr;
            return (true, proc.ExitCode, combined);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (false, -1, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (false, -1, string.Empty);
        }
    }
}
