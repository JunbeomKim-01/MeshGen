using Kitware.VTK;
using MeshGenLite.Models;
using System.Windows.Media.Media3D;

namespace MeshGenLite.Services;

public static class FemMetadataService
{
    public static void TagRoiSurface(vtkPolyData surface, RoiDescriptor roi, bool isExternal)
    {
        var regionId = isExternal ? FemIds.BodyRegion(roi.Id) : roi.Id;
        var materialId = isExternal ? FemIds.DefaultBodyMaterialId : FemIds.BodyMaterial(roi.Id);
        var boundaryMarker = FemIds.RoiBoundary(roi.Id, isExternal);

        AddOrReplaceConstantCellArray(surface, FemArrayNames.RegionId, regionId);
        AddOrReplaceConstantCellArray(surface, FemArrayNames.MaterialId, materialId);
        AddOrReplaceConstantCellArray(surface, FemArrayNames.BoundaryMarker, boundaryMarker);
        AddOrReplaceConstantCellArray(surface, FemArrayNames.SourceRoiId, roi.Id);
    }

    public static void TagElectrodeSurface(
        vtkPolyData surface,
        int placementId,
        ElectrodeSpec spec,
        Point3D? basePoint = null,
        Vector3D? normal = null)
    {
        var nCells = (int)surface.GetNumberOfCells();
        if (nCells == 0)
        {
            return;
        }

        var layerArray = ResolveCellArray(surface, FemArrayNames.LayerIndex);
        var regionIds = vtkIntArray.New();
        regionIds.SetName(FemArrayNames.RegionId);
        regionIds.SetNumberOfComponents(1);
        var materialIds = vtkIntArray.New();
        materialIds.SetName(FemArrayNames.MaterialId);
        materialIds.SetNumberOfComponents(1);
        var boundaryMarkers = vtkIntArray.New();
        boundaryMarkers.SetName(FemArrayNames.BoundaryMarker);
        boundaryMarkers.SetNumberOfComponents(1);

        var layerCount = Math.Max(spec.Layers.Count, 1);
        for (var cellId = 0; cellId < nCells; cellId++)
        {
            var layerIndex = layerArray is null || layerArray.GetNumberOfTuples() <= cellId
                ? 0
                : Clamp((int)Math.Round(layerArray.GetTuple1(cellId)), 0, layerCount - 1);
            var regionId = FemIds.ElectrodeLayerRegion(placementId, layerIndex);
            regionIds.InsertNextTuple1(regionId);
            materialIds.InsertNextTuple1(regionId);
            boundaryMarkers.InsertNextTuple1(ResolveElectrodeBoundaryMarker(surface, placementId, cellId, basePoint, normal));
        }

        ReplaceCellArray(surface, regionIds);
        ReplaceCellArray(surface, materialIds);
        ReplaceCellArray(surface, boundaryMarkers);
    }

    public static bool HasRequiredSurfaceMetadata(vtkPolyData surface)
        => ResolveCellArray(surface, FemArrayNames.RegionId) is not null &&
           ResolveCellArray(surface, FemArrayNames.MaterialId) is not null &&
           ResolveCellArray(surface, FemArrayNames.BoundaryMarker) is not null;

    private static int ResolveElectrodeBoundaryMarker(
        vtkPolyData surface,
        int placementId,
        int cellId,
        Point3D? basePoint,
        Vector3D? normal)
    {
        var cell = surface.GetCell(cellId);
        if (cell is null || cell.GetPoints() is null || cell.GetPoints().GetNumberOfPoints() == 0)
        {
            return FemIds.ElectrodeOuterBoundary(placementId);
        }

        var points = cell.GetPoints();
        if (basePoint is Point3D resolvedBase && normal is Vector3D resolvedNormal && resolvedNormal.LengthSquared > 1e-8)
        {
            resolvedNormal.Normalize();
            var axial = 0.0;
            for (var i = 0; i < points.GetNumberOfPoints(); i++)
            {
                var point = points.GetPoint(i);
                axial += Vector3D.DotProduct(
                    new Vector3D(point[0] - resolvedBase.X, point[1] - resolvedBase.Y, point[2] - resolvedBase.Z),
                    resolvedNormal);
            }

            axial /= points.GetNumberOfPoints();
            return axial <= 0.25
                ? FemIds.ElectrodeContactBoundary(placementId)
                : FemIds.ElectrodeOuterBoundary(placementId);
        }

        var bounds = surface.GetBounds();
        var totalZ = bounds[5] - bounds[4];
        if (totalZ <= 1e-8)
        {
            return FemIds.ElectrodeOuterBoundary(placementId);
        }

        var z = 0.0;
        for (var i = 0; i < points.GetNumberOfPoints(); i++)
        {
            z += points.GetPoint(i)[2];
        }
        z /= points.GetNumberOfPoints();

        var normalized = (z - bounds[4]) / totalZ;
        return normalized <= 0.08
            ? FemIds.ElectrodeContactBoundary(placementId)
            : FemIds.ElectrodeOuterBoundary(placementId);
    }

    private static void AddOrReplaceConstantCellArray(vtkPolyData surface, string name, int value)
    {
        var array = vtkIntArray.New();
        array.SetName(name);
        array.SetNumberOfComponents(1);
        var nCells = (int)surface.GetNumberOfCells();
        for (var i = 0; i < nCells; i++)
        {
            array.InsertNextTuple1(value);
        }

        ReplaceCellArray(surface, array);
    }

    private static void ReplaceCellArray(vtkPolyData surface, vtkIntArray array)
    {
        var cellData = surface.GetCellData();
        var existing = cellData.GetArray(array.GetName());
        if (existing is not null)
        {
            cellData.RemoveArray(array.GetName());
        }

        cellData.AddArray(array);
        if (array.GetName() == FemArrayNames.LayerIndex)
        {
            cellData.SetScalars(array);
        }
        surface.Modified();
    }

    private static vtkDataArray? ResolveCellArray(vtkPolyData surface, string name)
    {
        var cellData = surface.GetCellData();
        var array = cellData.GetArray(name);
        if (array is not null)
        {
            return array;
        }

        var scalars = cellData.GetScalars();
        return scalars is not null && scalars.GetName() == name ? scalars : null;
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;
}
