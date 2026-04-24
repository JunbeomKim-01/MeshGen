using Kitware.VTK;

namespace MeshGenLite.Services;

public sealed class LabeledSurface
{
    public vtkPolyData PolyData { get; init; } = vtkPolyData.New();

    public int RegionId { get; init; }

    public string Name { get; init; } = string.Empty;

    public double[]? SeedPointHint { get; init; }
}

public static class FemRegionIds
{
    public const int Air = 0;

    public const int FirstElectrode = 1000;

    public static int ElectrodeRegion(int placementId) => FirstElectrode + placementId;

    public static int CantorPairing(int a, int b)
    {
        var low = Math.Min(a, b);
        var high = Math.Max(a, b);
        return ((low + high) * (low + high + 1) / 2) + high;
    }
}
