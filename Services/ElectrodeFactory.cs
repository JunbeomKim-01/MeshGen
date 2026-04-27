using System.Windows.Media;
using Kitware.VTK;
using MeshGenLite.Models;
using WpfColor = System.Windows.Media.Color;

namespace MeshGenLite.Services;

public sealed class ElectrodeLayerMesh
{
    public ElectrodeLayer Layer { get; init; } = new();

    public vtkPolyData PolyData { get; init; } = vtkPolyData.New();

    public double ZStart { get; init; }

    public double ZEnd { get; init; }
}

public sealed class ElectrodeFactory
{
    public const string LayerIndexArrayName = "LayerIndex";

    public vtkPolyData CreateNeedle(ElectrodeSpec spec) => Build(spec);

    public vtkPolyData Build(ElectrodeSpec spec)
    {
        var layered = BuildLayered(spec);
        if (layered.Count == 0)
        {
            return vtkPolyData.New();
        }

        var append = vtkAppendPolyData.New();
        for (var index = 0; index < layered.Count; index++)
        {
            var poly = layered[index].PolyData;
            TagCellsWithLayerIndex(poly, index);
            append.AddInputData(poly);
        }

        append.Update();

        var combined = vtkPolyData.New();
        combined.DeepCopy(append.GetOutput());
        return combined;
    }

    public WpfColor[] GetLayerColors(ElectrodeSpec spec)
        => spec.Layers.Where(layer => layer.Thickness > 0).Select(layer => layer.Color).ToArray();

    private static void TagCellsWithLayerIndex(vtkPolyData polyData, int layerIndex)
    {
        var nCells = (int)polyData.GetNumberOfCells();
        var scalars = vtkIntArray.New();
        scalars.SetName(LayerIndexArrayName);
        scalars.SetNumberOfComponents(1);
        for (var cell = 0; cell < nCells; cell++)
        {
            scalars.InsertNextTuple1(layerIndex);
        }
        polyData.GetCellData().SetScalars(scalars);
    }

    public IReadOnlyList<ElectrodeLayerMesh> BuildLayered(ElectrodeSpec spec)
    {
        var result = new List<ElectrodeLayerMesh>();
        var z = 0.0;
        foreach (var layer in spec.Layers)
        {
            if (layer.Thickness <= 0)
            {
                continue;
            }

            var mesh = BuildLayer(spec.Shape, layer, z);
            result.Add(new ElectrodeLayerMesh
            {
                Layer = layer,
                PolyData = mesh,
                ZStart = z,
                ZEnd = z + layer.Thickness
            });

            z += layer.Thickness;
        }

        return result;
    }

    private static vtkPolyData BuildLayer(ElectrodeShape shape, ElectrodeLayer layer, double zStart) => shape switch
    {
        ElectrodeShape.Circle => BuildCircleLayer(layer, zStart),
        ElectrodeShape.Rectangle => BuildRectangleLayer(layer, zStart),
        ElectrodeShape.Triangle => BuildTriangleLayer(layer, zStart),
        _ => throw new NotSupportedException($"Electrode shape {shape} is not supported.")
    };

    private static vtkPolyData BuildCircleLayer(ElectrodeLayer layer, double zStart)
    {
        var source = vtkCylinderSource.New();
        source.SetRadius(Math.Max(layer.Radius, 1e-6));
        source.SetHeight(layer.Thickness);
        source.SetResolution(48);
        source.CappingOn();
        source.Update();

        var transform = vtkTransform.New();
        transform.PostMultiply();
        transform.RotateX(90.0);
        transform.Translate(0.0, 0.0, zStart + (layer.Thickness / 2.0));

        var filter = vtkTransformPolyDataFilter.New();
        filter.SetInputConnection(source.GetOutputPort());
        filter.SetTransform(transform);
        filter.Update();

        var result = vtkPolyData.New();
        result.DeepCopy(filter.GetOutput());
        return result;
    }

    private static vtkPolyData BuildRectangleLayer(ElectrodeLayer layer, double zStart)
    {
        var source = vtkCubeSource.New();
        source.SetXLength(Math.Max(layer.Width, 1e-6));
        source.SetYLength(Math.Max(layer.Depth, 1e-6));
        source.SetZLength(layer.Thickness);
        source.SetCenter(0.0, 0.0, zStart + (layer.Thickness / 2.0));
        source.Update();

        var triangleFilter = vtkTriangleFilter.New();
        triangleFilter.SetInputConnection(source.GetOutputPort());
        triangleFilter.Update();

        var result = vtkPolyData.New();
        result.DeepCopy(triangleFilter.GetOutput());
        return result;
    }

    private static vtkPolyData BuildTriangleLayer(ElectrodeLayer layer, double zStart)
    {
        var radius = Math.Max(layer.Radius, 1e-6);
        var basePoints = vtkPoints.New();
        basePoints.InsertNextPoint(radius, 0.0, 0.0);
        basePoints.InsertNextPoint(-radius / 2.0, radius * Math.Sqrt(3) / 2.0, 0.0);
        basePoints.InsertNextPoint(-radius / 2.0, -radius * Math.Sqrt(3) / 2.0, 0.0);

        var polygon = vtkPolygon.New();
        polygon.GetPointIds().SetNumberOfIds(3);
        polygon.GetPointIds().SetId(0, 0);
        polygon.GetPointIds().SetId(1, 1);
        polygon.GetPointIds().SetId(2, 2);

        var cells = vtkCellArray.New();
        cells.InsertNextCell(polygon);

        var basePoly = vtkPolyData.New();
        basePoly.SetPoints(basePoints);
        basePoly.SetPolys(cells);

        var extruder = vtkLinearExtrusionFilter.New();
        extruder.SetInputData(basePoly);
        extruder.SetExtrusionTypeToVectorExtrusion();
        extruder.SetVector(0.0, 0.0, layer.Thickness);
        extruder.SetScaleFactor(1.0);
        extruder.CappingOn();
        extruder.Update();

        var triangleFilter = vtkTriangleFilter.New();
        triangleFilter.SetInputConnection(extruder.GetOutputPort());
        triangleFilter.Update();

        var transform = vtkTransform.New();
        transform.Translate(0.0, 0.0, zStart);

        var transformFilter = vtkTransformPolyDataFilter.New();
        transformFilter.SetInputConnection(triangleFilter.GetOutputPort());
        transformFilter.SetTransform(transform);
        transformFilter.Update();

        var result = vtkPolyData.New();
        result.DeepCopy(transformFilter.GetOutput());
        return result;
    }
}
