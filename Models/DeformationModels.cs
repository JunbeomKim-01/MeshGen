using System.Numerics;

namespace MeshGenLite.Models;

/// <summary>
/// UI-independent pure data structure for mesh geometry.
/// Used as the canonical data representation inside the Core engine layer,
/// keeping all deformation logic free from rendering-library dependencies.
/// </summary>
public sealed class MeshData
{
    public MeshData() { }

    public MeshData(IEnumerable<Vector3> vertices, IEnumerable<int> indices)
    {
        Vertices.AddRange(vertices);
        Indices.AddRange(indices);
    }

    public List<Vector3> Vertices { get; } = new();

    public List<int> Indices { get; } = new();

    public List<Vector3>? Normals { get; set; }

    public int TriangleCount => Indices.Count / 3;

    public bool HasNormals => Normals is not null && Normals.Count == Vertices.Count;

    public MeshData DeepCopy()
    {
        var copy = new MeshData();
        copy.Vertices.AddRange(Vertices);
        copy.Indices.AddRange(Indices);
        if (Normals is not null)
        {
            copy.Normals = new List<Vector3>(Normals);
        }

        return copy;
    }
}

/// <summary>
/// A single vertex constraint for the deformation solver.
/// </summary>
public sealed class ConstraintPoint
{
    public int VertexId { get; set; }

    public Vector3 TargetPosition { get; set; }

    public double Weight { get; set; }

    public bool IsHardConstraint { get; set; }
}

/// <summary>
/// Describes the local region of the body mesh that makes contact with an electrode.
/// </summary>
public sealed class ContactPatch
{
    public List<int> VertexIds { get; } = new();

    public List<int> BoundaryVertexIds { get; } = new();

    public bool IsEmpty => VertexIds.Count == 0;
}

/// <summary>
/// Configuration parameters for the elastic deformation engine.
/// </summary>
public sealed class ElasticEngineConfig
{
    public int SmoothingIterations { get; set; } = 2;

    public double SmoothingLambda { get; set; } = 0.08;

    public double AlignmentClearanceMm { get; set; } = 0.5;

    public double ContactDistanceThresholdMm { get; set; } = 2.0;

    public int ContactExpansionRing { get; set; } = 2;

    public bool UseTpsPrototype { get; set; }

    public bool UseBiharmonic { get; set; }

    public bool UseLaplacian { get; set; } = true;

    public int LaplacianIterations { get; set; } = 10;

    public int SoftConstraintRings { get; set; } = 2;

    public double SoftConstraintDecay { get; set; } = 0.55;

    public int AnchorMinRingDistance { get; set; } = 4;

    public int MaxAnchorCount { get; set; } = 64;

    public int LocalSolveExpansionRings { get; set; } = 5;

    public int MaxLocalSolveVertexCount { get; set; } = 4000;

    public double HardConstraintWeight { get; set; } = 8.0;

    public double SoftConstraintWeight { get; set; } = 0.35;

    public double AnchorConstraintWeight { get; set; } = 24.0;

    public bool EnableCollisionCheck { get; set; } = true;

    public bool EnableQualityValidation { get; set; } = true;
}

/// <summary>
/// Holds all intermediate and final results of a deformation session.
/// </summary>
public sealed class ElasticEngineSession
{
    public int ElectrodeId { get; set; }

    public MeshData? OriginalBodyMesh { get; set; }

    public MeshData? PreprocessedBodyMesh { get; set; }

    public MeshData? ElectrodeMesh { get; set; }

    public MeshData? AlignedElectrodeMesh { get; set; }

    public MeshData? DeformedBodyMesh { get; set; }

    public ContactPatch? ContactPatch { get; set; }

    public List<int> AnchorVertexIds { get; } = new();

    public List<ConstraintPoint> Constraints { get; } = new();

    public DeformationQualityReport? QualityReport { get; set; }
}

/// <summary>
/// Batch attachment output for multiple electrodes.
/// </summary>
public sealed class ElasticAttachmentResult
{
    public MeshData? OriginalBodyMesh { get; set; }

    public MeshData? FinalBodyMesh { get; set; }

    public List<int> UnionContactPatchVertexIds { get; } = new();

    public List<ElasticEngineSession> PerElectrodeSessions { get; } = new();

    public DeformationQualityReport? AggregateQualityReport { get; set; }
}

/// <summary>
/// Quality metrics produced after deformation.
/// </summary>
public sealed class DeformationQualityReport
{
    public double MaxDisplacement { get; set; }

    public double MeanDisplacement { get; set; }

    public double MaxAreaStretch { get; set; }

    public double FlippedTriangleRatio { get; set; }

    public bool HasSelfIntersection { get; set; }

    public bool HasPenetration { get; set; }

    public double MeanResidualGap { get; set; }

    public double MaxPenetrationDepth { get; set; }

    public int TotalTriangles { get; set; }

    public int FlippedTriangles { get; set; }

    public string Summary =>
        $"Max {MaxDisplacement:F2} mm | Mean {MeanDisplacement:F2} mm | " +
        $"Flipped {FlippedTriangles}/{TotalTriangles} | Stretch {MaxAreaStretch:F2} | " +
        $"Gap {MeanResidualGap:F2} mm";

    public static DeformationQualityReport AggregateWorstCase(IEnumerable<DeformationQualityReport> reports)
    {
        var materialized = reports.ToList();
        if (materialized.Count == 0)
        {
            return new DeformationQualityReport();
        }

        return new DeformationQualityReport
        {
            MaxDisplacement = materialized.Max(report => report.MaxDisplacement),
            MeanDisplacement = materialized.Max(report => report.MeanDisplacement),
            MaxAreaStretch = materialized.Max(report => report.MaxAreaStretch),
            FlippedTriangleRatio = materialized.Max(report => report.FlippedTriangleRatio),
            HasSelfIntersection = materialized.Any(report => report.HasSelfIntersection),
            HasPenetration = materialized.Any(report => report.HasPenetration),
            MeanResidualGap = materialized.Max(report => report.MeanResidualGap),
            MaxPenetrationDepth = materialized.Max(report => report.MaxPenetrationDepth),
            TotalTriangles = materialized.Max(report => report.TotalTriangles),
            FlippedTriangles = materialized.Max(report => report.FlippedTriangles)
        };
    }
}

/// <summary>
/// Options for electrode rigid alignment.
/// </summary>
public sealed class ElectrodeAlignmentOptions
{
    public double ClearanceMm { get; set; } = 0.5;
}

/// <summary>
/// Options for contact patch extraction.
/// </summary>
public sealed class ContactPatchOptions
{
    public double DistanceThresholdMm { get; set; } = 2.0;

    public int ExpansionRings { get; set; } = 2;
}
