using System.IO;

namespace Leaf.Services;

/// <summary>
/// Single source of truth for the path to <c>Leaf.AskPass.exe</c>.
/// Both the git path (<see cref="GitCommandRunner"/>) and the SSH path
/// (<see cref="Leaf.Services.Ssh.SshKeyService"/>) need to find the
/// helper at runtime; previously each had its own probe and they
/// disagreed on caching + log behaviour. Centralising the resolver
/// guarantees they agree, and a missing helper is logged exactly once
/// per process even when both paths look it up.
/// </summary>
internal static class AskPassPathResolver
{
    private const string AskPassExecutable = "Leaf.AskPass.exe";

    private static readonly Lazy<string?> _executablePath = new(() =>
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, AskPassExecutable);
        if (File.Exists(candidate)) return candidate;
        Log.Warn("AskPass",
            $"{AskPassExecutable} not found at {candidate}; "
            + "credential-requiring commands will fall back to Git Credential Manager, "
            + "and SSH agent operations that need a passphrase will fail with a clear error.");
        return null;
    });

    /// <summary>
    /// Absolute path to <c>Leaf.AskPass.exe</c>, or null when the helper
    /// is missing from the install directory. Cached for the process
    /// lifetime — installs don't change while Leaf is running.
    /// </summary>
    public static string? ExecutablePath => _executablePath.Value;
}
