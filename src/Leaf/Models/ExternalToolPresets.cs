using System.Collections.Immutable;

namespace Leaf.Models;

/// <summary>
/// The named external tools Leaf ships with. Each preset appears in the
/// Settings dropdown; detection scans <see cref="ExternalTool.KnownInstallPaths"/>
/// and PATH to decide which are actually installed.
/// <para>
/// Command lines follow Git's mergetool conventions so that tools
/// selected here also work for <c>git difftool</c> / <c>git mergetool</c>
/// without further configuration.
/// </para>
/// </summary>
public static class ExternalToolPresets
{
    public static readonly ImmutableArray<ExternalTool> All = [
        // --- Visual Studio Code (baseline — matches legacy hardcoded integration) ---
        new ExternalTool(
            Name: "vscode",
            DisplayName: "Visual Studio Code",
            Kind: ExternalToolKind.Diff,
            Command: "code",
            ArgsTemplate: "--wait --diff \"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [
                @"%LocalAppData%\Programs\Microsoft VS Code\bin\code.cmd",
                @"%ProgramFiles%\Microsoft VS Code\bin\code.cmd"
            ]),
        new ExternalTool(
            Name: "vscode",
            DisplayName: "Visual Studio Code",
            Kind: ExternalToolKind.Merge,
            Command: "code",
            ArgsTemplate: "--wait --merge \"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\"",
            KnownInstallPaths: [
                @"%LocalAppData%\Programs\Microsoft VS Code\bin\code.cmd",
                @"%ProgramFiles%\Microsoft VS Code\bin\code.cmd"
            ]),

        // --- Beyond Compare 4/5 ---
        new ExternalTool(
            Name: "bcomp",
            DisplayName: "Beyond Compare",
            Kind: ExternalToolKind.Diff,
            Command: "bcomp",
            ArgsTemplate: "\"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\Beyond Compare 5\BCompare.exe",
                @"%ProgramFiles%\Beyond Compare 4\BCompare.exe",
                @"%ProgramFiles(x86)%\Beyond Compare 5\BCompare.exe",
                @"%ProgramFiles(x86)%\Beyond Compare 4\BCompare.exe"
            ]),
        new ExternalTool(
            Name: "bcomp",
            DisplayName: "Beyond Compare",
            Kind: ExternalToolKind.Merge,
            Command: "bcomp",
            ArgsTemplate: "\"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\Beyond Compare 5\BCompare.exe",
                @"%ProgramFiles%\Beyond Compare 4\BCompare.exe",
                @"%ProgramFiles(x86)%\Beyond Compare 5\BCompare.exe",
                @"%ProgramFiles(x86)%\Beyond Compare 4\BCompare.exe"
            ]),

        // --- KDiff3 ---
        new ExternalTool(
            Name: "kdiff3",
            DisplayName: "KDiff3",
            Kind: ExternalToolKind.Diff,
            Command: "kdiff3",
            ArgsTemplate: "\"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\KDiff3\kdiff3.exe",
                @"%ProgramFiles(x86)%\KDiff3\kdiff3.exe"
            ]),
        new ExternalTool(
            Name: "kdiff3",
            DisplayName: "KDiff3",
            Kind: ExternalToolKind.Merge,
            Command: "kdiff3",
            ArgsTemplate: "\"$BASE\" \"$LOCAL\" \"$REMOTE\" -o \"$MERGED\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\KDiff3\kdiff3.exe",
                @"%ProgramFiles(x86)%\KDiff3\kdiff3.exe"
            ]),

        // --- P4Merge (Perforce visual merge) ---
        new ExternalTool(
            Name: "p4merge",
            DisplayName: "P4Merge",
            Kind: ExternalToolKind.Diff,
            Command: "p4merge",
            ArgsTemplate: "\"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\Perforce\p4merge.exe",
                @"%ProgramFiles(x86)%\Perforce\p4merge.exe"
            ]),
        new ExternalTool(
            Name: "p4merge",
            DisplayName: "P4Merge",
            Kind: ExternalToolKind.Merge,
            Command: "p4merge",
            ArgsTemplate: "\"$BASE\" \"$LOCAL\" \"$REMOTE\" \"$MERGED\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\Perforce\p4merge.exe",
                @"%ProgramFiles(x86)%\Perforce\p4merge.exe"
            ]),

        // --- WinMerge ---
        new ExternalTool(
            Name: "winmerge",
            DisplayName: "WinMerge",
            Kind: ExternalToolKind.Diff,
            Command: "WinMergeU",
            ArgsTemplate: "/u /e \"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\WinMerge\WinMergeU.exe",
                @"%ProgramFiles(x86)%\WinMerge\WinMergeU.exe"
            ]),
        new ExternalTool(
            Name: "winmerge",
            DisplayName: "WinMerge",
            Kind: ExternalToolKind.Merge,
            Command: "WinMergeU",
            ArgsTemplate: "/u /e /wl /wr /ub /dl \"Local\" /dr \"Remote\" \"$LOCAL\" \"$REMOTE\" \"$MERGED\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\WinMerge\WinMergeU.exe",
                @"%ProgramFiles(x86)%\WinMerge\WinMergeU.exe"
            ]),

        // --- Meld (cross-platform; Windows installer ships meld.exe on PATH) ---
        new ExternalTool(
            Name: "meld",
            DisplayName: "Meld",
            Kind: ExternalToolKind.Diff,
            Command: "meld",
            ArgsTemplate: "\"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\Meld\Meld.exe",
                @"%ProgramFiles(x86)%\Meld\Meld.exe"
            ]),
        new ExternalTool(
            Name: "meld",
            DisplayName: "Meld",
            Kind: ExternalToolKind.Merge,
            Command: "meld",
            ArgsTemplate: "--auto-merge \"$LOCAL\" \"$BASE\" \"$REMOTE\" --output=\"$MERGED\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\Meld\Meld.exe",
                @"%ProgramFiles(x86)%\Meld\Meld.exe"
            ]),

        // --- Araxis Merge ---
        new ExternalTool(
            Name: "araxis",
            DisplayName: "Araxis Merge",
            Kind: ExternalToolKind.Diff,
            Command: "Compare",
            ArgsTemplate: "/wait /2 \"$LOCAL\" \"$REMOTE\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\Araxis\Araxis Merge\Compare.exe",
                @"%ProgramFiles(x86)%\Araxis\Araxis Merge\Compare.exe"
            ]),
        new ExternalTool(
            Name: "araxis",
            DisplayName: "Araxis Merge",
            Kind: ExternalToolKind.Merge,
            Command: "Compare",
            ArgsTemplate: "/wait /merge /a3 \"$BASE\" \"$LOCAL\" \"$REMOTE\" \"$MERGED\"",
            KnownInstallPaths: [
                @"%ProgramFiles%\Araxis\Araxis Merge\Compare.exe",
                @"%ProgramFiles(x86)%\Araxis\Araxis Merge\Compare.exe"
            ]),
    ];

    /// <summary>Diff-only presets.</summary>
    public static IEnumerable<ExternalTool> Diff =>
        All.Where(t => t.Kind == ExternalToolKind.Diff);

    /// <summary>Merge-only presets.</summary>
    public static IEnumerable<ExternalTool> Merge =>
        All.Where(t => t.Kind == ExternalToolKind.Merge);

    /// <summary>
    /// Look up the preset for a given tool name + kind. Returns null for
    /// unrecognised names (user-defined custom tool) or the built-in
    /// sentinel.
    /// </summary>
    public static ExternalTool? TryGet(string name, ExternalToolKind kind) =>
        All.FirstOrDefault(t => t.Kind == kind
            && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
