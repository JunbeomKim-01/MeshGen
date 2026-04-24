using System.Numerics;
using MathNet.Numerics.LinearAlgebra.Double;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class LaplacianSurfaceDeformer : ISurfaceDeformer
{
    public MeshData Deform(MeshData bodyMesh, IReadOnlyList<ConstraintPoint> constraints, ElasticEngineConfig config)
    {
        if (bodyMesh.Vertices.Count == 0 || constraints.Count == 0 || !config.UseLaplacian)
        {
            return bodyMesh.DeepCopy();
        }

        return DeformLocal(bodyMesh, constraints, config);
    }

    private static MeshData DeformLocal(MeshData bodyMesh, IReadOnlyList<ConstraintPoint> constraints, ElasticEngineConfig config)
    {
        var topology = new MeshTopology(bodyMesh);
        var activeOriginalIds = SelectActiveVertices(bodyMesh, constraints, topology, config);
        if (activeOriginalIds.Count < 3 || activeOriginalIds.Count >= bodyMesh.Vertices.Count)
        {
            return SolveMesh(bodyMesh, constraints);
        }

        var indexMap = new Dictionary<int, int>(activeOriginalIds.Count);
        var reverseMap = new List<int>(activeOriginalIds.Count);
        var localMesh = new MeshData();
        foreach (var originalId in activeOriginalIds.OrderBy(id => id))
        {
            indexMap[originalId] = localMesh.Vertices.Count;
            reverseMap.Add(originalId);
            localMesh.Vertices.Add(bodyMesh.Vertices[originalId]);
            if (bodyMesh.HasNormals)
            {
                localMesh.Normals ??= new List<Vector3>();
                localMesh.Normals.Add(bodyMesh.Normals![originalId]);
            }
        }

        var boundaryOriginalIds = new HashSet<int>();
        for (var index = 0; index + 2 < bodyMesh.Indices.Count; index += 3)
        {
            var a = bodyMesh.Indices[index];
            var b = bodyMesh.Indices[index + 1];
            var c = bodyMesh.Indices[index + 2];
            if (!IsValidTriangleIndex(bodyMesh, a, b, c))
            {
                continue;
            }

            var aActive = indexMap.ContainsKey(a);
            var bActive = indexMap.ContainsKey(b);
            var cActive = indexMap.ContainsKey(c);
            if (aActive && bActive && cActive)
            {
                localMesh.Indices.Add(indexMap[a]);
                localMesh.Indices.Add(indexMap[b]);
                localMesh.Indices.Add(indexMap[c]);
                continue;
            }

            if (aActive)
            {
                boundaryOriginalIds.Add(a);
            }

            if (bActive)
            {
                boundaryOriginalIds.Add(b);
            }

            if (cActive)
            {
                boundaryOriginalIds.Add(c);
            }
        }

        if (localMesh.Indices.Count == 0)
        {
            return ApplyWeightedConstraintFallback(bodyMesh, constraints, activeOriginalIds);
        }

        var localConstraints = new List<ConstraintPoint>();
        foreach (var constraint in constraints)
        {
            if (!indexMap.TryGetValue(constraint.VertexId, out var localVertexId))
            {
                continue;
            }

            localConstraints.Add(new ConstraintPoint
            {
                VertexId = localVertexId,
                TargetPosition = constraint.TargetPosition,
                Weight = constraint.Weight,
                IsHardConstraint = constraint.IsHardConstraint
            });
        }

        var constrainedLocalIds = localConstraints.Select(constraint => constraint.VertexId).ToHashSet();
        foreach (var originalBoundaryId in boundaryOriginalIds)
        {
            var localBoundaryId = indexMap[originalBoundaryId];
            if (constrainedLocalIds.Contains(localBoundaryId))
            {
                continue;
            }

            localConstraints.Add(new ConstraintPoint
            {
                VertexId = localBoundaryId,
                TargetPosition = bodyMesh.Vertices[originalBoundaryId],
                Weight = config.AnchorConstraintWeight,
                IsHardConstraint = true
            });
        }

        var localDeformed = SolveMesh(localMesh, localConstraints);
        var deformed = bodyMesh.DeepCopy();
        for (var localId = 0; localId < reverseMap.Count; localId++)
        {
            deformed.Vertices[reverseMap[localId]] = localDeformed.Vertices[localId];
        }

        return new BodyMeshPreprocessor().RecomputeNormals(deformed);
    }

    private static MeshData SolveMesh(MeshData mesh, IReadOnlyList<ConstraintPoint> constraints)
    {
        var laplacian = BuildLaplacian(mesh);
        var originalX = DenseVector.OfEnumerable(mesh.Vertices.Select(vertex => (double)vertex.X));
        var originalY = DenseVector.OfEnumerable(mesh.Vertices.Select(vertex => (double)vertex.Y));
        var originalZ = DenseVector.OfEnumerable(mesh.Vertices.Select(vertex => (double)vertex.Z));

        var deltaX = laplacian * originalX;
        var deltaY = laplacian * originalY;
        var deltaZ = laplacian * originalZ;

        var system = SparseMatrix.OfMatrix(laplacian.TransposeThisAndMultiply(laplacian));
        var rhsX = laplacian.TransposeThisAndMultiply(deltaX);
        var rhsY = laplacian.TransposeThisAndMultiply(deltaY);
        var rhsZ = laplacian.TransposeThisAndMultiply(deltaZ);

        foreach (var constraint in constraints)
        {
            if (constraint.VertexId < 0 || constraint.VertexId >= mesh.Vertices.Count)
            {
                continue;
            }

            var weight = Math.Max(constraint.Weight, 1e-4);
            system[constraint.VertexId, constraint.VertexId] += weight;
            rhsX[constraint.VertexId] += weight * constraint.TargetPosition.X;
            rhsY[constraint.VertexId] += weight * constraint.TargetPosition.Y;
            rhsZ[constraint.VertexId] += weight * constraint.TargetPosition.Z;
        }

        for (var diagonal = 0; diagonal < mesh.Vertices.Count; diagonal++)
        {
            if (Math.Abs(system[diagonal, diagonal]) < 1e-8)
            {
                system[diagonal, diagonal] = 1.0;
            }
        }

        var solvedX = system.Solve(rhsX);
        var solvedY = system.Solve(rhsY);
        var solvedZ = system.Solve(rhsZ);

        var deformed = mesh.DeepCopy();
        for (var vertexId = 0; vertexId < deformed.Vertices.Count; vertexId++)
        {
            deformed.Vertices[vertexId] = new Vector3(
                (float)solvedX[vertexId],
                (float)solvedY[vertexId],
                (float)solvedZ[vertexId]);
        }

        return new BodyMeshPreprocessor().RecomputeNormals(deformed);
    }

    private static HashSet<int> SelectActiveVertices(
        MeshData bodyMesh,
        IReadOnlyList<ConstraintPoint> constraints,
        MeshTopology topology,
        ElasticEngineConfig config)
    {
        var movedSeeds = constraints
            .Where(constraint =>
                constraint.VertexId >= 0 &&
                constraint.VertexId < bodyMesh.Vertices.Count &&
                Vector3.DistanceSquared(bodyMesh.Vertices[constraint.VertexId], constraint.TargetPosition) > 1e-8f)
            .Select(constraint => constraint.VertexId)
            .Distinct()
            .ToList();

        if (movedSeeds.Count == 0)
        {
            movedSeeds = constraints
                .Where(constraint => constraint.VertexId >= 0 && constraint.VertexId < bodyMesh.Vertices.Count)
                .Select(constraint => constraint.VertexId)
                .Distinct()
                .ToList();
        }

        var active = new HashSet<int>(movedSeeds);
        var queue = new Queue<(int VertexId, int Depth)>();
        foreach (var seed in movedSeeds)
        {
            queue.Enqueue((seed, 0));
        }

        var maxDepth = Math.Max(config.LocalSolveExpansionRings, 1);
        var maxVertices = Math.Max(config.MaxLocalSolveVertexCount, movedSeeds.Count);
        while (queue.Count > 0 && active.Count < maxVertices)
        {
            var (vertexId, depth) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var neighbor in topology.GetNeighbors(vertexId))
            {
                if (!active.Add(neighbor))
                {
                    continue;
                }

                if (active.Count >= maxVertices)
                {
                    break;
                }

                queue.Enqueue((neighbor, depth + 1));
            }
        }

        foreach (var constraint in constraints)
        {
            if (constraint.VertexId >= 0 && constraint.VertexId < bodyMesh.Vertices.Count)
            {
                active.Add(constraint.VertexId);
            }
        }

        return active;
    }

    private static MeshData ApplyWeightedConstraintFallback(
        MeshData bodyMesh,
        IReadOnlyList<ConstraintPoint> constraints,
        IReadOnlyCollection<int> activeVertexIds)
    {
        var deformed = bodyMesh.DeepCopy();
        var movedConstraints = constraints
            .Where(constraint =>
                constraint.VertexId >= 0 &&
                constraint.VertexId < bodyMesh.Vertices.Count &&
                Vector3.DistanceSquared(bodyMesh.Vertices[constraint.VertexId], constraint.TargetPosition) > 1e-8f)
            .ToList();

        if (movedConstraints.Count == 0)
        {
            return deformed;
        }

        foreach (var vertexId in activeVertexIds)
        {
            var weightedDisplacement = Vector3.Zero;
            var totalWeight = 0.0;
            foreach (var constraint in movedConstraints)
            {
                var distance = Math.Max(Vector3.Distance(bodyMesh.Vertices[vertexId], bodyMesh.Vertices[constraint.VertexId]), 1e-3f);
                var weight = constraint.Weight / (distance * distance);
                weightedDisplacement += (constraint.TargetPosition - bodyMesh.Vertices[constraint.VertexId]) * (float)weight;
                totalWeight += weight;
            }

            if (totalWeight > 1e-8)
            {
                deformed.Vertices[vertexId] = bodyMesh.Vertices[vertexId] + (weightedDisplacement / (float)totalWeight);
            }
        }

        return new BodyMeshPreprocessor().RecomputeNormals(deformed);
    }

    private static SparseMatrix BuildLaplacian(MeshData mesh)
    {
        var size = mesh.Vertices.Count;
        var laplacian = SparseMatrix.Create(size, size, 0.0);
        var cotangentWeights = new Dictionary<(int A, int B), double>();

        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var i = mesh.Indices[index];
            var j = mesh.Indices[index + 1];
            var k = mesh.Indices[index + 2];
            if (!IsValidTriangleIndex(mesh, i, j, k))
            {
                continue;
            }

            AddCotangentWeight(cotangentWeights, j, k, Cotangent(mesh.Vertices[i], mesh.Vertices[j], mesh.Vertices[k]));
            AddCotangentWeight(cotangentWeights, i, k, Cotangent(mesh.Vertices[j], mesh.Vertices[i], mesh.Vertices[k]));
            AddCotangentWeight(cotangentWeights, i, j, Cotangent(mesh.Vertices[k], mesh.Vertices[i], mesh.Vertices[j]));
        }

        var topology = new MeshTopology(mesh);
        var diagonalWeights = new double[size];
        foreach (var pair in cotangentWeights)
        {
            var weight = Math.Max(pair.Value, 1e-4);
            var a = pair.Key.A;
            var b = pair.Key.B;
            laplacian[a, b] -= weight;
            laplacian[b, a] -= weight;
            diagonalWeights[a] += weight;
            diagonalWeights[b] += weight;
        }

        for (var vertexId = 0; vertexId < size; vertexId++)
        {
            if (diagonalWeights[vertexId] <= 1e-8)
            {
                foreach (var neighbor in topology.GetNeighbors(vertexId))
                {
                    laplacian[vertexId, neighbor] -= 1.0;
                    diagonalWeights[vertexId] += 1.0;
                }
            }

            laplacian[vertexId, vertexId] = diagonalWeights[vertexId];
        }

        return laplacian;
    }

    private static void AddCotangentWeight(Dictionary<(int A, int B), double> weights, int a, int b, double value)
    {
        var key = a < b ? (a, b) : (b, a);
        weights[key] = weights.TryGetValue(key, out var existing) ? existing + value : value;
    }

    private static double Cotangent(Vector3 apex, Vector3 first, Vector3 second)
    {
        var left = first - apex;
        var right = second - apex;
        var cross = Vector3.Cross(left, right).Length();
        if (cross <= 1e-8f)
        {
            return 0.0;
        }

        return Vector3.Dot(left, right) / cross;
    }

    private static bool IsValidTriangleIndex(MeshData mesh, int a, int b, int c)
        => a >= 0 && b >= 0 && c >= 0 &&
           a < mesh.Vertices.Count && b < mesh.Vertices.Count && c < mesh.Vertices.Count;
}
