using System.Numerics;

namespace MeshGenLite.Models;

public sealed class ElectrodeSamplePoint
{
    public int SampleId { get; set; }
    public Vector3 LocalPosition { get; set; }
    public Vector3 WorldPosition { get; set; }
    public bool IsBoundary { get; set; }
}

public sealed class SurfaceHitResult
{
    public bool Success { get; set; }
    public int SampleId { get; set; }
    public Vector3 HitPoint { get; set; }
    public Vector3 HitNormal { get; set; }
    public float Distance { get; set; }
    public int TargetVertexId { get; set; }
}

public sealed class AttachmentQualityReport
{
    public float MaxPenetrationMm { get; set; }
    public float MeanPenetrationMm { get; set; }
    public float MaxGapMm { get; set; }
    public float MeanGapMm { get; set; }
    public float ContactCoverageRatio { get; set; }
    public int PenetrationCount { get; set; }
    public int FloatingCount { get; set; }
    public bool Passed { get; set; }
    public string Summary { get; set; } = "";
}

public sealed class AttachmentParameters
{
    public float ContactThresholdMm { get; set; } = 2.0f;
    public int ExpansionRings { get; set; } = 2;
    public int SmoothIterations { get; set; } = 2;
    public float HardWeight { get; set; } = 8.0f;
    public float SoftWeight { get; set; } = 0.35f;
    public float MaxDisplacementMm { get; set; } = 5.0f;
    public float MinimumClearanceMm { get; set; } = 0.2f;
}
