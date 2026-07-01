namespace MeshGenLite.Integration.Mcp.Dtos;

public sealed class ExportSurfaceRequest
{
    public string OutputPath { get; init; } = string.Empty;

    public bool Overwrite { get; init; }
}

public sealed class ExportResult
{
    public string OutputPath { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public int PointCount { get; init; }

    public int TriangleCount { get; init; }
}

public sealed class ExportFemPackageRequest
{
    public string OutputDirectory { get; init; } = string.Empty;

    public string BaseName { get; init; } = "meshgen_fem";

    public double AlphaMm { get; init; } = 0.0;

    public int MaxPoints { get; init; } = 3000;

    public bool Overwrite { get; init; }
}

public sealed class ExportFemPackageResult
{
    public string OutputDirectory { get; init; } = string.Empty;

    public string VtuPath { get; init; } = string.Empty;

    public string NodesPath { get; init; } = string.Empty;

    public string ElementsPath { get; init; } = string.Empty;

    public string BoundaryFacesPath { get; init; } = string.Empty;

    public string MaterialsPath { get; init; } = string.Empty;

    public string ManifestPath { get; init; } = string.Empty;

    public int NodeCount { get; init; }

    public int TetrahedronCount { get; init; }

    public int BoundaryFaceCount { get; init; }

    public int MaterialCount { get; init; }
}
