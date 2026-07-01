using Kitware.VTK;

namespace MeshGenLite.Services;

/// <summary>
/// Voxel brush editor operating directly on the shared label map (any scalar type,
/// accessed through the type-agnostic scalar-component API). Paint stamps a disk of the
/// active label into the current slice; erase stamps 0. Each stroke is undoable.
/// </summary>
public sealed class LabelMapEditor
{
    private sealed record Voxel(int I, int J, int K);

    private vtkImageData? labelMap;
    private int[] extent = { 0, 0, 0, 0, 0, 0 };
    private double[] spacing = { 1, 1, 1 };

    private readonly Stack<Dictionary<Voxel, double>> undoStack = new();
    private Dictionary<Voxel, double>? currentStroke;
    private readonly HashSet<int> affectedLabels = new();

    public bool HasVolume => labelMap is not null;

    public bool CanUndo => undoStack.Count > 0;

    public void SetLabelMap(vtkImageData map)
    {
        labelMap = map;
        extent = map.GetExtent();
        spacing = map.GetSpacing();
        undoStack.Clear();
        currentStroke = null;
    }

    public void BeginStroke()
    {
        currentStroke = new Dictionary<Voxel, double>();
        affectedLabels.Clear();
    }

    /// <summary>Paint (or erase) a disk on the given plane, centred at voxel (i,j,k).</summary>
    public void Stamp(MprPlane plane, int i, int j, int k, double radiusMm, int label, bool erase)
    {
        if (labelMap is null || currentStroke is null)
        {
            return;
        }

        var newValue = erase ? 0.0 : label;

        // In-plane radii in voxels for the two axes that vary on this plane.
        int radiusA, radiusB;
        switch (plane)
        {
            case MprPlane.Axial: // vary i (x), j (y)
                radiusA = RadiusVox(radiusMm, spacing[0]);
                radiusB = RadiusVox(radiusMm, spacing[1]);
                StampDisk(i, j, k, radiusA, radiusB, 0, 1, newValue);
                break;
            case MprPlane.Sagittal: // vary j (y), k (z)
                radiusA = RadiusVox(radiusMm, spacing[1]);
                radiusB = RadiusVox(radiusMm, spacing[2]);
                StampDisk(i, j, k, radiusA, radiusB, 1, 2, newValue);
                break;
            case MprPlane.Coronal: // vary i (x), k (z)
                radiusA = RadiusVox(radiusMm, spacing[0]);
                radiusB = RadiusVox(radiusMm, spacing[2]);
                StampDisk(i, j, k, radiusA, radiusB, 0, 2, newValue);
                break;
        }
    }

    private void StampDisk(int i, int j, int k, int radiusA, int radiusB, int axisA, int axisB, double newValue)
    {
        var center = new[] { i, j, k };
        for (var da = -radiusA; da <= radiusA; da++)
        {
            for (var db = -radiusB; db <= radiusB; db++)
            {
                // Elliptical footprint so brush is round in world space regardless of spacing.
                var na = radiusA == 0 ? 0.0 : (double)da / radiusA;
                var nb = radiusB == 0 ? 0.0 : (double)db / radiusB;
                if (na * na + nb * nb > 1.0)
                {
                    continue;
                }

                var voxel = new[] { center[0], center[1], center[2] };
                voxel[axisA] += da;
                voxel[axisB] += db;
                WriteVoxel(voxel[0], voxel[1], voxel[2], newValue);
            }
        }
    }

    private void WriteVoxel(int i, int j, int k, double newValue)
    {
        if (labelMap is null || currentStroke is null)
        {
            return;
        }

        if (i < extent[0] || i > extent[1] ||
            j < extent[2] || j > extent[3] ||
            k < extent[4] || k > extent[5])
        {
            return;
        }

        var key = new Voxel(i, j, k);
        var oldValue = labelMap.GetScalarComponentAsDouble(i, j, k, 0);
        if (Math.Abs(oldValue - newValue) < 0.5)
        {
            return;
        }

        if (!currentStroke.ContainsKey(key))
        {
            currentStroke[key] = oldValue;
        }

        affectedLabels.Add((int)Math.Round(oldValue));
        affectedLabels.Add((int)Math.Round(newValue));
        labelMap.SetScalarComponentFromDouble(i, j, k, 0, newValue);
    }

    /// <summary>Finalize the stroke; returns the set of label ids whose geometry changed.</summary>
    public IReadOnlyCollection<int> EndStroke()
    {
        var affected = affectedLabels.Where(id => id > 0).ToArray();
        if (currentStroke is { Count: > 0 })
        {
            undoStack.Push(currentStroke);
            labelMap?.Modified();
        }

        currentStroke = null;
        affectedLabels.Clear();
        return affected;
    }

    /// <summary>Undo the last stroke; returns the affected label ids for surface regeneration.</summary>
    public IReadOnlyCollection<int> Undo()
    {
        if (labelMap is null || undoStack.Count == 0)
        {
            return Array.Empty<int>();
        }

        var stroke = undoStack.Pop();
        var affected = new HashSet<int>();
        foreach (var pair in stroke)
        {
            var current = labelMap.GetScalarComponentAsDouble(pair.Key.I, pair.Key.J, pair.Key.K, 0);
            affected.Add((int)Math.Round(current));
            labelMap.SetScalarComponentFromDouble(pair.Key.I, pair.Key.J, pair.Key.K, 0, pair.Value);
            affected.Add((int)Math.Round(pair.Value));
        }

        labelMap.Modified();
        return affected.Where(id => id > 0).ToArray();
    }

    private static int RadiusVox(double radiusMm, double spacing)
    {
        if (spacing <= 0)
        {
            return 0;
        }

        return Math.Max(0, (int)Math.Round(radiusMm / spacing));
    }
}
