using Leaf.Models;
using LibGit2Sharp;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// Helper methods for building branch labels in commit history.
/// Extracted from CommitHistoryOperations to keep file size under 500 lines.
/// </summary>
internal static class BranchLabelHelpers
{
    public record RemoteBranchRef(string Name, string RemoteName, RemoteType RemoteType);

    public static string GetBranchNameWithoutRemote(string fullName)
    {
        var slashIndex = fullName.IndexOf('/');
        return slashIndex >= 0 ? fullName[(slashIndex + 1)..] : fullName;
    }

    public static string? FindNearestVisibleAncestor(Repository repo, string tipSha, HashSet<string> visibleShas)
    {
        var start = repo.Lookup<Commit>(tipSha);
        if (start == null) return null;

        var queue = new Queue<Commit>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current.Sha)) continue;

            if (visibleShas.Contains(current.Sha))
                return current.Sha;

            foreach (var parent in current.Parents)
            {
                if (!visited.Contains(parent.Sha))
                    queue.Enqueue(parent);
            }
        }

        return null;
    }

    public static void AddBranchLabels(CommitInfo commit, List<BranchLabel> labels)
    {
        foreach (var label in labels)
        {
            if (!commit.BranchLabels.Any(existing =>
                    string.Equals(existing.FullName, label.FullName, StringComparison.OrdinalIgnoreCase)))
            {
                commit.BranchLabels.Add(label);
            }

            if (label.IsLocal && !commit.BranchNames.Any(name =>
                    string.Equals(name, label.Name, StringComparison.OrdinalIgnoreCase)))
            {
                commit.BranchNames.Add(label.Name);
            }
        }
    }

    public static List<BranchLabel> BuildBranchLabels(
        string sha,
        Dictionary<string, List<string>> localBranchTips,
        Dictionary<string, List<RemoteBranchRef>> remoteBranchTips,
        Dictionary<string, string> branchNameToTipSha,
        string? currentBranchName)
    {
        var labels = new List<BranchLabel>();
        var localBranches = localBranchTips.TryGetValue(sha, out var locals) ? locals : [];
        var remoteBranches = remoteBranchTips.TryGetValue(sha, out var remotes) ? remotes : [];

        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var localName in localBranches)
        {
            var matchingRemote = remoteBranches.FirstOrDefault(r =>
                string.Equals(r.Name, localName, StringComparison.OrdinalIgnoreCase));

            var tipShaValue = branchNameToTipSha.GetValueOrDefault(localName);
            System.Diagnostics.Debug.WriteLine($"[LABEL] BuildBranchLabels: Creating local label '{localName}' at sha={sha}, TipSha={tipShaValue ?? "NULL"}");
            labels.Add(new BranchLabel
            {
                Name = localName,
                IsLocal = true,
                IsRemote = matchingRemote != null,
                RemoteName = matchingRemote?.RemoteName,
                RemoteType = matchingRemote?.RemoteType ?? RemoteType.Other,
                IsCurrent = string.Equals(localName, currentBranchName, StringComparison.OrdinalIgnoreCase),
                TipSha = tipShaValue
            });
            processedNames.Add(localName);
        }

        foreach (var remote in remoteBranches)
        {
            if (!processedNames.Contains(remote.Name))
            {
                var fullRemoteName = $"{remote.RemoteName}/{remote.Name}";
                labels.Add(new BranchLabel
                {
                    Name = remote.Name,
                    IsLocal = false,
                    IsRemote = true,
                    RemoteName = remote.RemoteName,
                    RemoteType = remote.RemoteType,
                    TipSha = branchNameToTipSha.GetValueOrDefault(fullRemoteName)
                });
            }
        }

        labels.Sort((a, b) =>
        {
            if (a.IsCurrent && !b.IsCurrent) return -1;
            if (!a.IsCurrent && b.IsCurrent) return 1;
            return 0;
        });

        return labels;
    }
}
