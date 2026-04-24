using System.Windows.Media;
using System.Windows.Media.Media3D;
using Kitware.VTK;
using MeshGenLite.Models;
using WpfColor = System.Windows.Media.Color;

namespace MeshGenLite.Services;

public sealed class VtkViewportService
{
    private readonly Dictionary<int, vtkActor> roiActors = new();
    private readonly Dictionary<int, vtkPolyData> roiSurfaces = new();
    private readonly Dictionary<int, vtkActor> committedElectrodeActors = new();
    private vtkActor? previewActor;
    private vtkActor? mergedPreviewActor;
    private vtkActor? externalActor;
    private vtkPolyData? externalSurface;
    private int? externalBodyMeshId;
    private RenderWindowControl? renderControl;
    private vtkRenderer? renderer;

    public bool IsInitialized => renderControl is not null && renderer is not null;

    public bool HasExternalSurface => externalActor is not null && externalSurface is not null;

    public int? ExternalBodyMeshId => externalBodyMeshId;

    public void Initialize(RenderWindowControl control)
    {
        renderControl = control;
        renderer = control.RenderWindow.GetRenderers().GetFirstRenderer();
        if (renderer is null)
        {
            renderer = vtkRenderer.New();
            control.RenderWindow.AddRenderer(renderer);
        }

        renderer.GradientBackgroundOn();
        renderer.SetBackground(0.06, 0.06, 0.07);
        renderer.SetBackground2(0.11, 0.12, 0.15);
        control.RenderWindow.Render();
    }

    public void ResetCamera()
    {
        if (renderer is null)
        {
            return;
        }

        renderer.ResetCamera();
        Render();
    }

    public void Clear()
    {
        if (renderer is null)
        {
            return;
        }

        foreach (var actor in roiActors.Values)
        {
            renderer.RemoveActor(actor);
        }

        foreach (var actor in committedElectrodeActors.Values)
        {
            renderer.RemoveActor(actor);
        }

        if (previewActor is not null)
        {
            renderer.RemoveActor(previewActor);
        }

        if (mergedPreviewActor is not null)
        {
            renderer.RemoveActor(mergedPreviewActor);
        }

        roiActors.Clear();
        roiSurfaces.Clear();
        committedElectrodeActors.Clear();
        previewActor = null;
        externalActor = null;
        externalSurface = null;
        externalBodyMeshId = null;
        mergedPreviewActor = null;
        Render();
    }

    public void ShowSurfaceMeshes(IEnumerable<SurfaceMeshInfo> surfaces, int? externalRoiId)
    {
        if (renderer is null)
        {
            return;
        }

        foreach (var actor in roiActors.Values)
        {
            renderer.RemoveActor(actor);
        }

        roiActors.Clear();
        roiSurfaces.Clear();
        SetExternalSurfaceRoi(null);

        var surfaceList = surfaces as IList<SurfaceMeshInfo> ?? surfaces.ToList();
        foreach (var surface in surfaceList)
        {
            var mapper = vtkPolyDataMapper.New();
            mapper.SetInputData(surface.PolyData);
            mapper.ScalarVisibilityOff();
            mapper.Update();

            var actor = vtkActor.New();
            actor.SetVisibility(1);
            actor.PickableOn();
            actor.SetMapper(mapper);
            renderer.AddActor(actor);
            roiActors[surface.Roi.Id] = actor;
            roiSurfaces[surface.Roi.Id] = surface.PolyData;
        }

        foreach (var surface in surfaceList)
        {
            if (roiActors.TryGetValue(surface.Roi.Id, out var actor))
            {
                ApplyAppearance(actor, surface.Roi);
            }
        }

        SetExternalSurfaceRoi(externalRoiId);
        ResetCamera();
        Render();
    }

    public void SetExternalSurfaceRoi(int? roiId)
    {
        if (roiId is not int resolvedId)
        {
            externalActor = null;
            externalSurface = null;
            externalBodyMeshId = null;
            return;
        }

        if (roiActors.TryGetValue(resolvedId, out var actor) &&
            roiSurfaces.TryGetValue(resolvedId, out var surface))
        {
            externalActor = actor;
            externalSurface = surface;
            externalBodyMeshId = resolvedId;
            return;
        }

        externalActor = null;
        externalSurface = null;
        externalBodyMeshId = null;
    }

    public void UpdateRoiAppearance(RoiDescriptor roi)
    {
        if (roiActors.TryGetValue(roi.Id, out var actor))
        {
            ApplyAppearance(actor, roi);
            Render();
        }
    }

    public void ShowPlacementPreview(vtkPolyData preview, WpfColor color, double opacity)
    {
        if (renderer is null)
        {
            return;
        }

        if (previewActor is null)
        {
            var mapper = vtkPolyDataMapper.New();
            previewActor = vtkActor.New();
            previewActor.SetMapper(mapper);
            renderer.AddActor(previewActor);
        }

        var previewMapper = vtkPolyDataMapper.SafeDownCast(previewActor.GetMapper()) ?? vtkPolyDataMapper.New();
        previewMapper.SetInputData(preview);
        previewMapper.ScalarVisibilityOff();
        previewMapper.Update();
        previewActor.SetMapper(previewMapper);
        previewActor.SetVisibility(1);

        var property = previewActor.GetProperty();
        ApplyColor(property, color);
        property.SetOpacity(Math.Max(opacity, 0.55));
        property.SetRepresentationToSurface();
        property.EdgeVisibilityOn();
        property.SetEdgeColor(1.0, 1.0, 1.0);
        ApplyMaterialDefaults(property);
        property.SetAmbient(0.65);
        property.SetDiffuse(0.35);
        property.Modified();
        Render();
    }

    public void HidePlacementPreview()
    {
        if (previewActor is null)
        {
            return;
        }

        previewActor.SetVisibility(0);
        Render();
    }

    public void AddCommittedElectrode(int placementId, vtkPolyData electrode, WpfColor color, double opacity)
    {
        if (renderer is null)
        {
            return;
        }

        if (committedElectrodeActors.TryGetValue(placementId, out var existingActor))
        {
            renderer.RemoveActor(existingActor);
        }

        var mapper = vtkPolyDataMapper.New();
        mapper.SetInputData(electrode);
        mapper.ScalarVisibilityOff();
        mapper.Update();

        var actor = vtkActor.New();
        actor.SetVisibility(1);
        actor.SetMapper(mapper);
        renderer.AddActor(actor);
        committedElectrodeActors[placementId] = actor;

        var property = actor.GetProperty();
        ApplyColor(property, color);
        property.SetOpacity(opacity);
        ApplyMaterialDefaults(property);
        property.Modified();
        Render();
    }

    public void RemoveCommittedElectrode(int placementId)
    {
        if (renderer is null || !committedElectrodeActors.TryGetValue(placementId, out var actor))
        {
            return;
        }

        renderer.RemoveActor(actor);
        committedElectrodeActors.Remove(placementId);
        Render();
    }

    public void ClearCommittedElectrodes()
    {
        if (renderer is null)
        {
            return;
        }

        foreach (var actor in committedElectrodeActors.Values)
        {
            renderer.RemoveActor(actor);
        }

        committedElectrodeActors.Clear();
        Render();
    }

    public void ShowElectrodes(IEnumerable<vtkPolyData> electrodes, WpfColor color)
    {
        ClearCommittedElectrodes();
        var nextId = 1;
        foreach (var electrode in electrodes)
        {
            AddCommittedElectrode(nextId++, electrode, color, 0.8);
        }
    }

    public void ShowMergedPreview(vtkPolyData preview, WpfColor color)
    {
        if (renderer is null)
        {
            return;
        }

        if (mergedPreviewActor is not null)
        {
            renderer.RemoveActor(mergedPreviewActor);
        }

        var mapper = vtkPolyDataMapper.New();
        mapper.SetInputData(preview);
        mapper.ScalarVisibilityOff();
        mapper.Update();

        mergedPreviewActor = vtkActor.New();
        mergedPreviewActor.SetVisibility(1);
        mergedPreviewActor.SetMapper(mapper);
        renderer.AddActor(mergedPreviewActor);

        var previewProperty = mergedPreviewActor.GetProperty();
        ApplyColor(previewProperty, color);
        previewProperty.SetOpacity(0.65);
        ApplyMaterialDefaults(previewProperty);
        previewProperty.Modified();
        Render();
    }

    public bool TryPickExternalHit(int screenX, int screenY, out MeshHitResult result)
    {
        result = new MeshHitResult { Success = false };
        if (renderer is null || externalActor is null || externalSurface is null || renderControl is null)
        {
            return false;
        }

        var picker = vtkCellPicker.New();
        picker.SetTolerance(0.0005);
        picker.PickFromListOn();
        picker.AddPickList(externalActor);
        var viewportY = Math.Max(0, renderControl.Height - screenY - 1);
        picker.Pick(screenX, viewportY, 0, renderer);
        if (picker.GetCellId() < 0)
        {
            return false;
        }

        var position = picker.GetPickPosition();
        var normal = ResolvePickNormal(picker, externalSurface, picker.GetCellId(), position);
        result = new MeshHitResult
        {
            Success = true,
            Position = new Point3D(position[0], position[1], position[2]),
            Normal = normal,
            CellId = picker.GetCellId()
        };

        return true;
    }

    private static Vector3D ResolvePickNormal(vtkCellPicker picker, vtkPolyData surface, long cellId, double[] pickedPosition)
    {
        Vector3D? pickedNormal = null;
        try
        {
            var tuple = picker.GetPickNormal();
            pickedNormal = new Vector3D(tuple[0], tuple[1], tuple[2]);
            if (pickedNormal.Value.LengthSquared > 1e-8)
            {
                pickedNormal = NormalizeAndOrientOutward(pickedNormal.Value, surface, pickedPosition);
                return pickedNormal.Value;
            }
        }
        catch
        {
            // Fall back to cell-based normal if the wrapper does not expose picker normals.
        }

        var cell = surface.GetCell(cellId);
        if (cell is null || cell.GetPoints() is null || cell.GetPoints().GetNumberOfPoints() < 3)
        {
            return new Vector3D(0, 0, 1);
        }

        var points = cell.GetPoints();
        var p0 = points.GetPoint(0);
        var p1 = points.GetPoint(1);
        var p2 = points.GetPoint(2);
        var edgeA = new Vector3D(p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]);
        var edgeB = new Vector3D(p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]);
        var normal = Vector3D.CrossProduct(edgeA, edgeB);
        if (normal.LengthSquared < 1e-8)
        {
            return new Vector3D(0, 0, 1);
        }

        return NormalizeAndOrientOutward(normal, surface, pickedPosition);
    }

    private static Vector3D NormalizeAndOrientOutward(Vector3D normal, vtkPolyData surface, double[] pickedPosition)
    {
        if (normal.LengthSquared < 1e-8)
        {
            return new Vector3D(0, 0, 1);
        }

        normal.Normalize();

        var bounds = surface.GetBounds();
        var center = new Point3D(
            (bounds[0] + bounds[1]) * 0.5,
            (bounds[2] + bounds[3]) * 0.5,
            (bounds[4] + bounds[5]) * 0.5);

        var outwardHint = new Vector3D(
            pickedPosition[0] - center.X,
            pickedPosition[1] - center.Y,
            pickedPosition[2] - center.Z);

        if (outwardHint.LengthSquared > 1e-8 && Vector3D.DotProduct(normal, outwardHint) < 0.0)
        {
            normal *= -1.0;
        }

        return normal;
    }

    private static void ApplyAppearance(vtkActor actor, RoiDescriptor roi)
    {
        var property = actor.GetProperty();
        ApplyColor(property, roi.Color);
        property.SetOpacity(roi.Opacity);
        ApplyMaterialDefaults(property);
        property.Modified();
        actor.SetVisibility(roi.Visibility ? 1 : 0);
    }

    private static void ApplyMaterialDefaults(vtkProperty property)
    {
        property.SetAmbient(0.22);
        property.SetDiffuse(0.88);
        property.SetSpecular(0.0);
        property.SetSpecularPower(1);
        property.SetInterpolationToGouraud();
    }

    private static void ApplyColor(vtkProperty property, WpfColor color)
    {
        var red = color.R / 255.0;
        var green = color.G / 255.0;
        var blue = color.B / 255.0;

        property.SetColor(red, green, blue);
        property.SetAmbientColor(red, green, blue);
        property.SetDiffuseColor(red, green, blue);
        property.SetSpecularColor(red, green, blue);
    }

    private void Render()
    {
        renderControl?.RenderWindow.Render();
    }
}
