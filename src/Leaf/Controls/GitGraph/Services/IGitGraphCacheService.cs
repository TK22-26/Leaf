using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Controls.GitGraph.Services;

/// <summary>
/// Service for caching pens, geometries, and node lookups.
/// </summary>
public interface IGitGraphCacheService
{
    /// <summary>
    /// Gets a cached ellipse geometry at the origin for the given radius.
    /// </summary>
    EllipseGeometry GetCircleAtOrigin(double radius);

    /// <summary>
    /// Gets a cached rectangle geometry for the full area.
    /// </summary>
    RectangleGeometry GetFullArea(double width, double height);

    /// <summary>
    /// Gets a cached pen for the given brush with the specified width.
    /// </summary>
    Pen GetConnectionPen(Brush brush, double width = 2.0);

    /// <summary>
    /// Gets a dictionary mapping SHA to GitTreeNode, caching based on the nodes reference.
    /// The returned dictionary is a fresh copy owned by the caller.
    /// </summary>
    Dictionary<string, GitTreeNode> GetNodesBySha(IReadOnlyList<GitTreeNode> nodes);

    /// <summary>
    /// Clears the node lookup cache. Call when Nodes property changes.
    /// </summary>
    void ClearNodeCache();

    /// <summary>
    /// Clears all caches.
    /// </summary>
    void ClearAll();
}
