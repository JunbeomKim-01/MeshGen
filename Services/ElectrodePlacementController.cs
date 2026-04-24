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

        var localMesh = electrodeFactory.CreateNeedle(spec);
        var transform = transformBuilder.BuildTransform(hit.Position, hit.Normal, DefaultClearance);
        var worldMesh = ApplyTransform(localMesh, transform);
        var worldPosition = hit.Position + (hit.Normal * DefaultClearance);

        currentPreview = new PreviewState(hit, worldMesh, worldPosition);
        viewportService.ShowPlacementPreview(worldMesh, color, 0.35);
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

        viewportService.AddCommittedElectrode(placement.Id, currentPreview.WorldMesh, color, 0.85);
        viewportService.HidePlacementPreview();
        currentPreview = null;
        return placement;
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
