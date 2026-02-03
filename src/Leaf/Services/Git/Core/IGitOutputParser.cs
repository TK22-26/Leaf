namespace Leaf.Services.Git.Core;

/// <summary>
/// Interface for parsing git command output.
/// </summary>
internal interface IGitOutputParser
{
    /// <summary>
    /// Extract branch name from stash message (format: "WIP on branch: ..." or "On branch: ...").
    /// </summary>
    string ExtractBranchFromStashMessage(string message);

    /// <summary>
    /// Parse conflict files from git diff --name-only --diff-filter=U output.
    /// </summary>
    List<string> ParseConflictFiles(string output);

    /// <summary>
    /// Parse conflict files from git status --porcelain output.
    /// </summary>
    List<string> ParseConflictFilesFromPorcelain(string output);

    /// <summary>
    /// Check if a line starts with a valid SHA (40 hex chars).
    /// </summary>
    bool IsShaLine(string line);

    /// <summary>
    /// Parse merging branch from MERGE_MSG file content.
    /// </summary>
    string ParseMergingBranch(string mergeMsgContent);
}
