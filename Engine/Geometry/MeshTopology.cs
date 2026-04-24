using MeshGenLite.Models;

namespace MeshGenLite.Engine.Geometry;

/// <summary>
/// Builds and queries mesh adjacency information.
/// Provides vertex-to-face, vertex-to-vertex (1-ring neighbor), and
/// n-ring expansion utilities needed by constraint building and deformation.
/// </summary>
public sealed class MeshTopology
{
    private readonly Dictionary<int, List<int>> vertexToFaces;
    private readonly Dictionary<int, HashSet<int>> vertexNeighbors;

    public MeshTopology(MeshData mesh)
    {
        vertexToFaces = BuildVertexToFaces(mesh);
        vertexNeighbors = BuildVertexNeighbors(mesh);
    }

    /// <summary>
    /// Returns the face (triangle) indices that share the given vertex.
    /// </summary>
    public IReadOnlyList<int> GetFaces(int vertexId)
    {
        return vertexToFaces.TryGetValue(vertexId, out var faces)
            ? faces
            : Array.Empty<int>();
    }

    /// <summary>
    /// Returns the 1-ring neighbor vertex indices of the given vertex.
    /// </summary>
    public IReadOnlyCollection<int> GetNeighbors(int vertexId)
    {
        return vertexNeighbors.TryGetValue(vertexId, out var neighbors)
            ? neighbors
            : (IReadOnlyCollection<int>)Array.Empty<int>();
    }

    /// <summary>
    /// Expands a set of vertex indices by the given number of rings.
    /// Ring 0 returns the input set unchanged.
    /// </summary>
    public HashSet<int> ExpandRings(IEnumerable<int> seedVertices, int rings)
    {
        var current = new HashSet<int>(seedVertices);
        for (var ring = 0; ring < rings; ring++)
        {
            var expanded = new HashSet<int>(current);
            foreach (var vertex in current)
            {
                foreach (var neighbor in GetNeighbors(vertex))
                {
                    expanded.Add(neighbor);
                }
            }

            current = expanded;
        }

        return current;
    }

    /// <summary>
    /// Returns vertex indices that are at least <paramref name="minRings"/> rings
    /// away from any of the given seed vertices. Useful for selecting fixed anchors.
    /// </summary>
    public List<int> GetDistantVertices(IEnumerable<int> seedVertices, int totalVertices, int minRings)
    {
        var nearSet = ExpandRings(seedVertices, minRings);
        var distant = new List<int>();
        for (var i = 0; i < totalVertices; i++)
        {
            if (!nearSet.Contains(i))
            {
                distant.Add(i);
            }
        }

        return distant;
    }

    private static Dictionary<int, List<int>> BuildVertexToFaces(MeshData mesh)
    {
        var map = new Dictionary<int, List<int>>();
        for (var faceIndex = 0; faceIndex < mesh.Indices.Count; faceIndex += 3)
        {
            var triangleIndex = faceIndex / 3;
            for (var j = 0; j < 3; j++)
            {
                var vertexId = mesh.Indices[faceIndex + j];
                if (!map.TryGetValue(vertexId, out var faces))
                {
                    faces = new List<int>();
                    map[vertexId] = faces;
                }

                faces.Add(triangleIndex);
            }
        }

        return map;
    }

    private static Dictionary<int, HashSet<int>> BuildVertexNeighbors(MeshData mesh)
    {
        var map = new Dictionary<int, HashSet<int>>();
        for (var faceIndex = 0; faceIndex < mesh.Indices.Count; faceIndex += 3)
        {
            var v0 = mesh.Indices[faceIndex];
            var v1 = mesh.Indices[faceIndex + 1];
            var v2 = mesh.Indices[faceIndex + 2];

            AddEdge(map, v0, v1);
            AddEdge(map, v1, v2);
            AddEdge(map, v2, v0);
        }

        return map;
    }

    private static void AddEdge(Dictionary<int, HashSet<int>> map, int a, int b)
    {
        if (!map.TryGetValue(a, out var setA))
        {
            setA = new HashSet<int>();
            map[a] = setA;
        }

        setA.Add(b);

        if (!map.TryGetValue(b, out var setB))
        {
            setB = new HashSet<int>();
            map[b] = setB;
        }

        setB.Add(a);
    }
}
