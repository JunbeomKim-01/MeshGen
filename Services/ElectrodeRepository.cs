using System.Collections.ObjectModel;
using MeshGenLite.Models;

namespace MeshGenLite.Services;

public sealed class ElectrodeRepository
{
    private int nextId = 1;

    public ObservableCollection<ElectrodePlacement> Placements { get; } = new();

    public ElectrodePlacement Add(ElectrodePlacement placement)
    {
        Placements.Add(placement);
        nextId = Math.Max(nextId, placement.Id + 1);
        return placement;
    }

    public ElectrodePlacement CreateAndAdd(
        MeshHitResult hit,
        int? bodyMeshId,
        ElectrodeSpec spec,
        double clearance,
        System.Windows.Media.Media3D.Point3D worldPosition)
    {
        var placement = new ElectrodePlacement
        {
            Id = nextId++,
            BodyMeshId = bodyMeshId,
            PickedCellId = hit.CellId,
            Length = spec.Height,
            Radius = spec.Radius,
            Clearance = clearance,
            SurfacePoint = hit.Position,
            WorldPosition = worldPosition,
            WorldNormal = hit.Normal
        };

        Placements.Add(placement);
        return placement;
    }

    public bool Remove(ElectrodePlacement placement) => Placements.Remove(placement);

    public void Clear()
    {
        Placements.Clear();
        nextId = 1;
    }
}
