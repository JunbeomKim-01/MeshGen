using System.Windows.Media.Media3D;
using Kitware.VTK;

namespace MeshGenLite.Services;

public sealed class ElectrodeTransformBuilder
{
    public vtkTransform BuildTransform(Point3D surfacePoint, Vector3D surfaceNormal, double clearance)
    {
        var normal = surfaceNormal;
        if (normal.LengthSquared < 1e-8)
        {
            normal = new Vector3D(0, 0, 1);
        }

        normal.Normalize();

        var localAxis = new Vector3D(0, 0, 1);
        var rotationAxis = Vector3D.CrossProduct(localAxis, normal);
        var dot = Clamp(Vector3D.DotProduct(localAxis, normal), -1.0, 1.0);
        var angle = Math.Acos(dot) * 180.0 / Math.PI;

        var transform = vtkTransform.New();
        transform.PostMultiply();

        if (rotationAxis.LengthSquared > 1e-8)
        {
            rotationAxis.Normalize();
            transform.RotateWXYZ(angle, rotationAxis.X, rotationAxis.Y, rotationAxis.Z);
        }
        else if (dot < 0.0)
        {
            transform.RotateWXYZ(180.0, 1.0, 0.0, 0.0);
        }

        var worldPosition = surfacePoint + (normal * clearance);
        transform.Translate(worldPosition.X, worldPosition.Y, worldPosition.Z);
        return transform;
    }

    private static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;
}
