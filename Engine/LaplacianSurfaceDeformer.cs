using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Double.Solvers;
using MathNet.Numerics.LinearAlgebra.Solvers;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
using MeshGenLite.Models;
using MathVector = MathNet.Numerics.LinearAlgebra.Vector<double>;

namespace MeshGenLite.Engine;

public sealed class LaplacianSurfaceDeformer : ISurfaceDeformer
{
    public MeshData Deform(MeshData bodyMesh, IReadOnlyList<ConstraintPoint> constraints, ElasticEngineConfig config)
    {
        if (bodyMesh.Vertices.Count == 0 || constraints.Count == 0 || !config.UseLaplacian)
        {
            return bodyMesh.DeepCopy();
        }

        return DeformLocal(bodyMesh, new MeshTopology(bodyMesh), constraints, config);
    }

    public MeshData Deform(MeshData bodyMesh, MeshTopology topology, IReadOnlyList<ConstraintPoint> constraints, ElasticEngineConfig config)
    {
        if (bodyMesh.Vertices.Count == 0 || constraints.Count == 0 || !config.UseLaplacian)
        {
            return bodyMesh.DeepCopy();
        }

        return DeformLocal(bodyMesh, topology, constraints, config);
    }

    private static MeshData DeformLocal(MeshData bodyMesh, MeshTopology topology, IReadOnlyList<ConstraintPoint> constraints, ElasticEngineConfig config)
    {
        var activeOriginalIds = SelectActiveVertices(bodyMesh, constraints, topology, config);
        if (activeOriginalIds.Count < 3)
        {
            return bodyMesh.DeepCopy();
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

            if (aActive) boundaryOriginalIds.Add(a);
            if (bActive) boundaryOriginalIds.Add(b);
            if (cActive) boundaryOriginalIds.Add(c);
        }

        if (localMesh.Indices.Count == 0)
        {
            return ApplyWeightedConstraintFallback(bodyMesh, constraints, activeOriginalIds);
        }

        var localConstraints = new List<ConstraintPoint>();
        foreach (var constraint in constraints)
        {
            if (indexMap.TryGetValue(constraint.VertexId, out var localVertexId))
            {
                localConstraints.Add(new ConstraintPoint
                {
                    VertexId = localVertexId,
                    TargetPosition = constraint.TargetPosition,
                    Weight = constraint.Weight,
                    IsHardConstraint = constraint.IsHardConstraint
                });
            }
        }

        var constrainedLocalIds = localConstraints.Select(constraint => constraint.VertexId).ToHashSet();
        foreach (var originalBoundaryId in boundaryOriginalIds)
        {
            var localBoundaryId = indexMap[originalBoundaryId];
            if (!constrainedLocalIds.Contains(localBoundaryId))
            {
                localConstraints.Add(new ConstraintPoint
                {
                    VertexId = localBoundaryId,
                    TargetPosition = bodyMesh.Vertices[originalBoundaryId],
                    Weight = config.AnchorConstraintWeight,
                    IsHardConstraint = true
                });
            }
        }

        var localDeformed = SolveMesh(localMesh, topology, localConstraints, config);
        var deformed = bodyMesh.DeepCopy();
        for (var localId = 0; localId < reverseMap.Count; localId++)
        {
            deformed.Vertices[reverseMap[localId]] = localDeformed.Vertices[localId];
        }

        return new BodyMeshPreprocessor().RecomputeNormals(deformed);
    }

    private static MeshData SolveMesh(MeshData mesh, MeshTopology topology, IReadOnlyList<ConstraintPoint> constraints, ElasticEngineConfig config)
    {
        var n = mesh.Vertices.Count;
        var laplacian = BuildLaplacian(mesh, topology);

        // Target differential coordinates (Laplacian coordinates)
        var originalX = DenseVector.OfEnumerable(mesh.Vertices.Select(v => (double)v.X));
        var originalY = DenseVector.OfEnumerable(mesh.Vertices.Select(v => (double)v.Y));
        var originalZ = DenseVector.OfEnumerable(mesh.Vertices.Select(v => (double)v.Z));

        // Compute deltas in parallel (3 independent matrix-vector products).
        MathVector? dx = null, dy = null, dz = null;
        Parallel.Invoke(
            () => dx = laplacian * originalX,
            () => dy = laplacian * originalY,
            () => dz = laplacian * originalZ);

        SparseMatrix system;
        DenseVector rhsX, rhsY, rhsZ;

        if (config.UseBiharmonic)
        {
            // Solve (L'L + W)x = L'dx + Wy  -> higher quality, slower
            var lt = (SparseMatrix)laplacian.Transpose();
            system = (SparseMatrix)(lt * laplacian);
            rhsX = (DenseVector)(lt * dx!);
            rhsY = (DenseVector)(lt * dy!);
            rhsZ = (DenseVector)(lt * dz!);
        }
        else
        {
            // Solve (L + W)x = dx + Wy  -> fast, good enough for most cases
            system = laplacian;
            rhsX = (DenseVector)dx!;
            rhsY = (DenseVector)dy!;
            rhsZ = (DenseVector)dz!;
        }

        foreach (var constraint in constraints)
        {
            if (constraint.VertexId < 0 || constraint.VertexId >= n) continue;

            var weight = Math.Max(constraint.Weight, 1e-6);
            system[constraint.VertexId, constraint.VertexId] += weight;
            rhsX[constraint.VertexId] += weight * constraint.TargetPosition.X;
            rhsY[constraint.VertexId] += weight * constraint.TargetPosition.Y;
            rhsZ[constraint.VertexId] += weight * constraint.TargetPosition.Z;
        }

        var solX = DenseVector.Create(n, 0.0);
        var solY = DenseVector.Create(n, 0.0);
        var solZ = DenseVector.Create(n, 0.0);

        // Run 3 RHS solves in parallel using sparse iterative solver (BiCgStab + diagonal preconditioner).
        // For symmetric Laplacian-edit systems this converges in O(sqrt(condition number)) iterations,
        // dramatically faster than direct LU (O(n^3)) on n > ~1000 problems.
        Parallel.Invoke(
            () => SolveIterative(system, rhsX, solX),
            () => SolveIterative(system, rhsY, solY),
            () => SolveIterative(system, rhsZ, solZ));

        var deformed = mesh.DeepCopy();
        for (var i = 0; i < n; i++)
        {
            deformed.Vertices[i] = new Vector3((float)solX[i], (float)solY[i], (float)solZ[i]);
        }
        return deformed;
    }

    private static void SolveIterative(SparseMatrix system, MathVector rhs, MathVector result)
    {
        // Per-thread instances: iterator/solver/preconditioner all carry mutable state.
        var solver = new BiCgStab();
        var preconditioner = new DiagonalPreconditioner();
        var iterCount = new IterationCountStopCriterion<double>(2000);
        var residual = new ResidualStopCriterion<double>(1e-6);

        try
        {
            system.TrySolveIterative(rhs, result, solver, preconditioner, iterCount, residual);
        }
        catch
        {
            // Fall back to direct solve if iterative fails to converge or preconditioner is singular.
            var direct = system.Solve(rhs);
            for (var i = 0; i < direct.Count; i++)
            {
                result[i] = direct[i];
            }
        }
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
        var maxVertices = Math.Min(Math.Max(config.MaxLocalSolveVertexCount, movedSeeds.Count), 8000); // Hard cap at 8000 vertices for performance
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

    private static SparseMatrix BuildLaplacian(MeshData mesh, MeshTopology topology)
    {
        var size = mesh.Vertices.Count;
        var cotangentWeights = new Dictionary<(int A, int B), double>();

        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var i = mesh.Indices[index];
            var j = mesh.Indices[index + 1];
            var k = mesh.Indices[index + 2];
            if (!IsValidTriangleIndex(mesh, i, j, k)) continue;

            AddCotangentWeight(cotangentWeights, j, k, Cotangent(mesh.Vertices[i], mesh.Vertices[j], mesh.Vertices[k]));
            AddCotangentWeight(cotangentWeights, i, k, Cotangent(mesh.Vertices[j], mesh.Vertices[i], mesh.Vertices[k]));
            AddCotangentWeight(cotangentWeights, i, j, Cotangent(mesh.Vertices[k], mesh.Vertices[i], mesh.Vertices[j]));
        }

        var diagonalWeights = new double[size];
        // Build COO triplets up-front: filling SparseMatrix via OfIndexed avoids per-mutation
        // CSR re-allocation that was O(N) per write (= O(N*E) for the whole build).
        var triplets = new List<Tuple<int, int, double>>(cotangentWeights.Count * 2 + size);

        foreach (var pair in cotangentWeights)
        {
            var weight = Math.Max(pair.Value, 1e-4);
            var a = pair.Key.A;
            var b = pair.Key.B;
            if (a >= size || b >= size) continue;

            triplets.Add(Tuple.Create(a, b, -weight));
            triplets.Add(Tuple.Create(b, a, -weight));
            diagonalWeights[a] += weight;
            diagonalWeights[b] += weight;
        }

        for (var vertexId = 0; vertexId < size; vertexId++)
        {
            if (diagonalWeights[vertexId] <= 1e-8)
            {
                // Fall back to combinatorial (uniform) Laplacian for isolated/boundary verts
                // so the matrix stays positive-definite.
                foreach (var neighbor in topology.GetNeighbors(vertexId))
                {
                    if (neighbor >= size) continue;
                    triplets.Add(Tuple.Create(vertexId, neighbor, -1.0));
                    diagonalWeights[vertexId] += 1.0;
                }
            }

            triplets.Add(Tuple.Create(vertexId, vertexId, diagonalWeights[vertexId]));
        }

        return SparseMatrix.OfIndexed(size, size, triplets);
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
