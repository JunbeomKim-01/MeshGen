using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class BodyMeshPreprocessor : IBodyMeshPreprocessor
{
    public MeshData Clean(MeshData mesh)
    {
        var validTriangles = new List<(int A, int B, int C)>();
        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var a = mesh.Indices[index];
            var b = mesh.Indices[index + 1];
            var c = mesh.Indices[index + 2];
            if (a < 0 || b < 0 || c < 0 ||
                a >= mesh.Vertices.Count || b >= mesh.Vertices.Count || c >= mesh.Vertices.Count)
            {
                continue;
            }

            if (a == b || b == c || c == a)
            {
                continue;
            }

            var area = Vector3.Cross(mesh.Vertices[b] - mesh.Vertices[a], mesh.Vertices[c] - mesh.Vertices[a]).Length();
            if (area <= 1e-6f)
            {
                continue;
            }

            validTriangles.Add((a, b, c));
        }

        if (validTriangles.Count == 0)
        {
            var emptyCopy = mesh.DeepCopy();
            emptyCopy.Indices.Clear();
            return emptyCopy;
        }

        var largestComponent = ExtractLargestTriangleComponent(validTriangles);
        var cleaned = mesh.DeepCopy();
        cleaned.Indices.Clear();

        foreach (var triangle in largestComponent)
        {
            cleaned.Indices.Add(triangle.A);
            cleaned.Indices.Add(triangle.B);
            cleaned.Indices.Add(triangle.C);
        }

        return cleaned;
    }

    public MeshData Smooth(MeshData mesh, int iterations, double lambda)
    {
        if (mesh.Vertices.Count == 0 || iterations <= 0 || lambda <= 0.0)
        {
            return mesh.DeepCopy();
        }

        var working = mesh.DeepCopy();
        var topology = new MeshTopology(working);
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var nextVertices = new List<Vector3>(working.Vertices.Count);
            for (var vertexId = 0; vertexId < working.Vertices.Count; vertexId++)
            {
                var neighbors = topology.GetNeighbors(vertexId);
                if (neighbors.Count == 0)
                {
                    nextVertices.Add(working.Vertices[vertexId]);
                    continue;
                }

                var average = Vector3.Zero;
                foreach (var neighbor in neighbors)
                {
                    average += working.Vertices[neighbor];
                }

                average /= neighbors.Count;
                var current = working.Vertices[vertexId];
                nextVertices.Add(current + ((average - current) * (float)lambda));
            }

            working.Vertices.Clear();
            working.Vertices.AddRange(nextVertices);
        }

        return working;
    }

    public MeshData RecomputeNormals(MeshData mesh)
    {
        var normals = Enumerable.Repeat(Vector3.Zero, mesh.Vertices.Count).ToArray();
        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var a = mesh.Indices[index];
            var b = mesh.Indices[index + 1];
            var c = mesh.Indices[index + 2];
            var faceNormal = Vector3.Cross(mesh.Vertices[b] - mesh.Vertices[a], mesh.Vertices[c] - mesh.Vertices[a]);
            if (faceNormal.LengthSquared() <= 1e-8f)
            {
                continue;
            }

            normals[a] += faceNormal;
            normals[b] += faceNormal;
            normals[c] += faceNormal;
        }

        mesh.Normals = normals
            .Select(normal => normal.LengthSquared() <= 1e-8f ? Vector3.UnitZ : Vector3.Normalize(normal))
            .ToList();
        return mesh;
    }

    public bool ValidateTopology(MeshData mesh)
    {
        if (mesh.Indices.Count % 3 != 0 || mesh.Vertices.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < mesh.Indices.Count; index++)
        {
            if (mesh.Indices[index] < 0 || mesh.Indices[index] >= mesh.Vertices.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static List<(int A, int B, int C)> ExtractLargestTriangleComponent(List<(int A, int B, int C)> triangles)
    {
        var vertexToTriangles = new Dictionary<int, List<int>>();
        for (var triangleId = 0; triangleId < triangles.Count; triangleId++)
        {
            foreach (var vertexId in new[] { triangles[triangleId].A, triangles[triangleId].B, triangles[triangleId].C })
            {
                if (!vertexToTriangles.TryGetValue(vertexId, out var list))
                {
                    list = new List<int>();
                    vertexToTriangles[vertexId] = list;
                }

                list.Add(triangleId);
            }
        }

        var visited = new bool[triangles.Count];
        List<int>? largest = null;
        for (var start = 0; start < triangles.Count; start++)
        {
            if (visited[start])
            {
                continue;
            }

            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);
                foreach (var vertexId in new[] { triangles[current].A, triangles[current].B, triangles[current].C })
                {
                    if (!vertexToTriangles.TryGetValue(vertexId, out var neighbors))
                    {
                        continue;
                    }

                    foreach (var neighborTriangle in neighbors)
                    {
                        if (visited[neighborTriangle])
                        {
                            continue;
                        }

                        visited[neighborTriangle] = true;
                        queue.Enqueue(neighborTriangle);
                    }
                }
            }

            if (largest is null || component.Count > largest.Count)
            {
                largest = component;
            }
        }

        return largest is null
            ? new List<(int A, int B, int C)>()
            : largest.Select(index => triangles[index]).ToList();
    }
}
