namespace MeshGenLite.Integration.Mcp.Dtos;

public sealed class AttachmentParametersDto
{
    public double ContactThresholdMm { get; init; } = 2.0;

    public int ExpansionRings { get; init; } = 2;

    public int SmoothingIterations { get; init; } = 2;

    public double HardWeight { get; init; } = 8.0;

    public double SoftWeight { get; init; } = 0.35;
}

public sealed class AttachElectrodesBatchRequest
{
    public AttachmentParametersDto? Parameters { get; init; }
}

public sealed class AttachElectrodesBatchResult
{
    public int AttachedCount { get; init; }

    public string QualitySummary { get; init; } = string.Empty;

    public double MaxDisplacementMm { get; init; }

    public double MeanDisplacementMm { get; init; }

    public int FlippedTriangleCount { get; init; }
}
