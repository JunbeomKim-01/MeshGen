using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class MeshQualityValidator : IMeshQualityValidator
{
    public DeformationQualityReport Validate(MeshData originalMesh, MeshData deformedMesh, MeshData electrodeMesh)
    {
        var report = new DeformationQualityReport();
        if (originalMesh.Vertices.Count == 0 || deformedMesh.Vertices.Count == 0)
        {
            return report;
        }

        var comparedVertexCount = Math.Min(originalMesh.Vertices.Count, deformedMesh.Vertices.Count);
        if (comparedVertexCount == 0)
        {
            return report;
        }

        var displacementSum = 0.0;
        for (var vertexId = 0; vertexId < comparedVertexCount; vertexId++)
        {
            var displacement = Vector3.Distance(originalMesh.Vertices[vertexId], deformedMesh.Vertices[vertexId]);
            report.MaxDisplacement = Math.Max(report.MaxDisplacement, displacement);
            displacementSum += displacement;
        }

        report.MeanDisplacement = displacementSum / comparedVertexCount;
        report.TotalTriangles = Math.Min(originalMesh.TriangleCount, deformedMesh.TriangleCount);

        for (var index = 0; index + 2 < originalMesh.Indices.Count && index + 2 < deformedMesh.Indices.Count; index += 3)
        {
            if (!IsValidTriangle(originalMesh, index) || !IsValidTriangle(deformedMesh, index))
            {
                continue;
            }

            var oa = originalMesh.Vertices[originalMesh.Indices[index]];
            var ob = originalMesh.Vertices[originalMesh.Indices[index + 1]];
            var oc = originalMesh.Vertices[originalMesh.Indices[index + 2]];
            var da = deformedMesh.Vertices[deformedMesh.Indices[index]];
            var db = deformedMesh.Vertices[deformedMesh.Indices[index + 1]];
            var dc = deformedMesh.Vertices[deformedMesh.Indices[index + 2]];

            var originalArea = TriangleArea(oa, ob, oc);
            var deformedArea = TriangleArea(da, db, dc);
            if (originalArea > 1e-8)
            {
                report.MaxAreaStretch = Math.Max(report.MaxAreaStretch, deformedArea / originalArea);
            }

            var originalNormal = Vector3.Cross(ob - oa, oc - oa);
            var deformedNormal = Vector3.Cross(db - da, dc - da);
            if (originalNormal.LengthSquared() > 1e-8f &&
                deformedNormal.LengthSquared() > 1e-8f &&
                Vector3.Dot(Vector3.Normalize(originalNormal), Vector3.Normalize(deformedNormal)) < 0.0f)
            {
                report.FlippedTriangles++;
            }
        }

        report.FlippedTriangleRatio = report.TotalTriangles == 0
            ? 0.0
            : report.FlippedTriangles / (double)report.TotalTriangles;

        var preprocessor = new BodyMeshPreprocessor();
        if (!deformedMesh.HasNormals)
        {
            preprocessor.RecomputeNormals(deformedMesh);
        }

        var bodyTree = new KdTree(deformedMesh.Vertices);
        var underside = ElectrodeSampling.GetUndersideCap(electrodeMesh, deformedMesh);
        var gapSum = 0.0;
        var gapCount = 0;

        foreach (var vertexId in underside.VertexIds)
        {
            var electrodeVertex = electrodeMesh.Vertices[vertexId];
            var bodyVertexId = bodyTree.FindNearest(electrodeVertex);
            if (bodyVertexId < 0)
            {
                continue;
            }

            var bodyVertex = deformedMesh.Vertices[bodyVertexId];
            var bodyNormal = deformedMesh.HasNormals ? deformedMesh.Normals![bodyVertexId] : Vector3.UnitZ;
            if (bodyNormal.LengthSquared() <= 1e-8f)
            {
                bodyNormal = Vector3.UnitZ;
            }

            bodyNormal = Vector3.Normalize(bodyNormal);
            var signedDistance = Vector3.Dot(electrodeVertex - bodyVertex, bodyNormal);
            gapSum += Math.Abs(signedDistance);
            gapCount++;
            if (signedDistance < 0.0f)
            {
                report.HasPenetration = true;
                report.MaxPenetrationDepth = Math.Max(report.MaxPenetrationDepth, -signedDistance);
            }
        }

        report.MeanResidualGap = gapCount == 0 ? 0.0 : gapSum / gapCount;
        return report;
    }

    private static double TriangleArea(Vector3 a, Vector3 b, Vector3 c)
        => Vector3.Cross(b - a, c - a).Length() * 0.5;

    private static bool IsValidTriangle(MeshData mesh, int index)
        => mesh.Indices[index] >= 0 &&
           mesh.Indices[index + 1] >= 0 &&
           mesh.Indices[index + 2] >= 0 &&
           mesh.Indices[index] < mesh.Vertices.Count &&
           mesh.Indices[index + 1] < mesh.Vertices.Count &&
           mesh.Indices[index + 2] < mesh.Vertices.Count;
}
