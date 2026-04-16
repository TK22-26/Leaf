namespace Leaf.Utils;

/// <summary>
/// Pragmatic validator for UI-entered branch names. Enforces a subset of
/// git's <c>check-ref-format</c> rules — enough to reject the inputs that
/// git itself would refuse, without pulling in the full ref-format grammar.
/// Git is still the final authority; this is a fail-fast for interactive
/// forms so users see a disabled Create button before the command runs.
/// </summary>
public static class BranchNameValidator
{
    /// <summary>
    /// Characters rejected outright by git in branch names, plus whitespace.
    /// Kept as a const string so the preview-text-input handler in code-behind
    /// can probe individual chars without allocating a HashSet per keystroke.
    /// </summary>
    public const string InvalidCharacters = " ~^:?*[\\@{}";

    /// <summary>
    /// Returns true if <paramref name="name"/> is a plausible branch name.
    /// Rejects empty/whitespace, spaces, <c>..</c>, and leading/trailing
    /// slash or dot — the most common foot-guns for interactive input.
    /// </summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Contains(' ')) return false;
        if (name.Contains("..")) return false;
        if (name.StartsWith('/') || name.EndsWith('/')) return false;
        if (name.StartsWith('.') || name.EndsWith('.')) return false;
        return true;
    }

    /// <summary>
    /// Returns true if <paramref name="c"/> is disallowed anywhere in a
    /// branch name (git's forbidden-char list plus control chars).
    /// </summary>
    public static bool IsForbiddenCharacter(char c) =>
        char.IsControl(c) || InvalidCharacters.Contains(c);

    /// <summary>
    /// Returns true if the candidate composed text would be rejected by git
    /// for structural reasons (leading dot, leading dash, double-dot, <c>@{</c>).
    /// Character-level rejection is handled by <see cref="IsForbiddenCharacter"/>.
    /// </summary>
    public static bool HasInvalidStructure(string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        return candidate.StartsWith('.')
            || candidate.StartsWith('-')
            || candidate.Contains("..")
            || candidate.Contains("@{");
    }
}
