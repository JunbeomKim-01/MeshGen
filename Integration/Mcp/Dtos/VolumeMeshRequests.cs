namespace MeshGenLite.Integration.Mcp.Dtos;

public sealed class GenerateVolumeMeshRequest
{
    /// <summary>
    /// Alpha radius (mm) for vtkDelaunay3D alpha shape filtering.
    /// 0 = unfiltered convex hull, larger = fills more of the gap between body and electrodes.
    /// </summary>
    public double AlphaMm { get; init; } = 0.0;

    /// <summary>
    /// Cap on input point count after decimation. Lower = much faster Delaunay3D, lower fidelity.
    /// Set 0 to disable decimation. Default 3000 (interactive preview quality).
    /// </summary>
    public int MaxPoints { get; init; } = 3000;
}

public sealed class GenerateVolumeMeshResult
{
    public int PointCount { get; init; }

    public int TetrahedronCount { get; init; }

    public int InputPointCount { get; init; }

    public int DecimatedPointCount { get; init; }
}
