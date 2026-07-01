namespace MeshGenLite.Integration.Mcp.Dtos;

public sealed class ImportDicomStudyRequest
{
    public string DicomDirectory { get; init; } = string.Empty;

    public string? RtssPath { get; init; }

    public double TargetSpacingMm { get; init; } = 2.0;
}

public sealed class ImportDicomStudyResult
{
    public string StudyKind { get; init; } = string.Empty;

    public int SliceCount { get; init; }

    public int RoiCount { get; init; }

    public int RoiWithContoursCount { get; init; }

    public double[] Spacing { get; init; } = Array.Empty<double>();

    public int[] Dimensions { get; init; } = Array.Empty<int>();

    public int? ExternalRoiId { get; init; }
}

public sealed class ImportNiftiSegmentationRequest
{
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

    public double TargetSpacingMm { get; init; } = 2.0;
}

public sealed class ListRoisResult
{
    public IReadOnlyList<RoiSummaryDto> Rois { get; init; } = Array.Empty<RoiSummaryDto>();

    public int? ExternalRoiId { get; init; }
}

public sealed class RoiSummaryDto
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public bool HasContours { get; init; }

    public bool IsSelected { get; init; }

    public bool Visible { get; init; }
}

public sealed class SetExternalRoiRequest
{
    public int RoiId { get; init; }
}

public sealed class SetExternalRoiResult
{
    public int ExternalRoiId { get; init; }

    public string ExternalRoiName { get; init; } = string.Empty;
}

public sealed class BuildLabelMapRequest
{
    public IReadOnlyList<int> RoiIds { get; init; } = Array.Empty<int>();

    public double TargetSpacingMm { get; init; } = 2.0;
}

public sealed class BuildLabelMapResult
{
    public int[] Dimensions { get; init; } = Array.Empty<int>();

    public double[] Spacing { get; init; } = Array.Empty<double>();
}

public sealed class BuildSurfaceMeshRequest
{
    public IReadOnlyList<int>? RoiIds { get; init; }

    public int ExternalSmoothingIterations { get; init; } = 20;
}

public sealed class BuildSurfaceMeshResult
{
    public IReadOnlyList<SurfaceMeshSummaryDto> Meshes { get; init; } = Array.Empty<SurfaceMeshSummaryDto>();
}

public sealed class SurfaceMeshSummaryDto
{
    public int RoiId { get; init; }

    public string RoiName { get; init; } = string.Empty;

    public int VertexCount { get; init; }

    public int TriangleCount { get; init; }
}

public sealed class PipelineStatusDto
{
    public bool StudyLoaded { get; init; }

    public string SourceKind { get; init; } = string.Empty;

    public int RoiCount { get; init; }

    public int? ExternalRoiId { get; init; }

    public bool LabelMapBuilt { get; init; }

    public int SurfaceMeshCount { get; init; }

    public int ElectrodePlacementCount { get; init; }

    public bool AttachmentCompleted { get; init; }

    public bool VolumeMeshGenerated { get; init; }
}
