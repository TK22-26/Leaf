using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Graph;

/// <summary>
/// Allocates lanes (columns) for commits in the Git graph visualization.
/// Uses a "Lane Reservoir" algorithm:
/// - Assign lane index (0, 1, 2...) to each active branch tip
/// - On merge: allocate connecting lanes
/// - On branch end: release lane back to reservoir
///
/// Main Branch Rule: main/master/develop are forced to Lane 0/1 for the "highway" effect.
/// </summary>
public class LaneAllocator
{
    private readonly Dictionary<string, int> _activeLanes = new();
    private readonly Queue<int> _availableLanes = new();
    private readonly HashSet<string> _mainBranchNames = ["main", "master", "develop"];
    private int _maxLane = -1;

    /// <summary>
    /// Branch colors for lane assignment.
    /// </summary>
    private static readonly Brush[] BranchBrushes =
    [
        new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), // Blue - main
        new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)), // Green - develop
        new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)), // Orange
        new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)), // Red
        new SolidColorBrush(Color.FromRgb(0x88, 0x17, 0x98)), // Purple
        new SolidColorBrush(Color.FromRgb(0x00, 0xB7, 0xC3)), // Teal
        new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00)), // Yellow
        new SolidColorBrush(Color.FromRgb(0xE7, 0x48, 0x56)), // Pink/Coral
        new SolidColorBrush(Color.FromRgb(0x00, 0xCC, 0x6A)), // Light Green
        new SolidColorBrush(Color.FromRgb(0x87, 0x64, 0xB8)), // Light Purple
        new SolidColorBrush(Color.FromRgb(0x03, 0x83, 0x87)), // Dark Teal
        new SolidColorBrush(Color.FromRgb(0xCA, 0x50, 0x10)), // Rust
    ];

    static LaneAllocator()
    {
        // Freeze brushes for thread safety
        foreach (var brush in BranchBrushes)
        {
            brush.Freeze();
        }
    }

    /// <summary>
    /// Reset the allocator for a new graph.
    /// </summary>
    public void Reset()
    {
        _activeLanes.Clear();
        _availableLanes.Clear();
        _maxLane = -1;
    }

    /// <summary>
    /// Get or allocate a lane for a commit.
    /// </summary>
    /// <param name="sha">Commit SHA</param>
    /// <param name="branchName">Primary branch name (for main branch rule)</param>
    /// <param name="parentShas">Parent commit SHAs</param>
    /// <returns>Allocated lane index</returns>
    public int AllocateLane(string sha, string? branchName, IReadOnlyList<string> parentShas)
    {
        // Check if we already have a lane for this commit
        if (_activeLanes.TryGetValue(sha, out int existingLane))
        {
            return existingLane;
        }

        // Main branch rule: force main/master/develop to lane 0
        if (branchName != null && _mainBranchNames.Contains(branchName.ToLowerInvariant()))
        {
            if (!_activeLanes.ContainsValue(0))
            {
                _activeLanes[sha] = 0;
                _maxLane = Math.Max(_maxLane, 0);
                return 0;
            }
        }

        // Get next available lane
        int lane = GetNextAvailableLane();
        _activeLanes[sha] = lane;

        return lane;
    }

    /// <summary>
    /// Continue a lane from parent to child commit.
    /// </summary>
    public int ContinueLane(string childSha, string parentSha)
    {
        if (_activeLanes.TryGetValue(parentSha, out int parentLane))
        {
            // Release parent's lane claim
            _activeLanes.Remove(parentSha);

            // Child takes over the lane
            _activeLanes[childSha] = parentLane;
            return parentLane;
        }

        // Parent not found, allocate new lane
        return AllocateLane(childSha, null, []);
    }

    /// <summary>
    /// Release a lane when a branch ends (no more children).
    /// </summary>
    public void ReleaseLane(string sha)
    {
        if (_activeLanes.TryGetValue(sha, out int lane))
        {
            _activeLanes.Remove(sha);
            _availableLanes.Enqueue(lane);
        }
    }

    /// <summary>
    /// Reserve lanes for merge connections.
    /// </summary>
    public void ReserveMergeLanes(string mergeSha, IReadOnlyList<string> parentShas)
    {
        if (parentShas.Count <= 1) return;

        // Ensure all parents have lanes allocated
        foreach (var parentSha in parentShas)
        {
            if (!_activeLanes.ContainsKey(parentSha))
            {
                int lane = GetNextAvailableLane();
                _activeLanes[parentSha] = lane;
            }
        }
    }

    /// <summary>
    /// Get the lane for a commit (if already allocated).
    /// </summary>
    public int? GetLane(string sha)
    {
        return _activeLanes.TryGetValue(sha, out int lane) ? lane : null;
    }

    /// <summary>
    /// Get brush color for a lane.
    /// </summary>
    public static Brush GetBrushForLane(int lane)
    {
        return BranchBrushes[lane % BranchBrushes.Length];
    }

    /// <summary>
    /// Get brush color based on branch name hash.
    /// </summary>
    public static Brush GetBrushForBranch(string? branchName)
    {
        if (string.IsNullOrEmpty(branchName))
            return BranchBrushes[0];

        // Main branches get specific colors
        var lowerName = branchName.ToLowerInvariant();
        if (lowerName is "main" or "master")
            return BranchBrushes[0]; // Blue
        if (lowerName == "develop")
            return BranchBrushes[1]; // Green

        // Hash other branch names for consistent colors
        int hash = Math.Abs(branchName.GetHashCode());
        return BranchBrushes[(hash % (BranchBrushes.Length - 2)) + 2];
    }

    /// <summary>
    /// Current number of active lanes.
    /// </summary>
    public int ActiveLaneCount => _activeLanes.Count;

    /// <summary>
    /// Maximum lane index used.
    /// </summary>
    public int MaxLane => _maxLane;

    private int GetNextAvailableLane()
    {
        if (_availableLanes.Count > 0)
        {
            return _availableLanes.Dequeue();
        }

        _maxLane++;
        return _maxLane;
    }
}
