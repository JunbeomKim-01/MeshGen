using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class ContactPatchExtractor : IContactPatchExtractor
{
    public ContactPatch Extract(MeshData bodyMesh, MeshData electrodeMesh, ContactPatchOptions options)
    {
        var patch = new ContactPatch();
        if (bodyMesh.Vertices.Count == 0 || electrodeMesh.Vertices.Count == 0)
        {
            return patch;
        }

        var topology = new MeshTopology(bodyMesh);
        var bodyTree = new KdTree(bodyMesh.Vertices);
        var underside = ElectrodeSampling.GetUndersideCap(electrodeMesh, bodyMesh);
        var initial = new HashSet<int>();

        foreach (var vertexId in underside.VertexIds)
        {
            var electrodeVertex = electrodeMesh.Vertices[vertexId];
            var nearestId = bodyTree.FindNearest(electrodeVertex);
            if (nearestId < 0)
            {
                continue;
            }

            var distance = Vector3.Distance(bodyMesh.Vertices[nearestId], electrodeVertex);
            if (distance <= options.DistanceThresholdMm)
            {
                initial.Add(nearestId);
            }
        }

        if (initial.Count == 0)
        {
            var fallbackId = bodyTree.FindNearest(underside.Centroid);
            if (fallbackId >= 0)
            {
                initial.Add(fallbackId);
            }
        }

        var expanded = topology.ExpandRings(initial, Math.Max(options.ExpansionRings, 0));
        patch.VertexIds.AddRange(expanded.OrderBy(id => id));

        foreach (var vertexId in expanded)
        {
            if (topology.GetNeighbors(vertexId).Any(neighbor => !expanded.Contains(neighbor)))
            {
                patch.BoundaryVertexIds.Add(vertexId);
            }
        }

        return patch;
    }
}
