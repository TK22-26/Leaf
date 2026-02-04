using System.Windows;
using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Controls.GitGraph.Services;

/// <summary>
/// Implementation of <see cref="IGitGraphCacheService"/> for caching pens, geometries, and node lookups.
/// </summary>
public sealed class GitGraphCacheService : IGitGraphCacheService
{
    private readonly Dictionary<double, EllipseGeometry> _circlesByRadius = new();
    private RectangleGeometry? _fullAreaGeometry;
    private double _lastWidth, _lastHeight;

    private readonly Dictionary<Brush, Dictionary<double, Pen>> _penCache = new();
    private Dictionary<string, GitTreeNode>? _nodesByShaCache;
    private IReadOnlyList<GitTreeNode>? _cachedNodesReference;

    public EllipseGeometry GetCircleAtOrigin(double radius)
    {
        if (!_circlesByRadius.TryGetValue(radius, out var geom))
        {
            geom = new EllipseGeometry(new Point(0, 0), radius, radius);
            geom.Freeze();
            _circlesByRadius[radius] = geom;
        }
        return geom;
    }

    public RectangleGeometry GetFullArea(double width, double height)
    {
        if (_fullAreaGeometry == null || _lastWidth != width || _lastHeight != height)
        {
            _fullAreaGeometry = new RectangleGeometry(new Rect(0, 0, width, height));
            _fullAreaGeometry.Freeze();
            _lastWidth = width;
            _lastHeight = height;
        }
        return _fullAreaGeometry;
    }

    public Pen GetConnectionPen(Brush brush, double width = 2.0)
    {
        if (!_penCache.TryGetValue(brush, out var widthDict))
        {
            widthDict = new Dictionary<double, Pen>();
            _penCache[brush] = widthDict;
        }

        if (!widthDict.TryGetValue(width, out var pen))
        {
            pen = new Pen(brush, width);
            pen.Freeze();
            widthDict[width] = pen;
        }

        return pen;
    }

    public Dictionary<string, GitTreeNode> GetNodesBySha(IReadOnlyList<GitTreeNode> nodes)
    {
        // If nodes reference changed, rebuild the cache
        if (_nodesByShaCache == null || !ReferenceEquals(_cachedNodesReference, nodes))
        {
            _nodesByShaCache = new Dictionary<string, GitTreeNode>(nodes.Count);
            foreach (var node in nodes)
            {
                _nodesByShaCache[node.Sha] = node;
            }
            _cachedNodesReference = nodes;
        }

        // Return a copy to avoid shared mutable state
        return new Dictionary<string, GitTreeNode>(_nodesByShaCache);
    }

    public void ClearNodeCache()
    {
        _nodesByShaCache = null;
        _cachedNodesReference = null;
    }

    public void ClearAll()
    {
        _circlesByRadius.Clear();
        _fullAreaGeometry = null;
        _penCache.Clear();
        ClearNodeCache();
    }
}
