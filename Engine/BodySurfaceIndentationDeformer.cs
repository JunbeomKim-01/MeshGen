using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class BodySurfaceIndentationResult
{
    public MeshData DeformedBodyMesh { get; init; } = new();

    public IReadOnlyList<ConstraintPoint> Constraints { get; init; } = Array.Empty<ConstraintPoint>();

    public IReadOnlyList<int> AnchorVertexIds { get; init; } = Array.Empty<int>();

    public DeformationQualityReport QualityReport { get; init; } = new();
}

public sealed class BodySurfaceIndentationDeformer
{
    private readonly LaplacianSurfaceDeformer surfaceDeformer = new();
    private readonly MeshQualityValidator qualityValidator = new();

    public BodySurfaceIndentationResult Indent(
        MeshData bodyMesh,
        MeshData attachedElectrodeMesh,
        ContactPatch patch,
        Vector3 outwardNormal,
        ElasticEngineConfig config)
    {
        var body = bodyMesh.DeepCopy();
        if (body.Vertices.Count == 0 || patch.VertexIds.Count == 0 || config.ExternalIndentationDepthMm <= 0.0)
        {
            return new BodySurfaceIndentationResult
            {
                DeformedBodyMesh = body,
                QualityReport = qualityValidator.Validate(bodyMesh, body, attachedElectrodeMesh)
            };
        }

        var preprocessor = new BodyMeshPreprocessor();
        if (!body.HasNormals)
        {
            preprocessor.RecomputeNormals(body);
        }

        var normalizedOutward = NormalizeOrDefault(outwardNormal, EstimatePatchNormal(body, patch));
        var topology = new MeshTopology(body);
        var constraints = new List<ConstraintPoint>();
        var constrainedIds = new HashSet<int>();
        var contactSet = patch.VertexIds
            .Where(vertexId => vertexId >= 0 && vertexId < body.Vertices.Count)
            .ToHashSet();
        var boundarySet = patch.BoundaryVertexIds.ToHashSet();
        var hardIds = contactSet.Except(boundarySet).ToList();
        if (hardIds.Count == 0)
        {
            hardIds = contactSet.ToList();
        }

        foreach (var vertexId in hardIds)
        {
            AddIndentConstraint(
                body,
                vertexId,
                normalizedOutward,
                config.ExternalIndentationDepthMm,
                config.HardConstraintWeight,
                true,
                constraints,
                constrainedIds);
        }

        var previousExpansion = new HashSet<int>(contactSet);
        for (var ring = 1; ring <= Math.Max(config.SoftConstraintRings, 0); ring++)
        {
            var expanded = topology.ExpandRings(contactSet, ring);
            var ringIds = expanded.Except(previousExpansion).ToList();
            previousExpansion = expanded;

            var ringScale = Math.Pow(config.SoftConstraintDecay, ring);
            foreach (var vertexId in ringIds)
            {
                AddIndentConstraint(
                    body,
                    vertexId,
                    normalizedOutward,
                    config.ExternalIndentationDepthMm * ringScale,
                    config.SoftConstraintWeight * ringScale,
                    false,
                    constraints,
                    constrainedIds);
            }
        }

        var anchorIds = SampleAnchors(
            topology.GetDistantVertices(contactSet, body.Vertices.Count, config.AnchorMinRingDistance),
            config.MaxAnchorCount).ToList();
        foreach (var anchorId in anchorIds)
        {
            if (!constrainedIds.Add(anchorId))
            {
                continue;
            }

            constraints.Add(new ConstraintPoint
            {
                VertexId = anchorId,
                TargetPosition = body.Vertices[anchorId],
                Weight = config.AnchorConstraintWeight,
                IsHardConstraint = true
            });
        }

        var deformed = surfaceDeformer.Deform(body, constraints, config);
        preprocessor.RecomputeNormals(deformed);
        return new BodySurfaceIndentationResult
        {
            DeformedBodyMesh = deformed,
            Constraints = constraints,
            AnchorVertexIds = anchorIds,
            QualityReport = qualityValidator.Validate(bodyMesh, deformed, attachedElectrodeMesh)
        };
    }

    private static void AddIndentConstraint(
        MeshData body,
        int vertexId,
        Vector3 outwardNormal,
        double depth,
        double weight,
        bool isHard,
        List<ConstraintPoint> constraints,
        HashSet<int> constrainedIds)
    {
        if (vertexId < 0 || vertexId >= body.Vertices.Count || !constrainedIds.Add(vertexId))
        {
            return;
        }

        var normal = body.HasNormals ? body.Normals![vertexId] : outwardNormal;
        normal = NormalizeOrDefault(normal, outwardNormal);
        if (Vector3.Dot(normal, outwardNormal) < 0.0f)
        {
            normal = -normal;
        }

        constraints.Add(new ConstraintPoint
        {
            VertexId = vertexId,
            TargetPosition = body.Vertices[vertexId] - (normal * (float)depth),
            Weight = weight,
            IsHardConstraint = isHard
        });
    }

    private static Vector3 EstimatePatchNormal(MeshData body, ContactPatch patch)
    {
        if (body.HasNormals)
        {
            var sum = Vector3.Zero;
            var count = 0;
            foreach (var vertexId in patch.VertexIds)
            {
                if (vertexId < 0 || vertexId >= body.Normals!.Count)
                {
                    continue;
                }

                sum += body.Normals[vertexId];
                count++;
            }

            if (count > 0 && sum.LengthSquared() > 1e-8f)
            {
                return Vector3.Normalize(sum);
            }
        }

        return Vector3.UnitZ;
    }

    private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback)
    {
        if (value.LengthSquared() > 1e-8f)
        {
            return Vector3.Normalize(value);
        }

        return fallback.LengthSquared() > 1e-8f ? Vector3.Normalize(fallback) : Vector3.UnitZ;
    }

    private static IEnumerable<int> SampleAnchors(IReadOnlyList<int> anchorVertexIds, int maxAnchorCount)
    {
        if (anchorVertexIds.Count <= maxAnchorCount || maxAnchorCount <= 0)
        {
            return anchorVertexIds;
        }

        var sampled = new List<int>(maxAnchorCount);
        var step = anchorVertexIds.Count / (double)maxAnchorCount;
        for (var sample = 0; sample < maxAnchorCount; sample++)
        {
            var index = (int)Math.Round(sample * step);
            index = Math.Clamp(index, 0, anchorVertexIds.Count - 1);
            sampled.Add(anchorVertexIds[index]);
        }

        return sampled.Distinct().ToList();
    }
}
