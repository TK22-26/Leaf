using System.Diagnostics;
using System.Text;
using Leaf.Services;

namespace Leaf.AskPass;

/// <summary>
/// Dual-purpose ASKPASS helper invoked by git (GIT_ASKPASS) and OpenSSH
/// (SSH_ASKPASS) when credentials or a private-key passphrase are needed.
///
/// <para><b>Git contract</b> (from <c>gitcredentials(7)</c>):</para>
///   - Git sets GIT_ASKPASS to this executable's full path.
///   - Git invokes us with the prompt text as argv[0], e.g.
///       "Username for 'https://github.com':"  or
///       "Password for 'https://user@github.com':"
///   - Helper writes the answer to stdout (one line, trailing newline OK)
///     and exits 0. Any non-zero exit aborts authentication.
///
/// <para><b>SSH contract</b> (from <c>ssh-add(1)</c> / <c>readpass.c</c>):</para>
///   - ssh-add invokes us with the prompt text as argv[0], e.g.
///       "Enter passphrase for /Users/me/.ssh/id_ed25519:"
///   - Helper writes the passphrase to stdout (one line) and exits 0.
///
/// <para><b>Leaf's contracts</b> (env vars set by Leaf when invoking the
/// underlying tool):</para>
/// <list type="bullet">
///   <item><c>LEAF_SSH_PASSPHRASE</c> — set when Leaf is loading a key
///   into ssh-agent. Takes precedence over the git path; presence of
///   this variable means "this is an SSH passphrase request, not a git
///   credential request". The value is the passphrase verbatim.
///   The variable is only on the ssh-add child process's environment —
///   not on Leaf's, not on the user's shell.</item>
///   <item><c>LEAF_CREDENTIAL_KEY</c> — required for the git path.
///   The key under which the PAT is stored in Windows Credential Manager
///   (e.g. "GitHub:microsoft").</item>
///   <item><c>LEAF_CREDENTIAL_USERNAME</c> — optional. Username to return
///   for Username prompts. Defaults to "x-access-token" which is accepted
///   by GitHub and Azure DevOps when the PAT is used as password.</item>
/// </list>
///
/// The helper fails loudly (exit 1, diagnostic on stderr) when the
/// expected environment variable is missing — per Leaf's Engineering
/// Software Policy, we do not silently substitute fallback credentials.
/// </summary>
internal static class Program
{
    private const string CredentialKeyEnv = "LEAF_CREDENTIAL_KEY";
    private const string UsernameEnv = "LEAF_CREDENTIAL_USERNAME";
    private const string SshPassphraseEnv = "LEAF_SSH_PASSPHRASE";
    private const string DefaultUsername = "x-access-token";

    private static int Main(string[] args)
    {
        // Force UTF-8 on stdout. The .NET console writer defaults to
        // the OEM/system code page when stdout is redirected (which it
        // always is here — git and ssh-add invoke us with a pipe).
        // That default mangles non-ASCII passphrases / PATs by the
        // time the parent reads bytes back. UTF-8 with no BOM matches
        // what both git and OpenSSH expect.
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var prompt = args.Length > 0 ? args[0] : string.Empty;

        // SSH path takes precedence — when LEAF_SSH_PASSPHRASE is set,
        // ssh-add invoked us via SSH_ASKPASS and is waiting on the key
        // passphrase. Empty string is a valid value (key has no
        // passphrase): ssh-add accepts it and loads the key directly.
        var sshPassphrase = Environment.GetEnvironmentVariable(SshPassphraseEnv);
        if (sshPassphrase is not null)
        {
            Debug.WriteLine($"[AskPass] SSH passphrase prompt: '{Truncate(prompt, 60)}'");
            // Write — no Line — to match the askpass contract exactly.
            // ssh-add reads up to the first newline OR EOF; printing
            // a trailing \n happens to work today but conflates
            // "the answer" with "end of stream".
            Console.Out.Write(sshPassphrase);
            return 0;
        }

        var key = Environment.GetEnvironmentVariable(CredentialKeyEnv);

        if (string.IsNullOrEmpty(key))
        {
            Console.Error.WriteLine(
                $"Leaf.AskPass: neither {SshPassphraseEnv} nor {CredentialKeyEnv} is set. "
                + "This helper is only meant to be invoked by Leaf.");
            return 1;
        }

        if (IsUsernamePrompt(prompt))
        {
            var username = Environment.GetEnvironmentVariable(UsernameEnv);
            if (string.IsNullOrEmpty(username))
            {
                username = DefaultUsername;
            }

            Debug.WriteLine($"[AskPass] username prompt for key={key} -> {username}");
            Console.Out.WriteLine(username);
            return 0;
        }

        // Anything that isn't a Username prompt we treat as a Password / PAT
        // prompt. git also uses prompts like "Password for 'https://...':" or,
        // with credential helpers that implement two-factor flows, "Token for
        // '...'" — we respond with the stored PAT in all these cases.
        var credentials = new CredentialService();
        var pat = credentials.GetPat(key);
        if (string.IsNullOrEmpty(pat))
        {
            Console.Error.WriteLine($"Leaf.AskPass: no credential stored under key '{key}'.");
            return 1;
        }

        Debug.WriteLine($"[AskPass] password prompt for key={key} -> PAT resolved");
        Console.Out.WriteLine(pat);
        return 0;
    }

    private static bool IsUsernamePrompt(string prompt)
    {
        // Git prefixes username prompts with "Username" (see fetch-pack.c,
        // remote-curl.c). Case-insensitive match handles localisation variants
        // git does not actually localise, but we stay defensive.
        return prompt.StartsWith("Username", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
