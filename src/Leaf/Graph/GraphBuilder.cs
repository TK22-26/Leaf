using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Graph;

/// <summary>
/// Builds the visual graph structure from commits.
/// Uses a simpler lane allocation that processes from oldest to newest.
/// </summary>
/// <remarks>
/// Instance-based and <b>immutable after construction</b> for its colour
/// resolver. Each repository gets its own builder; short-lived background
/// builds (pagination, tooltip previews) create their own builder bound
/// to the same resolver. Two guarantees fall out of this:
/// <list type="bullet">
/// <item>Concurrent builds cannot corrupt shared state (plan §1.2).</item>
/// <item>A displayed graph can never render against a differently-configured
/// resolver — the active builder, resolver, and nodes swap atomically in
/// the view model.</item>
/// </list>
///
/// <para>§5.14 split colour resolution out into
/// <see cref="Services.BranchColorService"/>. The builder now only owns
/// graph layout; colour for every node and every label is asked of the
/// injected <see cref="IBranchColorResolver"/>. Lane fallback for unnamed
/// lanes is the only colour-shaped responsibility that remains here, since
/// it depends on the lane index that the builder alone tracks.</para>
/// </remarks>
public class GraphBuilder
{
    /// <summary>
    /// Resolves colours for branch names. Owned externally — typically the
    /// per-repo <see cref="Services.IBranchColorService"/>. Held by reference
    /// because the resolver evolves over time (palette swaps, override
    /// changes) and the builder needs to read whatever is current at
    /// build time. Once a build completes, node colours are baked into
    /// <see cref="GitTreeNode.NodeColor"/>; callers re-paint by either
    /// (a) calling <see cref="RecolorNodes"/> or (b) doing a fresh build.
    /// </summary>
    private readonly IBranchColorResolver _colorResolver;

    // Fallback colors for lanes without a known branch name. Stays here
    // (rather than in the colour service) because the lane index that
    // selects from this set is a graph-layout concept the resolver
    // doesn't know about.
    private static readonly Brush[] FallbackBrushes;

    static GraphBuilder()
    {
        FallbackBrushes =
        [
            new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)), // Gray 1
            new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), // Gray 2
            new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)), // Gray 3
            new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)), // Gray 4
        ];
        foreach (var brush in FallbackBrushes)
            brush.Freeze();
    }

    /// <summary>
    /// Constructs a builder that delegates colour resolution to the given
    /// resolver. Pass <see cref="NullBranchColorResolver.Instance"/> when
    /// no repository is loaded yet — the design-time / initial state of
    /// <see cref="ViewModels.GitGraphViewModel"/>.
    /// </summary>
    public GraphBuilder(IBranchColorResolver colorResolver)
    {
        _colorResolver = colorResolver ?? throw new ArgumentNullException(nameof(colorResolver));
    }

    public int MaxLane { get; private set; }

    /// <summary>
    /// Re-paints already-built nodes against the current resolver, in place.
    /// Used after a colour-only change (palette switch, single-branch override)
    /// so the canvas can refresh without re-running the lane allocator.
    /// </summary>
    public void RecolorNodes(IReadOnlyList<GitTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.NodeColor = ResolveNodeColor(node.PrimaryBranch, node.ColumnIndex);
        }
    }

    private Brush ResolveNodeColor(string? branchName, int lane)
    {
        return !string.IsNullOrEmpty(branchName)
            ? _colorResolver.GetBranchColor(branchName)
            : FallbackBrushes[lane % FallbackBrushes.Length];
    }

    /// <summary>
    /// Build graph nodes from commits.
    /// Commits should be in reverse chronological order (newest first).
    /// </summary>
    public List<GitTreeNode> BuildGraph(IReadOnlyList<CommitInfo> commits, string? currentBranchName = null)
    {
        if (commits.Count == 0)
            return [];

        var nodes = new List<GitTreeNode>(commits.Count);
        var shaToNode = new Dictionary<string, GitTreeNode>(commits.Count);
        var commitsBySha = commits.ToDictionary(c => c.Sha);

        // Build branch data for GitKraken-style branch detection
        var branchData = BuildBranchData(commits, commitsBySha);

        // Extract main branch info for lane allocation (prefer current branch if known)
        // Use allAncestors for lane allocation so all main-branch commits get lane 0 preference
        var currentBranchData = !string.IsNullOrWhiteSpace(currentBranchName)
            ? branchData.FirstOrDefault(b => b.name.Equals(currentBranchName, StringComparison.OrdinalIgnoreCase))
            : default;
        var mainBranchData = currentBranchData.name != null
            ? currentBranchData
            : branchData.FirstOrDefault(b => GetBranchPriority(b.name) == 0);
        var mainBranchCommits = mainBranchData.allAncestors ?? new HashSet<string>();
        var mainBranchName = mainBranchData.name;

        // Track which lanes are active at each row
        // A lane is "active" if there's a line running through it
        var activeLanes = new List<string?>(); // SHA of commit that owns each lane, null = free

        // Track which branch name owns each lane (for consistent coloring)
        var laneBranchNames = new List<string?>();

        MaxLane = 0;

        // Process commits from newest (top) to oldest (bottom)
        // This is the display order - row 0 is at the top
        for (int row = 0; row < commits.Count; row++)
        {
            var commit = commits[row];

            // Find which lane this commit should be in
            int lane = FindLaneForCommit(commit, activeLanes, mainBranchCommits);

            // Ensure lane tracking lists are large enough
            while (laneBranchNames.Count <= lane)
                laneBranchNames.Add(null);

            // Determine branch name using GitKraken-style three-phase algorithm
            string? branchName = GetBranchForCommit(commit.Sha, branchData, commitsBySha);

            // Update lane's branch name if this commit has a definitive branch
            if (branchName != null)
                laneBranchNames[lane] = branchName;
            else if (commit.BranchNames.Count > 0)
            {
                // Fallback: use the first branch name from the commit
                branchName = commit.BranchNames[0];
                laneBranchNames[lane] = branchName;
            }
            else if (commit.BranchLabels.Count > 0)
            {
                // Remote-only branch tip: use BranchLabel name
                branchName = commit.BranchLabels[0].Name;
                laneBranchNames[lane] = branchName;
            }
            else
            {
                // Use the lane's current branch name
                branchName = laneBranchNames[lane];
            }

            // Determine the color based on the branch name
            var nodeColor = ResolveNodeColor(branchName, lane);

            // Create the node
            var node = new GitTreeNode
            {
                Sha = commit.Sha,
                RowIndex = row,
                ColumnIndex = lane,
                NodeColor = nodeColor,
                ParentShas = [.. commit.ParentShas],
                IsHead = commit.IsHead,
                BranchNames = [.. commit.BranchNames],
                BranchLabels = [.. commit.BranchLabels],
                TagNames = [.. commit.TagNames],
                PrimaryBranch = branchName ?? commit.BranchNames.FirstOrDefault(),
                IdenticonKey = commit.AvatarKey,
                IsStash = commit.IsStash,
                StashIndex = commit.StashIndex,
                // §5.8 carry the signature status onto the visual node so
                // GitGraphCanvas can render the verification badge without
                // crossing back to CommitInfo on every paint.
                SignatureStatus = commit.SignatureStatus,
                SignerName = commit.SignerName,
                SignerEmail = commit.SignerEmail,
                SignerKeyFingerprint = commit.SignerKeyFingerprint,
            };

            nodes.Add(node);
            shaToNode[commit.Sha] = node;

            // Update active lanes for the next row
            UpdateActiveLanes(commit, lane, activeLanes, laneBranchNames, mainBranchCommits, mainBranchName);

            MaxLane = Math.Max(MaxLane, lane);
        }

        // Resolve parent connections (for drawing lines)
        foreach (var node in nodes)
        {
            foreach (var parentSha in node.ParentShas)
            {
                if (shaToNode.TryGetValue(parentSha, out var parentNode))
                {
                    node.ParentColumns.Add(parentNode.ColumnIndex);
                    node.ParentRows.Add(parentNode.RowIndex);
                }
            }
        }

        return nodes;
    }

    #region Branch Detection (GitKraken-style two-phase algorithm)

    /// <summary>
    /// Gets branch priority for sorting (lower = higher priority).
    /// main/master = 0, develop = 1, others = 2+
    /// </summary>
    private static int GetBranchPriority(string branchName)
    {
        if (branchName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            branchName.Equals("master", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (branchName.Equals("develop", StringComparison.OrdinalIgnoreCase) ||
            branchName.Equals("development", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    /// <summary>
    /// Gets the first-parent lineage for a branch starting from its tip.
    /// Returns a set of all commit SHAs on this direct path.
    /// </summary>
    private static HashSet<string> GetFirstParentSet(string? tipSha, Dictionary<string, CommitInfo> commitsBySha)
    {
        var result = new HashSet<string>();
        if (tipSha == null || !commitsBySha.TryGetValue(tipSha, out var current))
            return result;

        while (current != null)
        {
            result.Add(current.Sha);
            if (current.ParentShas.Count > 0 && commitsBySha.TryGetValue(current.ParentShas[0], out var parent))
                current = parent;
            else
                current = null;
        }

        return result;
    }

    /// <summary>
    /// Gets all ancestors of a branch tip (all commits reachable from tip).
    /// </summary>
    private static HashSet<string> GetAllAncestors(string? tipSha, Dictionary<string, CommitInfo> commitsBySha)
    {
        var result = new HashSet<string>();
        if (tipSha == null)
            return result;

        var queue = new Queue<string>();
        queue.Enqueue(tipSha);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (result.Contains(current))
                continue;

            result.Add(current);

            if (commitsBySha.TryGetValue(current, out var commit))
            {
                foreach (var parentSha in commit.ParentShas)
                {
                    if (!result.Contains(parentSha))
                        queue.Enqueue(parentSha);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the owning branch for a commit using the GitKraken-style three-phase algorithm:
    /// Phase 1: Check first-parent lineages (commit is on the mainline)
    /// Phase 2: Check if parent is on a first-parent lineage (feature branch commits)
    /// Phase 3: Check ancestry with branch priority (fallback for deeply merged commits)
    /// </summary>
    private static string? GetBranchForCommit(
        string sha,
        List<(string name, HashSet<string> firstParentSet, HashSet<string> allAncestors)> branches,
        Dictionary<string, CommitInfo> commitsBySha)
    {
        // Phase 1: Check first-parent lineages (highest priority)
        // These commits are definitively on that branch's mainline
        foreach (var (branchName, firstParentSet, _) in branches)
        {
            if (firstParentSet.Contains(sha))
                return branchName;
        }

        // Phase 2: Trace first-parent chain until we find a commit on a branch's first-parent lineage
        // This captures "feature branch commits" - commits that branched off a mainline
        // We follow the first-parent path back to find which branch this commit originated from
        var currentSha = sha;
        var visited = new HashSet<string>();
        while (commitsBySha.TryGetValue(currentSha, out var commit) && commit.ParentShas.Count > 0)
        {
            if (!visited.Add(currentSha))
                break; // Avoid infinite loops

            var firstParentSha = commit.ParentShas[0];

            // Check if this first-parent is on any branch's first-parent lineage
            // Process in priority order (main > develop > others)
            foreach (var (branchName, firstParentSet, _) in branches)
            {
                if (firstParentSet.Contains(firstParentSha))
                    return branchName;
            }

            // Continue tracing back
            currentSha = firstParentSha;
        }

        // Phase 3: Check ancestry (for deeply merged commits)
        // Process in priority order (main > develop > others)
        foreach (var (branchName, _, allAncestors) in branches)
        {
            if (allAncestors.Contains(sha))
                return branchName;
        }

        return null;
    }

    /// <summary>
    /// Builds branch data structures sorted by priority for the three-phase algorithm.
    /// Pre-computes first-parent sets and all-ancestor sets for efficient lookup.
    /// </summary>
    private static List<(string name, HashSet<string> firstParentSet, HashSet<string> allAncestors)> BuildBranchData(
        IReadOnlyList<CommitInfo> commits,
        Dictionary<string, CommitInfo> commitsBySha)
    {
        var branchData = new List<(string name, HashSet<string> firstParentSet, HashSet<string> allAncestors)>();
        var processedBranches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find all branch tips from commits
        foreach (var commit in commits)
        {
            foreach (var branchName in commit.BranchNames)
            {
                if (processedBranches.Contains(branchName))
                    continue;

                processedBranches.Add(branchName);
                var firstParentSet = GetFirstParentSet(commit.Sha, commitsBySha);
                var allAncestors = GetAllAncestors(commit.Sha, commitsBySha);
                branchData.Add((branchName, firstParentSet, allAncestors));
            }

            // Also include remote-only branches from BranchLabels
            foreach (var label in commit.BranchLabels)
            {
                if (processedBranches.Contains(label.Name))
                    continue;

                processedBranches.Add(label.Name);
                var firstParentSet = GetFirstParentSet(commit.Sha, commitsBySha);
                var allAncestors = GetAllAncestors(commit.Sha, commitsBySha);
                branchData.Add((label.Name, firstParentSet, allAncestors));
            }
        }

        // Sort by priority (main/master first, then develop, then others alphabetically)
        branchData.Sort((a, b) =>
        {
            var priorityA = GetBranchPriority(a.name);
            var priorityB = GetBranchPriority(b.name);
            if (priorityA != priorityB)
                return priorityA.CompareTo(priorityB);
            return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        });

        return branchData;
    }

    #endregion

    private int FindLaneForCommit(CommitInfo commit, List<string?> activeLanes, HashSet<string> mainBranchCommits)
    {
        // Check if any active lane is expecting this commit (i.e., this commit is a parent of a previous commit)
        int foundLane = -1;
        for (int i = 0; i < activeLanes.Count; i++)
        {
            if (activeLanes[i] == commit.Sha)
            {
                if (foundLane == -1)
                {
                    // First lane expecting this commit - use it
                    foundLane = i;
                }
                else
                {
                    // Additional lanes expecting this commit (convergence point)
                    // Free them so they can be reused
                    activeLanes[i] = null;
                }
            }
        }

        if (foundLane != -1)
        {
            // Prefer lane 0 for main branch commits even if another lane expects it
            if (mainBranchCommits.Contains(commit.Sha) && (activeLanes.Count == 0 || activeLanes[0] == null))
            {
                activeLanes[foundLane] = null;
                EnsureLaneExists(activeLanes, 0);
                return 0;
            }
            return foundLane;
        }

        // No lane is expecting us - find a free lane or create a new one
        // Prefer lane 0 for commits on the main branch lineage
        bool isMainBranchCommit = mainBranchCommits.Contains(commit.Sha);
        if (isMainBranchCommit)
        {
            if (activeLanes.Count == 0 || activeLanes[0] == null)
            {
                EnsureLaneExists(activeLanes, 0);
                return 0;
            }
        }

        // Find first free lane
        int startLane = isMainBranchCommit ? 0 : 1;
        for (int i = startLane; i < activeLanes.Count; i++)
        {
            if (activeLanes[i] == null)
            {
                return i;
            }
        }

        // No free lane, create new one
        if (activeLanes.Count == 0 && !isMainBranchCommit)
        {
            EnsureLaneExists(activeLanes, 1);
            return 1;
        }
        activeLanes.Add(null);
        return activeLanes.Count - 1;
    }

    private void UpdateActiveLanes(CommitInfo commit, int commitLane, List<string?> activeLanes, List<string?> laneBranchNames, HashSet<string> mainBranchCommits, string? mainBranchName)
    {
        EnsureLaneExists(activeLanes, commitLane);
        while (laneBranchNames.Count <= commitLane)
            laneBranchNames.Add(null);

        // This commit's lane now points to its first parent (if any)
        if (commit.ParentShas.Count > 0)
        {
            // First parent continues in the same lane (branch name stays)
            activeLanes[commitLane] = commit.ParentShas[0];

            // Additional parents need their own lanes (merge)
            for (int i = 1; i < commit.ParentShas.Count; i++)
            {
                var parentSha = commit.ParentShas[i];

                // Check if this parent already has a lane
                bool found = false;
                for (int j = 0; j < activeLanes.Count; j++)
                {
                    if (activeLanes[j] == parentSha)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // If this parent is on the main branch, prefer lane 0
                    int freeLane = -1;
                    bool parentIsMain = mainBranchCommits.Contains(parentSha);
                    if (parentIsMain && (activeLanes.Count == 0 || activeLanes[0] == null))
                    {
                        EnsureLaneExists(activeLanes, 0);
                        freeLane = 0;
                    }
                    else
                    {
                        // Find a free lane for this parent
                        int startLane = parentIsMain ? 0 : 1;
                        for (int j = startLane; j < activeLanes.Count; j++)
                        {
                            if (activeLanes[j] == null)
                            {
                                freeLane = j;
                                break;
                            }
                        }

                        if (freeLane == -1)
                        {
                            if (activeLanes.Count == 0 && !parentIsMain)
                            {
                                EnsureLaneExists(activeLanes, 1);
                                freeLane = 1;
                            }
                            else
                            {
                                freeLane = activeLanes.Count;
                                activeLanes.Add(null);
                                laneBranchNames.Add(null);
                            }
                        }
                    }

                    activeLanes[freeLane] = parentSha;

                    // If this parent is on main branch, set the lane's branch name
                    if (mainBranchCommits.Contains(parentSha))
                    {
                        while (laneBranchNames.Count <= freeLane)
                            laneBranchNames.Add(null);
                        laneBranchNames[freeLane] = mainBranchName;
                    }
                }
            }
        }
        else
        {
            // No parents - this lane ends here (root commit)
            activeLanes[commitLane] = null;
            laneBranchNames[commitLane] = null; // Clear branch name for reuse
        }
    }

    private void EnsureLaneExists(List<string?> activeLanes, int lane)
    {
        while (activeLanes.Count <= lane)
        {
            activeLanes.Add(null);
        }
    }
}

/// <summary>
/// No-op resolver used as the initial state of <see cref="ViewModels.GitGraphViewModel"/>
/// before a repository is loaded. Returns a single neutral grey for any
/// branch — keeps design-time and the brief "no repo selected" window
/// from crashing on a null resolver.
/// </summary>
public sealed class NullBranchColorResolver : IBranchColorResolver
{
    public static readonly NullBranchColorResolver Instance = new();

    private static readonly Brush NeutralBrush;

    static NullBranchColorResolver()
    {
        NeutralBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        NeutralBrush.Freeze();
    }

    private NullBranchColorResolver() { }

    public Brush GetBranchColor(string branchName) => NeutralBrush;
}
