using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;

namespace MeshGenLite.Engine.Heuristics;

public sealed class ElectrodeBottomSampler
{
    public IReadOnlyList<ElectrodeSamplePoint> Sample(MeshData electrodeMesh, MeshData bodyMesh)
    {
        var underside = ElectrodeSampling.GetUndersideCap(electrodeMesh, bodyMesh);
        var samples = new List<ElectrodeSamplePoint>();
        
        var boundaryIds = FindBoundary(electrodeMesh, underside.VertexIds);
        
        foreach(var vertexId in underside.VertexIds)
        {
            var isBoundary = boundaryIds.Contains(vertexId);
            samples.Add(new ElectrodeSamplePoint
            {
                SampleId = vertexId,
                LocalPosition = electrodeMesh.Vertices[vertexId],
                WorldPosition = electrodeMesh.Vertices[vertexId],
                IsBoundary = isBoundary
            });
        }
        
        return samples;
    }
    
    private HashSet<int> FindBoundary(MeshData mesh, List<int> subset)
    {
        var topology = new MeshTopology(mesh);
        var subsetSet = subset.ToHashSet();
        var boundaries = new HashSet<int>();
        
        foreach(var id in subset)
        {
            var neighbors = topology.GetNeighbors(id);
            if (neighbors.Any(n => !subsetSet.Contains(n)))
            {
                boundaries.Add(id);
            }
        }
        return boundaries;
    }
}
