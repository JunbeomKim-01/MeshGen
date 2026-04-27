using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class ElectrodeConformanceResult
{
    public MeshData ElectrodeMesh { get; init; } = new();

    public DeformationQualityReport QualityReport { get; init; } = new();
}

public sealed class ElectrodeSurfaceConformer
{
    public ElectrodeConformanceResult Conform(MeshData bodyMesh, MeshData electrodeMesh, ElasticEngineConfig config)
    {
        var attached = electrodeMesh.DeepCopy();
        if (bodyMesh.Vertices.Count == 0 || electrodeMesh.Vertices.Count == 0)
        {
            return new ElectrodeConformanceResult
            {
                ElectrodeMesh = attached,
                QualityReport = new DeformationQualityReport()
            };
        }

        var body = bodyMesh.DeepCopy();
        if (!body.HasNormals)
        {
            new BodyMeshPreprocessor().RecomputeNormals(body);
        }

        var underside = ElectrodeSampling.GetUndersideCap(electrodeMesh, body);
        if (underside.VertexIds.Count == 0)
        {
            return new ElectrodeConformanceResult
            {
                ElectrodeMesh = attached,
                QualityReport = BuildQualityReport(electrodeMesh, attached, body)
            };
        }

        var bodyTree = new KdTree(body.Vertices);
        var undersidePositions = underside.VertexIds.Select(id => electrodeMesh.Vertices[id]).ToList();
        var undersideTree = new KdTree(undersidePositions);
        var undersideSet = underside.VertexIds.ToHashSet();
        var undersideDisplacements = BuildUndersideDisplacements(body, bodyTree, electrodeMesh, underside.VertexIds, config);
        var rigidShift = ComputeAverage(undersideDisplacements);

        var electrodeCentroid = ComputeCentroid(electrodeMesh.Vertices);
        var outward = electrodeCentroid - underside.Centroid;
        if (outward.LengthSquared() <= 1e-8f)
        {
            outward = -underside.CapNormal;
        }

        outward = outward.LengthSquared() <= 1e-8f ? Vector3.UnitZ : Vector3.Normalize(outward);
        var maxDepth = Math.Max(
            electrodeMesh.Vertices.Max(vertex => Vector3.Dot(vertex - underside.Centroid, outward)),
            1e-3f);

        for (var vertexId = 0; vertexId < electrodeMesh.Vertices.Count; vertexId++)
        {
            var vertex = electrodeMesh.Vertices[vertexId];
            var nearestUndersideLocalId = undersideTree.FindNearest(vertex);
            if (nearestUndersideLocalId < 0 || nearestUndersideLocalId >= undersideDisplacements.Count)
            {
                continue;
            }

            var depth = Math.Clamp(Vector3.Dot(vertex - underside.Centroid, outward) / maxDepth, 0.0f, 1.0f);
            var falloff = undersideSet.Contains(vertexId)
                ? 1.0f
                : MathF.Pow(1.0f - depth, 1.35f);

            var localResidual = undersideDisplacements[nearestUndersideLocalId] - rigidShift;
            attached.Vertices[vertexId] = vertex + rigidShift + (localResidual * falloff);
        }

        new BodyMeshPreprocessor().RecomputeNormals(attached);
        return new ElectrodeConformanceResult
        {
            ElectrodeMesh = attached,
            QualityReport = BuildQualityReport(electrodeMesh, attached, body)
        };
    }

    private static List<Vector3> BuildUndersideDisplacements(
        MeshData body,
        KdTree bodyTree,
        MeshData electrodeMesh,
        IReadOnlyList<int> undersideVertexIds,
        ElasticEngineConfig config)
    {
        var displacements = new List<Vector3>(undersideVertexIds.Count);
        var clearance = (float)Math.Max(0.0, config.ElectrodeAttachmentClearanceMm);
        foreach (var vertexId in undersideVertexIds)
        {
            var electrodeVertex = electrodeMesh.Vertices[vertexId];
            var nearestBodyId = bodyTree.FindNearest(electrodeVertex);
            if (nearestBodyId < 0)
            {
                displacements.Add(Vector3.Zero);
                continue;
            }

            var normal = body.HasNormals ? body.Normals![nearestBodyId] : Vector3.UnitZ;
            if (normal.LengthSquared() <= 1e-8f)
            {
                normal = Vector3.UnitZ;
            }

            normal = Vector3.Normalize(normal);
            var target = body.Vertices[nearestBodyId] + (normal * clearance);
            displacements.Add(target - electrodeVertex);
        }

        return displacements;
    }

    private static DeformationQualityReport BuildQualityReport(
        MeshData originalElectrode,
        MeshData attachedElectrode,
        MeshData body)
    {
        var report = new DeformationQualityReport
        {
            TotalTriangles = Math.Min(originalElectrode.TriangleCount, attachedElectrode.TriangleCount)
        };

        var comparedVertexCount = Math.Min(originalElectrode.Vertices.Count, attachedElectrode.Vertices.Count);
        var displacementSum = 0.0;
        for (var vertexId = 0; vertexId < comparedVertexCount; vertexId++)
        {
            var displacement = Vector3.Distance(originalElectrode.Vertices[vertexId], attachedElectrode.Vertices[vertexId]);
            report.MaxDisplacement = Math.Max(report.MaxDisplacement, displacement);
            displacementSum += displacement;
        }

        report.MeanDisplacement = comparedVertexCount == 0 ? 0.0 : displacementSum / comparedVertexCount;

        for (var index = 0; index + 2 < originalElectrode.Indices.Count && index + 2 < attachedElectrode.Indices.Count; index += 3)
        {
            if (!IsValidTriangle(originalElectrode, index) || !IsValidTriangle(attachedElectrode, index))
            {
                continue;
            }

            var oa = originalElectrode.Vertices[originalElectrode.Indices[index]];
            var ob = originalElectrode.Vertices[originalElectrode.Indices[index + 1]];
            var oc = originalElectrode.Vertices[originalElectrode.Indices[index + 2]];
            var da = attachedElectrode.Vertices[attachedElectrode.Indices[index]];
            var db = attachedElectrode.Vertices[attachedElectrode.Indices[index + 1]];
            var dc = attachedElectrode.Vertices[attachedElectrode.Indices[index + 2]];

            var originalArea = TriangleArea(oa, ob, oc);
            var attachedArea = TriangleArea(da, db, dc);
            if (originalArea > 1e-8)
            {
                report.MaxAreaStretch = Math.Max(report.MaxAreaStretch, attachedArea / originalArea);
            }

            var originalNormal = Vector3.Cross(ob - oa, oc - oa);
            var attachedNormal = Vector3.Cross(db - da, dc - da);
            if (originalNormal.LengthSquared() > 1e-8f &&
                attachedNormal.LengthSquared() > 1e-8f &&
                Vector3.Dot(Vector3.Normalize(originalNormal), Vector3.Normalize(attachedNormal)) < 0.0f)
            {
                report.FlippedTriangles++;
            }
        }

        report.FlippedTriangleRatio = report.TotalTriangles == 0
            ? 0.0
            : report.FlippedTriangles / (double)report.TotalTriangles;

        var bodyTree = new KdTree(body.Vertices);
        var underside = ElectrodeSampling.GetUndersideCap(attachedElectrode, body);
        var gapSum = 0.0;
        var gapCount = 0;
        foreach (var vertexId in underside.VertexIds)
        {
            var electrodeVertex = attachedElectrode.Vertices[vertexId];
            var bodyVertexId = bodyTree.FindNearest(electrodeVertex);
            if (bodyVertexId < 0)
            {
                continue;
            }

            var bodyVertex = body.Vertices[bodyVertexId];
            var bodyNormal = body.HasNormals ? body.Normals![bodyVertexId] : Vector3.UnitZ;
            if (bodyNormal.LengthSquared() <= 1e-8f)
            {
                bodyNormal = Vector3.UnitZ;
            }

            bodyNormal = Vector3.Normalize(bodyNormal);
            var signedDistance = Vector3.Dot(electrodeVertex - bodyVertex, bodyNormal);
            gapSum += Math.Abs(signedDistance);
            gapCount++;
            if (signedDistance < -1e-3f)
            {
                report.HasPenetration = true;
                report.MaxPenetrationDepth = Math.Max(report.MaxPenetrationDepth, -signedDistance);
            }
        }

        report.MeanResidualGap = gapCount == 0 ? 0.0 : gapSum / gapCount;
        return report;
    }

    private static Vector3 ComputeCentroid(IEnumerable<Vector3> vertices)
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var vertex in vertices)
        {
            sum += vertex;
            count++;
        }

        return count == 0 ? Vector3.Zero : sum / count;
    }

    private static Vector3 ComputeAverage(IEnumerable<Vector3> vectors)
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var vector in vectors)
        {
            sum += vector;
            count++;
        }

        return count == 0 ? Vector3.Zero : sum / count;
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
