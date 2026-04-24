using System.Numerics;

namespace MeshGenLite.Engine.Geometry;

/// <summary>
/// A simple 3D KD-tree for nearest-neighbor queries on point sets.
/// Used by <see cref="ContactPatchExtractor"/> to find body-surface vertices
/// that are close to the electrode underside.
/// </summary>
public sealed class KdTree
{
    private readonly KdNode? root;
    private readonly Vector3[] points;

    public KdTree(IReadOnlyList<Vector3> points)
    {
        this.points = points.ToArray();
        var indices = Enumerable.Range(0, this.points.Length).ToArray();
        root = Build(indices, 0);
    }

    /// <summary>
    /// Finds the index of the nearest point to the given query point.
    /// </summary>
    public int FindNearest(Vector3 query)
    {
        if (root is null)
        {
            return -1;
        }

        var bestIndex = -1;
        var bestDistSq = double.MaxValue;
        SearchNearest(root, query, 0, ref bestIndex, ref bestDistSq);
        return bestIndex;
    }

    /// <summary>
    /// Finds all point indices within the given radius of the query point.
    /// </summary>
    public List<int> FindWithinRadius(Vector3 query, double radius)
    {
        var result = new List<int>();
        if (root is null)
        {
            return result;
        }

        var radiusSq = radius * radius;
        SearchRadius(root, query, 0, radiusSq, result);
        return result;
    }

    private KdNode? Build(int[] indices, int depth)
    {
        if (indices.Length == 0)
        {
            return null;
        }

        var axis = depth % 3;
        Array.Sort(indices, (a, b) => GetAxis(points[a], axis).CompareTo(GetAxis(points[b], axis)));

        var median = indices.Length / 2;
        return new KdNode
        {
            PointIndex = indices[median],
            Left = Build(indices[..median], depth + 1),
            Right = Build(indices[(median + 1)..], depth + 1)
        };
    }

    private void SearchNearest(KdNode node, Vector3 query, int depth, ref int bestIndex, ref double bestDistSq)
    {
        var point = points[node.PointIndex];
        var distSq = DistanceSquared(query, point);
        if (distSq < bestDistSq)
        {
            bestDistSq = distSq;
            bestIndex = node.PointIndex;
        }

        var axis = depth % 3;
        var diff = GetAxis(query, axis) - GetAxis(point, axis);
        var diffSq = diff * diff;

        var first = diff < 0 ? node.Left : node.Right;
        var second = diff < 0 ? node.Right : node.Left;

        if (first is not null)
        {
            SearchNearest(first, query, depth + 1, ref bestIndex, ref bestDistSq);
        }

        if (second is not null && diffSq < bestDistSq)
        {
            SearchNearest(second, query, depth + 1, ref bestIndex, ref bestDistSq);
        }
    }

    private void SearchRadius(KdNode node, Vector3 query, int depth, double radiusSq, List<int> result)
    {
        var point = points[node.PointIndex];
        var distSq = DistanceSquared(query, point);
        if (distSq <= radiusSq)
        {
            result.Add(node.PointIndex);
        }

        var axis = depth % 3;
        var diff = GetAxis(query, axis) - GetAxis(point, axis);
        var diffSq = diff * diff;

        var first = diff < 0 ? node.Left : node.Right;
        var second = diff < 0 ? node.Right : node.Left;

        if (first is not null)
        {
            SearchRadius(first, query, depth + 1, radiusSq, result);
        }

        if (second is not null && diffSq <= radiusSq)
        {
            SearchRadius(second, query, depth + 1, radiusSq, result);
        }
    }

    private static double GetAxis(Vector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z
    };

    private static double DistanceSquared(Vector3 a, Vector3 b)
    {
        var dx = (double)a.X - b.X;
        var dy = (double)a.Y - b.Y;
        var dz = (double)a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private sealed class KdNode
    {
        public int PointIndex { get; init; }
        public KdNode? Left { get; init; }
        public KdNode? Right { get; init; }
    }
}
