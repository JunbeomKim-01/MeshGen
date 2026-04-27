using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;

namespace MeshGenLite.Engine.Heuristics;

public sealed class SurfaceCorrespondenceService
{
    public IReadOnlyList<SurfaceHitResult> FindCorrespondences(
        IReadOnlyList<ElectrodeSamplePoint> samples, 
        MeshData bodyMesh, 
        Vector3 electrodeApproachDirection)
    {
        var bodyTree = new KdTree(bodyMesh.Vertices);
        var results = new List<SurfaceHitResult>();

        foreach (var sample in samples)
        {
            var hit = new SurfaceHitResult
            {
                SampleId = sample.SampleId,
                Success = false
            };

            var candidates = bodyTree.FindWithinRadius(sample.WorldPosition, 20.0);
            var bestBodyId = -1;
            var bestScore = float.MaxValue;

            foreach (var bodyId in candidates)
            {
                var bodyVertex = bodyMesh.Vertices[bodyId];
                var bodyNormal = bodyMesh.HasNormals ? bodyMesh.Normals![bodyId] : Vector3.UnitZ;
                if (bodyNormal.LengthSquared() <= 1e-8f) bodyNormal = Vector3.UnitZ;
                bodyNormal = Vector3.Normalize(bodyNormal);

                // electrodeApproachDirection points from the electrode towards the body.
                // We want the bodyNormal to point TOWARDS the electrode (opposite to approach direction).
                // So Dot(bodyNormal, electrodeApproachDirection) should be near -1.0 for the outer shell.
                // For the inner shell, the normal points away from the electrode, so Dot is near +1.0.
                var distance = Vector3.Distance(sample.WorldPosition, bodyVertex);
                var alignment = Vector3.Dot(bodyNormal, electrodeApproachDirection);
                
                // We penalize positive alignment (inner shell) and reward negative alignment (outer shell).
                var penalty = alignment > 0.0f ? 50.0f * alignment : 0.0f;
                var score = distance + penalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestBodyId = bodyId;
                }
            }

            if (bestBodyId < 0)
            {
                bestBodyId = bodyTree.FindNearest(sample.WorldPosition);
            }

            if (bestBodyId >= 0)
            {
                hit.Success = true;
                hit.TargetVertexId = bestBodyId;
                hit.HitPoint = bodyMesh.Vertices[bestBodyId];
                var finalNormal = bodyMesh.HasNormals ? bodyMesh.Normals![bestBodyId] : Vector3.UnitZ;
                hit.HitNormal = finalNormal.LengthSquared() <= 1e-8f ? Vector3.UnitZ : Vector3.Normalize(finalNormal);
                hit.Distance = Vector3.Distance(sample.WorldPosition, hit.HitPoint);
            }

            results.Add(hit);
        }

        return results;
    }
}
