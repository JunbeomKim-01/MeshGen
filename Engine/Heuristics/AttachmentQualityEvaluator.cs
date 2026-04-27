using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;

namespace MeshGenLite.Engine.Heuristics;

public sealed class AttachmentQualityEvaluator
{
    public AttachmentQualityReport Evaluate(
        MeshData electrodeMesh, 
        MeshData bodyMesh, 
        IReadOnlyList<ElectrodeSamplePoint> samples)
    {
        var report = new AttachmentQualityReport();
        if (bodyMesh.Vertices.Count == 0 || samples.Count == 0) return report;

        var bodyTree = new KdTree(bodyMesh.Vertices);
        var penetrationSum = 0.0f;
        var gapSum = 0.0f;

        foreach (var sample in samples)
        {
            var worldPos = sample.WorldPosition;
            var nearestId = bodyTree.FindNearest(worldPos);
            if (nearestId < 0) continue;

            var bodyVertex = bodyMesh.Vertices[nearestId];
            var bodyNormal = bodyMesh.HasNormals ? bodyMesh.Normals![nearestId] : Vector3.UnitZ;
            bodyNormal = bodyNormal.LengthSquared() <= 1e-8f ? Vector3.UnitZ : Vector3.Normalize(bodyNormal);

            // Vector from surface to sample
            var surfaceToPoint = worldPos - bodyVertex;
            // Signed distance: positive means outside (gap), negative means inside (penetration)
            var signedDistance = Vector3.Dot(surfaceToPoint, bodyNormal);

            if (signedDistance < -1e-3f) // Penetration
            {
                var depth = -signedDistance;
                report.MaxPenetrationMm = Math.Max(report.MaxPenetrationMm, depth);
                penetrationSum += depth;
                report.PenetrationCount++;
            }
            else if (signedDistance > 1e-3f) // Gap
            {
                report.MaxGapMm = Math.Max(report.MaxGapMm, signedDistance);
                gapSum += signedDistance;
                report.FloatingCount++;
            }
        }

        report.MeanPenetrationMm = report.PenetrationCount > 0 ? penetrationSum / report.PenetrationCount : 0;
        report.MeanGapMm = report.FloatingCount > 0 ? gapSum / report.FloatingCount : 0;
        
        var validContactCount = samples.Count - report.PenetrationCount - report.FloatingCount;
        report.ContactCoverageRatio = (float)validContactCount / samples.Count;

        report.Passed = report.MaxPenetrationMm < 2.0f && report.MaxGapMm < 5.0f; 
        
        return report;
    }
}
