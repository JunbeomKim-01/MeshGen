using Kitware.VTK;
using MeshGenLite.Models;

namespace MeshGenLite.Services;

public sealed class ElectrodeFactory
{
    public vtkPolyData CreateNeedle(ElectrodeSpec spec)
    {
        var source = vtkCylinderSource.New();
        source.SetRadius(spec.Radius);
        source.SetHeight(spec.Height);
        source.SetResolution(48);
        source.CappingOn();
        source.Update();

        var transform = vtkTransform.New();
        transform.PostMultiply();
        transform.RotateX(90.0);
        transform.Translate(0.0, 0.0, spec.Height / 2.0);

        var filter = vtkTransformPolyDataFilter.New();
        filter.SetInputConnection(source.GetOutputPort());
        filter.SetTransform(transform);
        filter.Update();

        var polyData = vtkPolyData.New();
        polyData.DeepCopy(filter.GetOutput());
        return polyData;
    }
}
