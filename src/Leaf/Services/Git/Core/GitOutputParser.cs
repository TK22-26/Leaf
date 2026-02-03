namespace Leaf.Services.Git.Core;

/// <summary>
/// Parses git command output into structured data.
/// Stateless - safe to share across operations.
/// </summary>
internal class GitOutputParser : IGitOutputParser
{
    /// <inheritdoc />
    public string ExtractBranchFromStashMessage(string message)
    {
        // Stash messages typically have format: "WIP on branch: commit message" or "On branch: message"
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        const string wipPrefix = "WIP on ";
        const string onPrefix = "On ";

        string? branch = null;
        if (message.StartsWith(wipPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = message[wipPrefix.Length..];
            var colonIndex = afterPrefix.IndexOf(':');
            if (colonIndex > 0)
            {
                branch = afterPrefix[..colonIndex];
            }
        }
        else if (message.StartsWith(onPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = message[onPrefix.Length..];
            var colonIndex = afterPrefix.IndexOf(':');
            if (colonIndex > 0)
            {
                branch = afterPrefix[..colonIndex];
            }
        }

        return branch ?? string.Empty;
    }

    /// <inheritdoc />
    public List<string> ParseConflictFiles(string output)
    {
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();
    }

    /// <inheritdoc />
    public List<string> ParseConflictFilesFromPorcelain(string output)
    {
        var files = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3)
                continue;

            var status = line[..2];
            if (!status.Contains('U', StringComparison.Ordinal))
                continue;

            var path = line[3..].Trim();
            if (!string.IsNullOrEmpty(path))
            {
                files.Add(path);
            }
        }
        return files;
    }

    /// <inheritdoc />
    public bool IsShaLine(string line)
    {
        for (int i = 0; i < 40 && i < line.Length; i++)
        {
            if (!char.IsAsciiHexDigit(line[i]))
            {
                return false;
            }
        }

        return line.Length >= 40;
    }

    /// <inheritdoc />
    public string ParseMergingBranch(string mergeMsgContent)
    {
        if (string.IsNullOrEmpty(mergeMsgContent))
            return "Incoming";

        var msg = mergeMsgContent.Trim();
        // Common format: "Merge branch 'feature' into master"
        if (msg.StartsWith("Merge branch '") && msg.Contains('\''))
        {
            var parts = msg.Split('\'');
            if (parts.Length >= 2)
            {
                return parts[1];
            }
        }

        return "Incoming";
    }
}
