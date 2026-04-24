using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class ConstraintBuilder : IConstraintBuilder
{
    public IReadOnlyList<ConstraintPoint> Build(
        MeshData bodyMesh,
        MeshData alignedElectrodeMesh,
        ContactPatch contactPatch,
        IReadOnlyList<int> anchorVertexIds,
        ElasticEngineConfig config)
    {
        var constraints = new List<ConstraintPoint>();
        if (bodyMesh.Vertices.Count == 0 || contactPatch.VertexIds.Count == 0)
        {
            return constraints;
        }

        var topology = new MeshTopology(bodyMesh);
        var underside = ElectrodeSampling.GetUndersideCap(alignedElectrodeMesh, bodyMesh);
        var undersidePoints = underside.VertexIds.Count > 0
            ? underside.VertexIds.Select(id => alignedElectrodeMesh.Vertices[id]).ToArray()
            : alignedElectrodeMesh.Vertices.ToArray();
        var undersideTree = new KdTree(undersidePoints);
        var constrainedVertexIds = new HashSet<int>();
        var totalDisplacement = Vector3.Zero;

        foreach (var vertexId in contactPatch.VertexIds.Where(vertexId => vertexId >= 0 && vertexId < bodyMesh.Vertices.Count))
        {
            var current = bodyMesh.Vertices[vertexId];
            var target = FindNearestTarget(undersideTree, undersidePoints, current);
            totalDisplacement += target - current;
            constraints.Add(new ConstraintPoint
            {
                VertexId = vertexId,
                TargetPosition = target,
                Weight = config.HardConstraintWeight,
                IsHardConstraint = true
            });
            constrainedVertexIds.Add(vertexId);
        }

        var averageDisplacement = contactPatch.VertexIds.Count == 0
            ? Vector3.Zero
            : totalDisplacement / contactPatch.VertexIds.Count;

        var previousExpansion = new HashSet<int>(contactPatch.VertexIds);
        for (var ring = 1; ring <= Math.Max(config.SoftConstraintRings, 0); ring++)
        {
            var expanded = topology.ExpandRings(contactPatch.VertexIds, ring);
            var ringVertices = expanded.Except(previousExpansion).ToList();
            previousExpansion = expanded;

            var ringScale = (float)Math.Pow(config.SoftConstraintDecay, ring);
            var ringWeight = config.SoftConstraintWeight * ringScale;
            foreach (var vertexId in ringVertices)
            {
                if (!constrainedVertexIds.Add(vertexId))
                {
                    continue;
                }

                constraints.Add(new ConstraintPoint
                {
                    VertexId = vertexId,
                    TargetPosition = bodyMesh.Vertices[vertexId] + (averageDisplacement * ringScale),
                    Weight = ringWeight,
                    IsHardConstraint = false
                });
            }
        }

        foreach (var anchorId in SampleAnchors(anchorVertexIds, config.MaxAnchorCount).Where(anchorId => anchorId >= 0 && anchorId < bodyMesh.Vertices.Count))
        {
            if (!constrainedVertexIds.Add(anchorId))
            {
                continue;
            }

            constraints.Add(new ConstraintPoint
            {
                VertexId = anchorId,
                TargetPosition = bodyMesh.Vertices[anchorId],
                Weight = config.AnchorConstraintWeight,
                IsHardConstraint = true
            });
        }

        return constraints;
    }

    private static IEnumerable<int> SampleAnchors(IReadOnlyList<int> anchorVertexIds, int maxAnchorCount)
    {
        if (anchorVertexIds.Count <= maxAnchorCount || maxAnchorCount <= 0)
        {
            return anchorVertexIds;
        }

        var sampled = new List<int>(maxAnchorCount);
        var step = anchorVertexIds.Count / (double)maxAnchorCount;
        for (var sample = 0; sample < maxAnchorCount; sample++)
        {
            var index = (int)Math.Round(sample * step);
            index = Math.Clamp(index, 0, anchorVertexIds.Count - 1);
            sampled.Add(anchorVertexIds[index]);
        }

        return sampled.Distinct().ToList();
    }

    private static Vector3 FindNearestTarget(KdTree undersideTree, IReadOnlyList<Vector3> undersidePoints, Vector3 query)
    {
        var targetIndex = undersideTree.FindNearest(query);
        return targetIndex >= 0 ? undersidePoints[targetIndex] : query;
    }
}
