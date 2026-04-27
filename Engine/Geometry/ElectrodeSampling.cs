using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using MeshGenLite.Models;

namespace MeshGenLite.Engine.Geometry;

internal sealed class ElectrodeCapSample
{
    public List<int> VertexIds { get; init; } = new();

    public Vector3 Centroid { get; init; }

    public Vector3 CapNormal { get; init; }
}

internal static class ElectrodeSampling
{
    public static ElectrodeCapSample GetUndersideCap(MeshData electrodeMesh, MeshData bodyMesh)
    {
        return GetUndersideCap(electrodeMesh, bodyMesh, new KdTree(bodyMesh.Vertices));
    }

    public static ElectrodeCapSample GetUndersideCap(MeshData electrodeMesh, MeshData bodyMesh, KdTree bodyTree)
    {
        if (electrodeMesh.Vertices.Count == 0)
        {
            return new ElectrodeCapSample
            {
                VertexIds = new List<int>(),
                Centroid = Vector3.Zero,
                CapNormal = Vector3.UnitZ
            };
        }

        var centroid = ComputeCentroid(electrodeMesh.Vertices);
        var axis = EstimateMinimalVarianceAxis(electrodeMesh.Vertices, centroid);
        var projections = electrodeMesh.Vertices
            .Select(vertex => Vector3.Dot(vertex - centroid, axis))
            .ToArray();

        var minProjection = projections.Min();
        var maxProjection = projections.Max();
        var span = Math.Max(maxProjection - minProjection, 1e-3f);
        var tolerance = Math.Max(span * 0.12f, 0.35f);

        var minIds = new List<int>();
        var maxIds = new List<int>();
        for (var index = 0; index < projections.Length; index++)
        {
            if (projections[index] <= minProjection + tolerance)
            {
                minIds.Add(index);
            }

            if (projections[index] >= maxProjection - tolerance)
            {
                maxIds.Add(index);
            }
        }

        if (minIds.Count == 0)
        {
            minIds.Add(Array.IndexOf(projections, minProjection));
        }

        if (maxIds.Count == 0)
        {
            maxIds.Add(Array.IndexOf(projections, maxProjection));
        }

        var minCentroid = ComputeCentroid(minIds.Select(id => electrodeMesh.Vertices[id]));
        var maxCentroid = ComputeCentroid(maxIds.Select(id => electrodeMesh.Vertices[id]));
        
        var minDistance = DistanceToClosest(bodyTree, bodyMesh, minCentroid);
        var maxDistance = DistanceToClosest(bodyTree, bodyMesh, maxCentroid);

        return minDistance <= maxDistance
            ? new ElectrodeCapSample
            {
                VertexIds = minIds,
                Centroid = minCentroid,
                CapNormal = -axis
            }
            : new ElectrodeCapSample
            {
                VertexIds = maxIds,
                Centroid = maxCentroid,
                CapNormal = axis
            };
    }

    private static Vector3 EstimateMinimalVarianceAxis(IReadOnlyList<Vector3> vertices, Vector3 centroid)
    {
        var covariance = DenseMatrix.Create(3, 3, 0.0);
        foreach (var vertex in vertices)
        {
            var offset = vertex - centroid;
            covariance[0, 0] += offset.X * offset.X;
            covariance[0, 1] += offset.X * offset.Y;
            covariance[0, 2] += offset.X * offset.Z;
            covariance[1, 0] += offset.Y * offset.X;
            covariance[1, 1] += offset.Y * offset.Y;
            covariance[1, 2] += offset.Y * offset.Z;
            covariance[2, 0] += offset.Z * offset.X;
            covariance[2, 1] += offset.Z * offset.Y;
            covariance[2, 2] += offset.Z * offset.Z;
        }

        covariance = (DenseMatrix)(covariance / Math.Max(vertices.Count, 1));
        var evd = covariance.Evd(Symmetricity.Symmetric);
        var minIndex = 0;
        var minValue = double.MaxValue;
        for (var index = 0; index < evd.EigenValues.Count; index++)
        {
            var value = evd.EigenValues[index].Real;
            if (value < minValue)
            {
                minValue = value;
                minIndex = index;
            }
        }

        var eigenVector = evd.EigenVectors.Column(minIndex);
        var axis = new Vector3((float)eigenVector[0], (float)eigenVector[1], (float)eigenVector[2]);
        if (axis.LengthSquared() <= 1e-8f)
        {
            return Vector3.UnitZ;
        }

        return Vector3.Normalize(axis);
    }

    private static Vector3 ComputeCentroid(IEnumerable<Vector3> vertices)
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var vertex in vertices)
        {
            sum += vertex;
            count++;
        }

        return count == 0 ? Vector3.Zero : sum / count;
    }

    private static double DistanceToClosest(KdTree bodyTree, MeshData bodyMesh, Vector3 query)
    {
        var nearestId = bodyTree.FindNearest(query);
        if (nearestId < 0)
        {
            return double.MaxValue;
        }

        return Vector3.Distance(bodyMesh.Vertices[nearestId], query);
    }
}
