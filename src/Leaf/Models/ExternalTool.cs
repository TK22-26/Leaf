namespace Leaf.Models;

/// <summary>
/// Whether an external tool is invoked for a two-way diff or a three-way merge.
/// Git stores diff and merge tools separately (<c>difftool.&lt;name&gt;.cmd</c> vs
/// <c>mergetool.&lt;name&gt;.cmd</c>) and so do we.
/// </summary>
public enum ExternalToolKind
{
    Diff,
    Merge
}

/// <summary>
/// A single external tool binding — either a preset shipped with Leaf
/// or a user-defined entry written back to <c>.git/config</c>. The
/// <see cref="ArgsTemplate"/> uses Git's conventional placeholders
/// (<c>$LOCAL</c>, <c>$REMOTE</c>, <c>$BASE</c>, <c>$MERGED</c>) which
/// <c>IExternalToolLauncherService</c> substitutes at launch.
/// </summary>
/// <param name="Name">git-config key name (e.g. <c>"vscode"</c>, <c>"bcomp"</c>).</param>
/// <param name="DisplayName">UI-friendly label (e.g. <c>"Visual Studio Code"</c>).</param>
/// <param name="Kind">Diff or Merge.</param>
/// <param name="Command">Executable name (on PATH) or absolute path.</param>
/// <param name="ArgsTemplate">Command-line template with $LOCAL/$REMOTE/$BASE/$MERGED.</param>
/// <param name="KnownInstallPaths">
/// Candidate absolute paths that auto-detection checks when the executable isn't on PATH.
/// Environment variables like <c>%ProgramFiles%</c> are expanded at detection time.
/// </param>
public sealed record ExternalTool(
    string Name,
    string DisplayName,
    ExternalToolKind Kind,
    string Command,
    string ArgsTemplate,
    IReadOnlyList<string> KnownInstallPaths)
{
    /// <summary>
    /// Sentinel name stored in git config when the user explicitly picks
    /// "use Leaf's built-in viewer". Treated as "no external tool
    /// configured" by the launcher.
    /// </summary>
    public const string BuiltInName = "leaf-builtin";

    /// <summary>
    /// Factory for the built-in fallback entry.
    /// </summary>
    public static ExternalTool BuiltIn(ExternalToolKind kind) =>
        new(BuiltInName, "Leaf built-in viewer", kind, string.Empty, string.Empty, []);

    /// <summary>
    /// True when this entry represents Leaf's built-in viewer rather than
    /// a real external process.
    /// </summary>
    public bool IsBuiltIn => Name == BuiltInName;
}
