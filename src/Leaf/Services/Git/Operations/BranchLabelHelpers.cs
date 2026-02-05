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

        // Debug: Log what remotes we have at this SHA
        if (remoteBranches.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[LABELS] BuildBranchLabels for SHA {sha[..7]}:");
            System.Diagnostics.Debug.WriteLine($"[LABELS]   Local branches: {string.Join(", ", localBranches)}");
            System.Diagnostics.Debug.WriteLine($"[LABELS]   Remote branches: {string.Join(", ", remoteBranches.Select(r => $"{r.RemoteName}/{r.Name} ({r.RemoteType})"))}");
        }

        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Group remote branches by branch name (to consolidate multiple remotes)
        var remoteBranchesByName = remoteBranches
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Process local branches first - consolidate with ALL matching remotes
        foreach (var localName in localBranches)
        {
            var tipShaValue = branchNameToTipSha.GetValueOrDefault(localName);
            var label = new BranchLabel
            {
                Name = localName,
                IsLocal = true,
                IsCurrent = string.Equals(localName, currentBranchName, StringComparison.OrdinalIgnoreCase),
                TipSha = tipShaValue
            };

            // Add ALL matching remotes to this label
            if (remoteBranchesByName.TryGetValue(localName, out var matchingRemotes))
            {
                foreach (var remote in matchingRemotes)
                {
                    var fullRemoteName = $"{remote.RemoteName}/{remote.Name}";
                    label.Remotes.Add(new RemoteBranchInfo
                    {
                        RemoteName = remote.RemoteName,
                        RemoteType = remote.RemoteType,
                        TipSha = branchNameToTipSha.GetValueOrDefault(fullRemoteName)
                    });
                }
                System.Diagnostics.Debug.WriteLine($"[LABELS]   Created label '{localName}' with {label.Remotes.Count} remotes: {string.Join(", ", label.Remotes.Select(r => $"{r.RemoteName}({r.RemoteType})"))}");
            }

            labels.Add(label);
            processedNames.Add(localName);
        }

        // Process remote-only branches - consolidate all remotes with same branch name
        foreach (var group in remoteBranchesByName)
        {
            if (processedNames.Contains(group.Key))
                continue;

            var firstRemote = group.Value[0];
            var fullRemoteName = $"{firstRemote.RemoteName}/{firstRemote.Name}";

            var label = new BranchLabel
            {
                Name = group.Key,
                IsLocal = false,
                TipSha = branchNameToTipSha.GetValueOrDefault(fullRemoteName)
            };

            // Add ALL remotes to this label
            foreach (var remote in group.Value)
            {
                var remoteFullName = $"{remote.RemoteName}/{remote.Name}";
                label.Remotes.Add(new RemoteBranchInfo
                {
                    RemoteName = remote.RemoteName,
                    RemoteType = remote.RemoteType,
                    TipSha = branchNameToTipSha.GetValueOrDefault(remoteFullName)
                });
            }

            labels.Add(label);
            processedNames.Add(group.Key);
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
