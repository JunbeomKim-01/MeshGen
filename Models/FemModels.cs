using System.Windows.Media.Media3D;
using Kitware.VTK;

namespace MeshGenLite.Models;

public static class FemArrayNames
{
    public const string LayerIndex = "LayerIndex";
    public const string RegionId = "RegionId";
    public const string MaterialId = "MaterialId";
    public const string BoundaryMarker = "BoundaryMarker";
    public const string SourceRoiId = "SourceRoiId";
}

public static class FemIds
{
    public const int DefaultBodyRegionId = 1;
    public const int DefaultBodyMaterialId = 1;
    public const int ExternalBoundaryMarker = 1;
    public const int UnknownBoundaryMarker = 999;
    public const int RoiBoundaryBase = 10_000;
    public const int ElectrodeRegionBase = 100_000;
    public const int ElectrodeOuterBoundaryBase = 200_000;
    public const int ElectrodeContactBoundaryBase = 300_000;

    public static int BodyRegion(int roiId) => roiId > 0 ? roiId : DefaultBodyRegionId;

    public static int BodyMaterial(int roiId) => roiId > 0 ? roiId : DefaultBodyMaterialId;

    public static int RoiBoundary(int roiId, bool isExternal)
        => isExternal ? ExternalBoundaryMarker : RoiBoundaryBase + Math.Max(roiId, 0);

    public static int ElectrodeLayerRegion(int placementId, int layerIndex)
        => ElectrodeRegionBase + (Math.Max(placementId, 0) * 100) + Math.Max(layerIndex, 0);

    public static int ElectrodeOuterBoundary(int placementId)
        => ElectrodeOuterBoundaryBase + Math.Max(placementId, 0);

    public static int ElectrodeContactBoundary(int placementId)
        => ElectrodeContactBoundaryBase + Math.Max(placementId, 0);
}

public static class FemMaterialDefaults
{
    public const double BodyConductivity = 0.2;
    public const double BodyPermittivity = 1.0e5;
    public const double BodyThermalConductivity = 0.5;
    public const double BodyDensity = 1000.0;
    public const double BodySpecificHeat = 3600.0;

    public const double ElectrodeThermalConductivity = 15.0;
    public const double ElectrodeDensity = 1900.0;
    public const double ElectrodeSpecificHeat = 800.0;
}

public sealed class FemMaterialRecord
{
    public int RegionId { get; init; }

    public int MaterialId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string SourceType { get; init; } = string.Empty;

    public double Conductivity { get; init; }

    public double Permittivity { get; init; }

    public double ThermalConductivity { get; init; }

    public double Density { get; init; }

    public double SpecificHeat { get; init; }
}

public sealed class FemBoundaryRecord
{
    public int BoundaryMarker { get; init; }

    public string Name { get; init; } = string.Empty;

    public string SourceType { get; init; } = string.Empty;

    public int OwnerRegionId { get; init; }
}

public sealed class FemElectrodeExportInfo
{
    public int PlacementId { get; init; }

    public vtkPolyData Surface { get; init; } = vtkPolyData.New();

    public Point3D BasePoint { get; init; }

    public Vector3D Normal { get; init; } = new(0, 0, 1);
}

public sealed class FemExportRequest
{
    public vtkUnstructuredGrid VolumeGrid { get; init; } = vtkUnstructuredGrid.New();

    public vtkPolyData? BodySurface { get; init; }

    public IReadOnlyList<SurfaceMeshInfo> RoiSurfaces { get; init; } = Array.Empty<SurfaceMeshInfo>();

    public IReadOnlyList<FemElectrodeExportInfo> Electrodes { get; init; } = Array.Empty<FemElectrodeExportInfo>();

    public ElectrodeSpec ElectrodeSpec { get; init; } = new();

    public string OutputDirectory { get; init; } = string.Empty;

    public string BaseName { get; init; } = "meshgen_fem";
}

public sealed class FemExportResult
{
    public string OutputDirectory { get; init; } = string.Empty;

    public string BaseName { get; init; } = string.Empty;

    public string VtuPath { get; init; } = string.Empty;

    public string NodesPath { get; init; } = string.Empty;

    public string ElementsPath { get; init; } = string.Empty;

    public string BoundaryFacesPath { get; init; } = string.Empty;

    public string MaterialsPath { get; init; } = string.Empty;

    public string ManifestPath { get; init; } = string.Empty;

    public int NodeCount { get; init; }

    public int TetrahedronCount { get; init; }

    public int BoundaryFaceCount { get; init; }

    public int RegionCount { get; init; }

    public int MaterialCount { get; init; }

    public string Summary =>
        $"{TetrahedronCount:N0} tetrahedra, {BoundaryFaceCount:N0} boundary faces, {MaterialCount:N0} materials";
}
