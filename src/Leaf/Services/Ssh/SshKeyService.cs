using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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

        // Case-insensitive .pub match — keys imported from another OS
        // (or a stricter filesystem) might be `id_ed25519.PUB`. Windows
        // EnumerateFiles' glob is already case-insensitive on NTFS, but
        // the explicit check guarantees behaviour across mounts (WSL
        // bind mounts, network shares with case-sensitive flags).
        var pubFiles = Directory.EnumerateFiles(SshDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(p => string.Equals(Path.GetExtension(p), ".pub", StringComparison.OrdinalIgnoreCase))
            .ToList();
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
            var probe = await ProcessHelper.RunAsync("ssh-keygen", ["-l", "-f", pubPath], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (probe.Spawned && probe.ExitCode == 0
                && SshPublicKeyParser.TryParseFingerprintLine(probe.Output, out var parsedBits, out var parsedFp, out var parsedComment))
            {
                bits = parsedBits;
                fingerprint = parsedFp;
                if (!string.IsNullOrWhiteSpace(parsedComment)) fingerprintComment = parsedComment;
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
        var probe = await ProcessHelper.RunAsync(
            "ssh",
            ["-T", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=accept-new", "-o", "ConnectTimeout=10", sshTarget],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var output = probe.Output;

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

        var parentDir = Path.GetDirectoryName(request.OutputPath)!;
        // When the user generates into ~/.ssh (the default), apply the
        // owner-only ACL via EnsureSshDirectoryCore; for other parents,
        // CreateDirectory is fine — those are the user's call.
        if (string.Equals(Path.GetFullPath(parentDir), Path.GetFullPath(SshDirectory), StringComparison.OrdinalIgnoreCase))
            EnsureSshDirectoryCore();
        else
            Directory.CreateDirectory(parentDir);

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
        // Bits flag is only valid for RSA / ECDSA. Adding it to Ed25519
        // produces "Invalid key length" exit 1.
        if (request.Bits is { } bits && request.Algorithm is SshKeyAlgorithm.Rsa or SshKeyAlgorithm.Ecdsa)
        {
            args.AddRange(["-b", bits.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        var probe = await ProcessHelper.RunAsync("ssh-keygen", args.ToArray(), cancellationToken: cancellationToken).ConfigureAwait(false);
        // ssh-keygen exits 0 on success and writes the random-art header
        // to stderr. Any non-zero / non-spawn outcome is a real failure.
        if (!probe.Spawned || probe.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(probe.Output) ? "ssh-keygen failed." : probe.Output.Trim();
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
        EnsureSshDirectoryCore();

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
        var probe = await ProcessHelper.RunAsync("ssh-add", ["-l", "-E", "sha256"], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!probe.Spawned || probe.ExitCode == 2 || probe.ExitCode == 1) return [];

        var keys = new List<SshAgentKey>();
        foreach (var rawLine in probe.Output.Split('\n'))
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
                algo = SshPublicKeyParser.MapAlgorithm(typeToken);
            }
            keys.Add(new SshAgentKey(bits, fp, comment, algo));
        }
        return keys;
    }

    public async Task<SshAgentOperationResult> AddKeyToAgentAsync(string privateKeyPath, string? passphrase, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(privateKeyPath))
            return new SshAgentOperationResult(false, $"Private key not found: {privateKeyPath}");

        // ssh-add on Windows reads passphrases from the controlling
        // terminal via AllocConsole, NOT from stdin. Under WPF there's
        // no console, so the only viable path is to point ssh-add at
        // an SSH_ASKPASS helper. We reuse Leaf.AskPass.exe — the same
        // helper that already serves GIT_ASKPASS — and tell it the
        // passphrase via the LEAF_SSH_PASSPHRASE env var that AskPass
        // checks before falling through to its git logic.
        //
        // SSH_ASKPASS_REQUIRE=force makes ssh-add ALWAYS use the
        // helper, even when a tty is technically available. Without
        // that, OpenSSH 8.4+ would prefer the (non-existent) tty.
        var askPass = AskPassPathResolver.ExecutablePath;
        if (askPass is null)
        {
            return new SshAgentOperationResult(false,
                "Leaf.AskPass.exe is missing from the install directory. "
                + "ssh-add can't be driven without it under WPF.");
        }

        var environmentOverrides = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["SSH_ASKPASS"] = askPass,
            ["SSH_ASKPASS_REQUIRE"] = "force",
            // DISPLAY must be non-empty on legacy OpenSSH for SSH_ASKPASS
            // to be honoured — modern Windows builds ignore it but
            // setting it costs nothing. The value is irrelevant.
            ["DISPLAY"] = ":0",
            ["LEAF_SSH_PASSPHRASE"] = passphrase ?? string.Empty,
        };

        // ssh-add doesn't read stdin under SSH_ASKPASS_REQUIRE=force —
        // env-only path is enough, no need to allocate the stdin pipe.
        var result = await ProcessHelper.RunAsync(
            "ssh-add",
            new[] { privateKeyPath },
            environmentOverrides,
            cancellationToken).ConfigureAwait(false);

        if (!result.Spawned || result.ExitCode != 0)
        {
            return new SshAgentOperationResult(false, string.IsNullOrWhiteSpace(result.Output)
                ? "ssh-add failed without output."
                : result.Output.Trim());
        }
        return new SshAgentOperationResult(true, result.Output.Trim());
    }


    public async Task<SshAgentOperationResult> RemoveKeyFromAgentAsync(string privateKeyPath, CancellationToken cancellationToken = default)
    {
        var probe = await ProcessHelper.RunAsync("ssh-add", ["-d", privateKeyPath], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!probe.Spawned || probe.ExitCode != 0)
        {
            return new SshAgentOperationResult(false, string.IsNullOrWhiteSpace(probe.Output) ? "ssh-add -d failed." : probe.Output.Trim());
        }
        return new SshAgentOperationResult(true, probe.Output.Trim());
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
            var probe = await ProcessHelper.RunAsync("ssh-add", ["-l"], cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!probe.Spawned)
                agentStatus = SshAgentStatus.NotRunning;
            else
                agentStatus = probe.ExitCode switch
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
        var probe = await ProcessHelper.RunAsync("ssh-keygen", ["-l", "-f", pubPath], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (probe.Spawned && probe.ExitCode == 0
            && SshPublicKeyParser.TryParseFingerprintLine(probe.Output, out var parsedBits, out var parsedFp, out var parsedComment))
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

    private static string StripPubExtension(string pubPath)
    {
        // Strip whatever case the extension actually has, not a hardcoded ".pub".
        var ext = Path.GetExtension(pubPath);
        return string.Equals(ext, ".pub", StringComparison.OrdinalIgnoreCase)
            ? pubPath[..^ext.Length]
            : pubPath;
    }

    /// <inheritdoc />
    public void EnsureSshDirectory() => EnsureSshDirectoryCore();

    /// <summary>
    /// Create <c>~/.ssh</c> if missing and apply an owner-only ACL —
    /// the Windows analogue of POSIX 700. OpenSSH's <c>StrictModes</c>
    /// refuses to read a config / key whose containing directory is
    /// readable or writable by anyone but the owner, so getting this
    /// right is the difference between "ssh works" and "Permission
    /// denied (publickey)" with no obvious cause.
    ///
    /// <para>If the directory already exists we do NOT rewrite its ACL —
    /// silently tightening permissions could break other tools the user
    /// has set up. OpenSSH will surface "bad permissions" if the
    /// existing ACL is too loose, which is the correct place for that
    /// signal to come from.</para>
    ///
    /// <para>The static implementation is private because in-process
    /// callers must go through the <see cref="ISshKeyService"/> instance
    /// — keeping the method static-internal would re-introduce the
    /// architectural inconsistency the audit flagged.</para>
    /// </summary>
    private static void EnsureSshDirectoryCore()
    {
        if (Directory.Exists(SshDirectory)) return;

        var info = Directory.CreateDirectory(SshDirectory);

        try
        {
            var owner = WindowsIdentity.GetCurrent().User;
            if (owner is null) return;

            var security = new DirectorySecurity();
            security.SetOwner(owner);
            // Block inheritance — start from a clean slate so we don't
            // pick up Users/Authenticated Users entries from the
            // profile root.
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            // Owner gets full control; everyone else gets nothing.
            security.AddAccessRule(new FileSystemAccessRule(
                identity: owner,
                fileSystemRights: FileSystemRights.FullControl,
                inheritanceFlags: InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                propagationFlags: PropagationFlags.None,
                type: AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException)
        {
            // ACL apply failed — directory still exists with default
            // perms. Log so the user has something to look at if ssh
            // later complains about StrictModes. Don't throw: the ssh
            // operations will surface the real error themselves.
            Log.Warn("Ssh", $"Could not tighten ~/.ssh ACL: {ex.Message}");
        }
    }

    /// <summary>
    /// Map our enum onto the <c>-t</c> argument ssh-keygen accepts.
    /// DSA is intentionally absent: OpenSSH dropped it from the
    /// generation defaults in 7.0 (2015) and current builds reject
    /// <c>-t dsa</c>. Existing on-disk DSA keys still parse via
    /// <see cref="SshPublicKeyParser"/> — only generation is gone.
    /// </summary>
    private static string AlgorithmToken(SshKeyAlgorithm algorithm) => algorithm switch
    {
        SshKeyAlgorithm.Ed25519 => "ed25519",
        SshKeyAlgorithm.Rsa => "rsa",
        SshKeyAlgorithm.Ecdsa => "ecdsa",
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported SSH key algorithm for generation."),
    };

    /// <summary>
    /// Probe whether <paramref name="exe"/> is on PATH by spawning it
    /// with <c>-V</c> (version). ssh, ssh-add, ssh-keygen all support
    /// it and exit immediately; spawning with no args would block ssh
    /// reading from stdin.
    /// </summary>
    private static Task<bool> CanSpawnAsync(string exe, CancellationToken cancellationToken) =>
        ProcessHelper.CanSpawnAsync(exe, ["-V"], cancellationToken);
}
