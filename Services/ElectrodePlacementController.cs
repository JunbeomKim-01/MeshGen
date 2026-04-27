using System.Windows.Media;
using System.Windows.Media.Media3D;
using Kitware.VTK;
using MeshGenLite.Models;
using WpfColor = System.Windows.Media.Color;

namespace MeshGenLite.Services;

public sealed class ElectrodePlacementController
{
    public const double DefaultClearance = 0.5;

    private readonly MeshHitTestService hitTestService;
    private readonly ElectrodeFactory electrodeFactory;
    private readonly ElectrodeTransformBuilder transformBuilder;
    private readonly VtkViewportService viewportService;

    private PreviewState? currentPreview;
    private vtkPolyData? cachedLocalMesh;
    private long cachedSpecFingerprint;
    private WpfColor[]? cachedLayerColors;

    public ElectrodePlacementController(
        MeshHitTestService hitTestService,
        ElectrodeFactory electrodeFactory,
        ElectrodeTransformBuilder transformBuilder,
        VtkViewportService viewportService)
    {
        this.hitTestService = hitTestService;
        this.electrodeFactory = electrodeFactory;
        this.transformBuilder = transformBuilder;
        this.viewportService = viewportService;
    }

    public PlacementMode Mode { get; private set; }

    public bool IsActive => Mode == PlacementMode.ElectrodePlacement;

    public bool HasPreview => currentPreview is not null;

    public MeshHitResult? CurrentHit => currentPreview?.Hit;

    public void BeginPlacement()
    {
        Mode = PlacementMode.ElectrodePlacement;
        currentPreview = null;
        viewportService.HidePlacementPreview();
    }

    public void CancelPlacement()
    {
        Mode = PlacementMode.None;
        currentPreview = null;
        viewportService.HidePlacementPreview();
    }

    public bool UpdatePreview(int screenX, int screenY, ElectrodeSpec spec, WpfColor color)
    {
        if (!IsActive)
        {
            return false;
        }

        if (!hitTestService.TryHitExternalSurface(screenX, screenY, out var hit))
        {
            currentPreview = null;
            viewportService.HidePlacementPreview();
            return false;
        }

        var localMesh = GetCachedLocalMesh(spec);
        var layerColors = cachedLayerColors!;

        var transform = transformBuilder.BuildTransform(hit.Position, hit.Normal, DefaultClearance);
        var worldMesh = ApplyTransform(localMesh, transform);
        var worldPosition = hit.Position + (hit.Normal * DefaultClearance);

        currentPreview = new PreviewState(hit, worldMesh, worldPosition);
        viewportService.ShowPlacementPreview(worldMesh, color, 0.65, layerColors);
        return true;
    }

    public ElectrodePlacement? CommitPreview(ElectrodeRepository repository, ElectrodeSpec spec, WpfColor color, int? bodyMeshId)
    {
        if (currentPreview is null)
        {
            return null;
        }

        var placement = repository.CreateAndAdd(
            currentPreview.Hit,
            bodyMeshId,
            spec,
            DefaultClearance,
            currentPreview.WorldPosition);

        var layerColors = cachedLayerColors ?? electrodeFactory.GetLayerColors(spec);
        viewportService.AddCommittedElectrode(placement.Id, currentPreview.WorldMesh, color, 1.0, layerColors);
        viewportService.HidePlacementPreview();
        currentPreview = null;
        return placement;
    }

    private vtkPolyData GetCachedLocalMesh(ElectrodeSpec spec)
    {
        var fingerprint = ComputeSpecFingerprint(spec);
        if (cachedLocalMesh is not null && fingerprint == cachedSpecFingerprint)
        {
            return cachedLocalMesh;
        }

        // Visualization-only mesh (no FEM-grade subdivision) keeps preview rebuilds cheap.
        cachedLocalMesh = electrodeFactory.Build(spec, dense: false);
        cachedLayerColors = electrodeFactory.GetLayerColors(spec);
        cachedSpecFingerprint = fingerprint;
        return cachedLocalMesh;
    }

    private static long ComputeSpecFingerprint(ElectrodeSpec spec)
    {
        unchecked
        {
            long hash = 17;
            hash = (hash * 31) + (int)spec.Shape;
            hash = (hash * 31) + spec.Layers.Count;
            foreach (var layer in spec.Layers)
            {
                hash = (hash * 31) + BitConverter.DoubleToInt64Bits(layer.Radius);
                hash = (hash * 31) + BitConverter.DoubleToInt64Bits(layer.Width);
                hash = (hash * 31) + BitConverter.DoubleToInt64Bits(layer.Depth);
                hash = (hash * 31) + BitConverter.DoubleToInt64Bits(layer.Thickness);
                // Color is part of fingerprint so layer color edits invalidate the cached LUT.
                hash = (hash * 31) + (layer.Color.R << 16 | layer.Color.G << 8 | layer.Color.B);
            }
            return hash;
        }
    }

    private static vtkPolyData ApplyTransform(vtkPolyData localMesh, vtkTransform transform)
    {
        var filter = vtkTransformPolyDataFilter.New();
        filter.SetInputData(localMesh);
        filter.SetTransform(transform);
        filter.Update();

        var worldMesh = vtkPolyData.New();
        worldMesh.DeepCopy(filter.GetOutput());
        return worldMesh;
    }

    private sealed class PreviewState
    {
        public PreviewState(MeshHitResult hit, vtkPolyData worldMesh, Point3D worldPosition)
        {
            Hit = hit;
            WorldMesh = worldMesh;
            WorldPosition = worldPosition;
        }

        public MeshHitResult Hit { get; }

        public vtkPolyData WorldMesh { get; }

        public Point3D WorldPosition { get; }
    }
}
