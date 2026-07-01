using System.Windows.Media;
using System.Windows.Media.Media3D;
using Kitware.VTK;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;
using WpfColor = System.Windows.Media.Color;

namespace MeshGenLite.Services;

public sealed class VtkViewportService
{
    private readonly Dictionary<int, vtkActor> roiActors = new();
    private readonly Dictionary<int, vtkPolyData> roiSurfaces = new();
    private readonly Dictionary<int, vtkActor> committedElectrodeActors = new();
    private readonly Dictionary<int, vtkPolyData> committedElectrodeSurfaces = new();
    private readonly HashSet<int> contactPatchVertexIds = new();
    private vtkActor? previewActor;
    private vtkActor? mergedPreviewActor;
    private vtkActor? externalActor;
    private vtkPolyData? externalSurface;
    private int? externalBodyMeshId;
    private vtkActor? deformedBodyActor;
    private vtkPolyDataMapper? deformedBodyMapper;
    private vtkPolyData? originalBodySurfaceBackup;
    private vtkPolyData? deformedBodySurface;
    private vtkActor? contactPatchActor;
    private vtkPolyDataMapper? contactPatchMapper;
    private vtkPolyData? contactPatchPolyData;
    private bool showContactPatch;
    private bool showDeformedBody;
    private bool externalOriginalVisibility = true;
    private RenderWindowControl? renderControl;
    private vtkRenderer? renderer;

    public bool IsInitialized => renderControl is not null && renderer is not null;

    public bool HasExternalSurface => externalActor is not null && externalSurface is not null;

    public bool HasAttachmentVisualization => deformedBodySurface is not null;

    public bool IsShowingDeformedBody => showDeformedBody && deformedBodySurface is not null;

    public bool IsContactPatchVisible => showContactPatch && contactPatchActor is not null && contactPatchActor.GetVisibility() == 1;

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

        ClearAttachmentVisuals();
        roiActors.Clear();
        roiSurfaces.Clear();
        committedElectrodeActors.Clear();
        committedElectrodeSurfaces.Clear();
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

        ClearAttachmentVisuals();
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

    /// <summary>
    /// Replace (or add) the surface actor for a single ROI in place, keeping electrodes and
    /// other ROI actors untouched. Used for incremental regeneration after contour editing.
    /// </summary>
    public void ReplaceRoiSurface(SurfaceMeshInfo surface)
    {
        if (renderer is null)
        {
            return;
        }

        if (roiActors.TryGetValue(surface.Roi.Id, out var existing))
        {
            renderer.RemoveActor(existing);
        }

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
        ApplyAppearance(actor, surface.Roi);

        if (externalBodyMeshId == surface.Roi.Id)
        {
            externalActor = actor;
            externalSurface = surface.PolyData;
            externalOriginalVisibility = surface.Roi.Visibility;
            ApplyExternalVisibility();
        }

        Render();
    }

    public void RemoveRoiSurface(int roiId)
    {
        if (renderer is null || !roiActors.TryGetValue(roiId, out var actor))
        {
            return;
        }

        renderer.RemoveActor(actor);
        roiActors.Remove(roiId);
        roiSurfaces.Remove(roiId);
        Render();
    }

    public void SetExternalSurfaceRoi(int? roiId)
    {
        if (roiId is not int resolvedId)
        {
            externalActor = null;
            externalSurface = null;
            externalBodyMeshId = null;
            externalOriginalVisibility = true;
            return;
        }

        if (roiActors.TryGetValue(resolvedId, out var actor) &&
            roiSurfaces.TryGetValue(resolvedId, out var surface))
        {
            externalActor = actor;
            externalSurface = surface;
            externalBodyMeshId = resolvedId;
            externalOriginalVisibility = actor.GetVisibility() == 1;
            ApplyExternalVisibility();
            return;
        }

        externalActor = null;
        externalSurface = null;
        externalBodyMeshId = null;
        externalOriginalVisibility = true;
    }

    public void UpdateRoiAppearance(RoiDescriptor roi)
    {
        if (!roiActors.TryGetValue(roi.Id, out var actor))
        {
            return;
        }

        ApplyAppearance(actor, roi);
        if (externalActor == actor)
        {
            externalOriginalVisibility = roi.Visibility;
            ApplyExternalVisibility();
            SyncDeformedAppearanceFromExternal();
        }

        Render();
    }

    public void ShowAttachmentResult(vtkPolyData originalBody, vtkPolyData deformedBody, IReadOnlyCollection<int> vertexIds)
    {
        if (renderer is null)
        {
            return;
        }

        originalBodySurfaceBackup = originalBody;
        deformedBodySurface = deformedBody;
        contactPatchVertexIds.Clear();
        foreach (var vertexId in vertexIds)
        {
            contactPatchVertexIds.Add(vertexId);
        }

        if (deformedBodyActor is null)
        {
            deformedBodyActor = vtkActor.New();
            deformedBodyActor.PickableOff();
            renderer.AddActor(deformedBodyActor);
        }

        if (deformedBodyMapper is null)
        {
            deformedBodyMapper = vtkPolyDataMapper.New();
            deformedBodyMapper.ScalarVisibilityOff();
            deformedBodyActor.SetMapper(deformedBodyMapper);
        }

        deformedBodyMapper.SetInputData(deformedBody);
        deformedBodyMapper.Update();
        SyncDeformedAppearanceFromExternal();

        showDeformedBody = true;
        showContactPatch = true;
        ApplyExternalVisibility();
        RebuildContactPatchActor();
        Render();
    }

    public void ClearAttachmentVisuals()
    {
        if (renderer is null)
        {
            originalBodySurfaceBackup = null;
            deformedBodySurface = null;
            showContactPatch = false;
            showDeformedBody = false;
            contactPatchVertexIds.Clear();
            return;
        }

        if (deformedBodyActor is not null)
        {
            renderer.RemoveActor(deformedBodyActor);
            deformedBodyActor = null;
        }
        deformedBodyMapper = null;

        if (contactPatchActor is not null)
        {
            renderer.RemoveActor(contactPatchActor);
            contactPatchActor = null;
        }
        contactPatchMapper = null;
        contactPatchPolyData = null;

        originalBodySurfaceBackup = null;
        deformedBodySurface = null;
        showContactPatch = false;
        showDeformedBody = false;
        contactPatchVertexIds.Clear();
        ApplyExternalVisibility();
        Render();
    }

    public void ShowContactPatch()
    {
        showContactPatch = true;
        RebuildContactPatchActor();
        Render();
    }

    public void HideContactPatch()
    {
        showContactPatch = false;
        if (contactPatchActor is not null)
        {
            contactPatchActor.SetVisibility(0);
        }

        Render();
    }

    public void ShowDeformedBody()
    {
        showDeformedBody = true;
        ApplyExternalVisibility();
        RebuildContactPatchActor();
        Render();
    }

    public void ShowOriginalBody()
    {
        showDeformedBody = false;
        ApplyExternalVisibility();
        RebuildContactPatchActor();
        Render();
    }

    public void ShowPlacementPreview(vtkPolyData preview, WpfColor color, double opacity, IReadOnlyList<WpfColor>? layerColors = null)
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
        ConfigureElectrodeMapper(previewMapper, layerColors);
        previewMapper.Update();
        previewActor.SetMapper(previewMapper);
        previewActor.SetVisibility(1);

        var property = previewActor.GetProperty();
        if (layerColors is null || layerColors.Count == 0)
        {
            ApplyColor(property, color);
        }

        property.SetOpacity(Math.Clamp(opacity, 0.2, 1.0));
        property.SetRepresentationToSurface();
        property.EdgeVisibilityOn();
        property.SetEdgeColor(1.0, 1.0, 1.0);
        ApplyElectrodeMaterial(property);
        property.SetAmbient(0.45);
        property.SetDiffuse(0.55);
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

    public void AddCommittedElectrode(
        int placementId,
        vtkPolyData electrode,
        WpfColor color,
        double opacity,
        IReadOnlyList<WpfColor>? layerColors = null)
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
        ConfigureElectrodeMapper(mapper, layerColors);
        mapper.Update();

        var actor = vtkActor.New();
        actor.SetVisibility(1);
        actor.SetMapper(mapper);
        renderer.AddActor(actor);
        committedElectrodeActors[placementId] = actor;
        committedElectrodeSurfaces[placementId] = electrode;

        var property = actor.GetProperty();
        if (layerColors is null || layerColors.Count == 0)
        {
            ApplyColor(property, color);
        }

        property.SetOpacity(Math.Clamp(opacity, 0.0, 1.0));
        ApplyElectrodeMaterial(property);
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
        committedElectrodeSurfaces.Remove(placementId);
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
        committedElectrodeSurfaces.Clear();
        Render();
    }

    public IReadOnlyCollection<vtkPolyData> GetCommittedElectrodeSurfaces()
        => committedElectrodeSurfaces.Values;

    public IReadOnlyDictionary<int, vtkPolyData> GetCommittedElectrodeSurfacesById()
        => committedElectrodeSurfaces;

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

    private void SyncDeformedAppearanceFromExternal()
    {
        if (deformedBodyActor is null)
        {
            return;
        }

        if (externalActor is not null)
        {
            deformedBodyActor.GetProperty().DeepCopy(externalActor.GetProperty());
        }
        else
        {
            ApplyMaterialDefaults(deformedBodyActor.GetProperty());
            deformedBodyActor.GetProperty().SetOpacity(0.95);
        }

        deformedBodyActor.GetProperty().Modified();
    }

    private void ApplyExternalVisibility()
    {
        if (externalActor is not null)
        {
            var shouldShowOriginal = deformedBodySurface is null || !showDeformedBody;
            externalActor.SetVisibility(shouldShowOriginal && externalOriginalVisibility ? 1 : 0);
        }

        if (deformedBodyActor is not null)
        {
            deformedBodyActor.SetVisibility(showDeformedBody ? 1 : 0);
        }
    }

    private void RebuildContactPatchActor()
    {
        if (renderer is null || !showContactPatch || contactPatchVertexIds.Count == 0)
        {
            if (contactPatchActor is not null)
            {
                contactPatchActor.SetVisibility(0);
            }

            return;
        }

        var source = showDeformedBody
            ? deformedBodySurface
            : originalBodySurfaceBackup ?? externalSurface;
        if (source is null)
        {
            return;
        }

        contactPatchPolyData = MeshConverter.CreateVertexSubset(source, contactPatchVertexIds);

        if (contactPatchActor is null)
        {
            contactPatchActor = vtkActor.New();
            renderer.AddActor(contactPatchActor);
        }

        if (contactPatchMapper is null)
        {
            contactPatchMapper = vtkPolyDataMapper.New();
            contactPatchMapper.ScalarVisibilityOff();
            contactPatchActor.SetMapper(contactPatchMapper);
        }

        contactPatchMapper.SetInputData(contactPatchPolyData);
        contactPatchMapper.Update();
        contactPatchActor.SetVisibility(1);

        var property = contactPatchActor.GetProperty();
        property.SetColor(0.95, 0.2, 0.2);
        property.SetOpacity(0.85);
        property.SetRepresentationToSurface();
        property.EdgeVisibilityOn();
        property.SetEdgeColor(1.0, 0.9, 0.9);
        property.SetAmbient(0.3);
        property.SetDiffuse(0.9);
        property.Modified();
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

    private static void ApplyElectrodeMaterial(vtkProperty property)
    {
        property.SetAmbient(0.34);
        property.SetDiffuse(0.76);
        property.SetSpecular(0.12);
        property.SetSpecularPower(18);
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

    private static void ConfigureElectrodeMapper(vtkPolyDataMapper mapper, IReadOnlyList<WpfColor>? layerColors)
    {
        if (layerColors is null || layerColors.Count == 0)
        {
            mapper.ScalarVisibilityOff();
            return;
        }

        var lookupTable = vtkLookupTable.New();
        lookupTable.SetNumberOfTableValues(layerColors.Count);
        lookupTable.SetRange(0, Math.Max(layerColors.Count - 1, 0));
        for (var index = 0; index < layerColors.Count; index++)
        {
            var color = layerColors[index];
            lookupTable.SetTableValue(index, color.R / 255.0, color.G / 255.0, color.B / 255.0, 1.0);
        }

        lookupTable.Build();
        mapper.SetLookupTable(lookupTable);
        mapper.SetScalarRange(0, Math.Max(layerColors.Count - 1, 0));
        mapper.SetScalarModeToUseCellData();
        mapper.ScalarVisibilityOn();
    }

    private void Render()
    {
        renderControl?.RenderWindow.Render();
    }
}
