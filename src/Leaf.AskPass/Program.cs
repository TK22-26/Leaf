using System.Diagnostics;
using Leaf.Services;

namespace Leaf.AskPass;

/// <summary>
/// GIT_ASKPASS helper invoked by git when credentials are needed.
///
/// Contract (from <c>gitcredentials(7)</c>):
///   - Git sets GIT_ASKPASS to this executable's full path.
///   - Git invokes us with the prompt text as argv[0], e.g.
///       "Username for 'https://github.com':"  or
///       "Password for 'https://user@github.com':"
///   - Helper writes the answer to stdout (one line, trailing newline OK)
///     and exits 0. Any non-zero exit aborts authentication.
///
/// Leaf's contract (env vars set by <c>GitCommandRunner</c> when a credential
/// context is supplied):
///   - LEAF_CREDENTIAL_KEY — required. The key under which the PAT is stored
///     in Windows Credential Manager (e.g. "GitHub:microsoft").
///   - LEAF_CREDENTIAL_USERNAME — optional. Username to return for Username
///     prompts. Defaults to "x-access-token" which is accepted by GitHub and
///     Azure DevOps when the PAT is used as password.
///
/// The helper fails loudly (exit 1, diagnostic on stderr) when the key is
/// missing or no PAT is found — per Leaf's Engineering Software Policy, we
/// do not silently substitute fallback credentials.
/// </summary>
internal static class Program
{
    private const string CredentialKeyEnv = "LEAF_CREDENTIAL_KEY";
    private const string UsernameEnv = "LEAF_CREDENTIAL_USERNAME";
    private const string DefaultUsername = "x-access-token";

    private static int Main(string[] args)
    {
        var prompt = args.Length > 0 ? args[0] : string.Empty;
        var key = Environment.GetEnvironmentVariable(CredentialKeyEnv);

        if (string.IsNullOrEmpty(key))
        {
            Console.Error.WriteLine($"Leaf.AskPass: {CredentialKeyEnv} environment variable is not set.");
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
}
