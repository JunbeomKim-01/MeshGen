using Kitware.VTK;
using MeshGenLite.Models;

namespace MeshGenLite.Services;

public enum MprPlane
{
    Axial = 0,
    Sagittal = 1,
    Coronal = 2
}

/// <summary>
/// Renders the shared label map as three orthogonal 2D slices (axial/sagittal/coronal)
/// with a per-ROI colour overlay, corner annotations, and converts panel clicks into
/// label-map voxel indices. The label map itself stays the single source of truth.
/// </summary>
public sealed class MprSliceService
{
    private const double FillAlpha = 0.85;

    private sealed class Panel
    {
        public RenderWindowControl? Control;
        public vtkRenderer? Renderer;
        public vtkImageSlice? ImageSlice;
        public vtkImageSliceMapper? SliceMapper;
        public vtkImageMapToColors? MapToColors;
        public vtkCornerAnnotation? Annotation;
        public vtkRegularPolygonSource? BrushSource;
        public vtkActor? BrushActor;
        public int Slice;
        public string Title = string.Empty;
    }

    private readonly Panel[] panels =
    {
        new() { Title = "Axial" },
        new() { Title = "Sagittal" },
        new() { Title = "Coronal" }
    };

    private vtkImageData? labelMap;
    private vtkLookupTable? lookupTable;
    private double[] origin = { 0, 0, 0 };
    private double[] spacing = { 1, 1, 1 };
    private int[] extent = { 0, 0, 0, 0, 0, 0 };

    public bool HasVolume => labelMap is not null;

    public void Initialize(RenderWindowControl axial, RenderWindowControl sagittal, RenderWindowControl coronal)
    {
        InitializePanel(panels[(int)MprPlane.Axial], axial);
        InitializePanel(panels[(int)MprPlane.Sagittal], sagittal);
        InitializePanel(panels[(int)MprPlane.Coronal], coronal);
    }

    private static void InitializePanel(Panel panel, RenderWindowControl control)
    {
        panel.Control = control;
        var renderer = control.RenderWindow.GetRenderers().GetFirstRenderer();
        if (renderer is null)
        {
            renderer = vtkRenderer.New();
            control.RenderWindow.AddRenderer(renderer);
        }

        renderer.SetBackground(0.02, 0.02, 0.03);
        panel.Renderer = renderer;

        var annotation = vtkCornerAnnotation.New();
        annotation.SetLinearFontScaleFactor(2);
        annotation.SetNonlinearFontScaleFactor(1);
        annotation.SetMaximumFontSize(16);
        annotation.GetTextProperty().SetColor(1.0, 0.85, 0.3);
        annotation.SetText(2, panel.Title);
        renderer.AddViewProp(annotation);
        panel.Annotation = annotation;

        var interactor = control.RenderWindow.GetInteractor();
        if (interactor is not null)
        {
            interactor.SetInteractorStyle(vtkInteractorStyleImage.New());
        }

        control.RenderWindow.Render();
    }

    /// <summary>
    /// When editing (paint/erase), disable the image interactor so left-drag no longer performs
    /// window/level; our own WinForms mouse handlers drive the brush. Restore pan/zoom otherwise.
    /// </summary>
    public void SetEditInteraction(bool editing)
    {
        foreach (var panel in panels)
        {
            var interactor = panel.Control?.RenderWindow.GetInteractor();
            if (interactor is null)
            {
                continue;
            }

            if (editing)
            {
                interactor.SetInteractorStyle(vtkInteractorStyleUser.New());
            }
            else
            {
                interactor.SetInteractorStyle(vtkInteractorStyleImage.New());
            }
        }
    }

    public int GetSliceCount(MprPlane plane) => plane switch
    {
        MprPlane.Axial => extent[5] - extent[4] + 1,
        MprPlane.Sagittal => extent[1] - extent[0] + 1,
        MprPlane.Coronal => extent[3] - extent[2] + 1,
        _ => 0
    };

    public int GetSlice(MprPlane plane) => panels[(int)plane].Slice;

    public void SetLabelMap(vtkImageData map, IReadOnlyList<RoiDescriptor> rois)
    {
        labelMap = map;
        origin = map.GetOrigin();
        spacing = map.GetSpacing();
        extent = map.GetExtent();

        BuildLookupTable(rois);

        for (var i = 0; i < panels.Length; i++)
        {
            var plane = (MprPlane)i;
            AttachPanelPipeline(panels[i], plane);
            panels[i].Slice = GetSliceCount(plane) / 2;
            ApplySlice(panels[i], plane);
            SetupCamera(panels[i], plane);
            UpdateAnnotation(panels[i], plane);
        }

        RenderAll();
    }

    private void AttachPanelPipeline(Panel panel, MprPlane plane)
    {
        if (panel.Renderer is null || labelMap is null || lookupTable is null)
        {
            return;
        }

        var mapToColors = vtkImageMapToColors.New();
        mapToColors.SetLookupTable(lookupTable);
        mapToColors.SetOutputFormatToRGBA();
        mapToColors.SetInputData(labelMap);

        var mapper = vtkImageSliceMapper.New();
        mapper.SetInputConnection(mapToColors.GetOutputPort());
        switch (plane)
        {
            case MprPlane.Axial:
                mapper.SetOrientationToZ();
                break;
            case MprPlane.Sagittal:
                mapper.SetOrientationToX();
                break;
            case MprPlane.Coronal:
                mapper.SetOrientationToY();
                break;
        }

        if (panel.ImageSlice is not null)
        {
            panel.Renderer.RemoveViewProp(panel.ImageSlice);
        }

        var imageSlice = vtkImageSlice.New();
        imageSlice.SetMapper(mapper);
        imageSlice.GetProperty().SetInterpolationTypeToNearest();
        panel.Renderer.AddViewProp(imageSlice);

        panel.MapToColors = mapToColors;
        panel.SliceMapper = mapper;
        panel.ImageSlice = imageSlice;
    }

    public void SetSlice(MprPlane plane, int index)
    {
        if (labelMap is null)
        {
            return;
        }

        var count = GetSliceCount(plane);
        if (count <= 0)
        {
            return;
        }

        var panel = panels[(int)plane];
        panel.Slice = Math.Clamp(index, 0, count - 1);
        ApplySlice(panel, plane);
        UpdateAnnotation(panel, plane);
        panel.Control?.RenderWindow.Render();
    }

    private void ApplySlice(Panel panel, MprPlane plane)
    {
        if (panel.SliceMapper is null)
        {
            return;
        }

        var sliceNumber = plane switch
        {
            MprPlane.Axial => extent[4] + panel.Slice,
            MprPlane.Sagittal => extent[0] + panel.Slice,
            MprPlane.Coronal => extent[2] + panel.Slice,
            _ => 0
        };

        panel.SliceMapper.SetSliceNumber(sliceNumber);
    }

    private void UpdateAnnotation(Panel panel, MprPlane plane)
    {
        if (panel.Annotation is null)
        {
            return;
        }

        var count = GetSliceCount(plane);
        panel.Annotation.SetText(2, $"{panel.Title}\nSlice: {panel.Slice + 1} / {count}");
    }

    private void SetupCamera(Panel panel, MprPlane plane)
    {
        if (panel.Renderer is null)
        {
            return;
        }

        var cx = origin[0] + (extent[0] + extent[1]) * 0.5 * spacing[0];
        var cy = origin[1] + (extent[2] + extent[3]) * 0.5 * spacing[1];
        var cz = origin[2] + (extent[4] + extent[5]) * 0.5 * spacing[2];

        var spanX = (extent[1] - extent[0] + 1) * spacing[0];
        var spanY = (extent[3] - extent[2] + 1) * spacing[1];
        var spanZ = (extent[5] - extent[4] + 1) * spacing[2];
        var dist = Math.Max(spanX, Math.Max(spanY, spanZ)) * 3.0 + 1.0;

        var camera = panel.Renderer.GetActiveCamera();
        camera.ParallelProjectionOn();
        camera.SetFocalPoint(cx, cy, cz);

        switch (plane)
        {
            case MprPlane.Axial:
                camera.SetPosition(cx, cy, cz + dist);
                camera.SetViewUp(0, -1, 0);
                break;
            case MprPlane.Sagittal:
                camera.SetPosition(cx + dist, cy, cz);
                camera.SetViewUp(0, 0, 1);
                break;
            case MprPlane.Coronal:
                camera.SetPosition(cx, cy - dist, cz);
                camera.SetViewUp(0, 0, 1);
                break;
        }

        panel.Renderer.ResetCamera();
    }

    /// <summary>Rebuild the colour table after ROI visibility/colour changes.</summary>
    public void RefreshColors(IReadOnlyList<RoiDescriptor> rois)
    {
        if (labelMap is null)
        {
            return;
        }

        BuildLookupTable(rois);
        foreach (var panel in panels)
        {
            if (panel.MapToColors is null || lookupTable is null)
            {
                continue;
            }

            panel.MapToColors.SetLookupTable(lookupTable);
            panel.MapToColors.Modified();
        }

        RenderAll();
    }

    /// <summary>Re-run the colour mapping after label-map voxels were edited.</summary>
    public void RefreshAfterEdit()
    {
        foreach (var panel in panels)
        {
            panel.MapToColors?.Modified();
        }

        RenderAll();
    }

    /// <summary>
    /// Converts a click on a plane (display coordinates, already Y-flipped to VTK space)
    /// into a label-map voxel and its current label value. The out-of-plane axis is pinned
    /// to the panel's current slice.
    /// </summary>
    public bool TryPickVoxel(MprPlane plane, int displayX, int displayY, out int i, out int j, out int k, out int label)
    {
        i = j = k = 0;
        label = 0;
        var panel = panels[(int)plane];
        if (panel.Renderer is null || labelMap is null)
        {
            return false;
        }

        var picker = vtkWorldPointPicker.New();
        picker.Pick(displayX, displayY, 0, panel.Renderer);
        var world = picker.GetPickPosition();

        var vi = (int)Math.Round((world[0] - origin[0]) / spacing[0]);
        var vj = (int)Math.Round((world[1] - origin[1]) / spacing[1]);
        var vk = (int)Math.Round((world[2] - origin[2]) / spacing[2]);

        switch (plane)
        {
            case MprPlane.Axial:
                vk = extent[4] + panel.Slice;
                break;
            case MprPlane.Sagittal:
                vi = extent[0] + panel.Slice;
                break;
            case MprPlane.Coronal:
                vj = extent[2] + panel.Slice;
                break;
        }

        if (vi < extent[0] || vi > extent[1] ||
            vj < extent[2] || vj > extent[3] ||
            vk < extent[4] || vk > extent[5])
        {
            return false;
        }

        i = vi;
        j = vj;
        k = vk;
        label = (int)Math.Round(labelMap.GetScalarComponentAsDouble(vi, vj, vk, 0));
        return true;
    }

    /// <summary>Draw/update the round brush outline at the cursor on the given plane.</summary>
    public void UpdateBrushCursor(MprPlane plane, int displayX, int displayY, double radiusMm)
    {
        var panel = panels[(int)plane];
        if (panel.Renderer is null || labelMap is null)
        {
            return;
        }

        var picker = vtkWorldPointPicker.New();
        picker.Pick(displayX, displayY, 0, panel.Renderer);
        var world = picker.GetPickPosition();

        double cx = world[0], cy = world[1], cz = world[2];
        double nx = 0, ny = 0, nz = 0;
        switch (plane)
        {
            case MprPlane.Axial:
                cz = origin[2] + (extent[4] + panel.Slice) * spacing[2] + 0.5 * spacing[2];
                nz = 1;
                break;
            case MprPlane.Sagittal:
                cx = origin[0] + (extent[0] + panel.Slice) * spacing[0] + 0.5 * spacing[0];
                nx = 1;
                break;
            case MprPlane.Coronal:
                cy = origin[1] + (extent[2] + panel.Slice) * spacing[1] - 0.5 * spacing[1];
                ny = 1;
                break;
        }

        if (panel.BrushSource is null)
        {
            var source = vtkRegularPolygonSource.New();
            source.SetNumberOfSides(48);
            source.GeneratePolygonOff();
            source.GeneratePolylineOn();

            var mapper = vtkPolyDataMapper.New();
            mapper.SetInputConnection(source.GetOutputPort());
            mapper.ScalarVisibilityOff();

            var actor = vtkActor.New();
            actor.SetMapper(mapper);
            actor.GetProperty().SetColor(1.0, 1.0, 1.0);
            actor.GetProperty().SetLineWidth(2);
            actor.PickableOff();
            panel.Renderer.AddActor(actor);

            panel.BrushSource = source;
            panel.BrushActor = actor;
        }

        panel.BrushSource.SetCenter(cx, cy, cz);
        panel.BrushSource.SetNormal(nx, ny, nz);
        panel.BrushSource.SetRadius(Math.Max(radiusMm, 0.1));
        panel.BrushActor?.SetVisibility(1);
        panel.Control?.RenderWindow.Render();
    }

    public void SetBrushVisible(bool visible)
    {
        foreach (var panel in panels)
        {
            panel.BrushActor?.SetVisibility(visible ? 1 : 0);
        }

        RenderAll();
    }

    public void Clear()
    {
        foreach (var panel in panels)
        {
            if (panel.Renderer is not null && panel.ImageSlice is not null)
            {
                panel.Renderer.RemoveViewProp(panel.ImageSlice);
            }

            if (panel.Renderer is not null && panel.BrushActor is not null)
            {
                panel.Renderer.RemoveActor(panel.BrushActor);
            }

            panel.BrushActor = null;
            panel.BrushSource = null;
            panel.ImageSlice = null;
            panel.SliceMapper = null;
            panel.MapToColors = null;
            panel.Slice = 0;
            if (panel.Annotation is not null)
            {
                panel.Annotation.SetText(2, panel.Title);
            }
        }

        labelMap = null;
        RenderAll();
    }

    private void BuildLookupTable(IReadOnlyList<RoiDescriptor> rois)
    {
        var maxId = 1;
        foreach (var roi in rois)
        {
            if (roi.Id > maxId)
            {
                maxId = roi.Id;
            }
        }

        var lut = vtkLookupTable.New();
        lut.SetNumberOfTableValues(maxId + 1);
        lut.SetTableRange(0, maxId);

        for (var value = 0; value <= maxId; value++)
        {
            lut.SetTableValue(value, 0, 0, 0, 0); // default: transparent
        }

        foreach (var roi in rois)
        {
            if (roi.Id < 0 || roi.Id > maxId)
            {
                continue;
            }

            var alpha = roi.Visibility ? FillAlpha : 0.0;
            var color = roi.Color;
            lut.SetTableValue(roi.Id, color.R / 255.0, color.G / 255.0, color.B / 255.0, alpha);
        }

        lut.Build();
        lookupTable = lut;
    }

    private void RenderAll()
    {
        foreach (var panel in panels)
        {
            panel.Control?.RenderWindow.Render();
        }
    }
}
