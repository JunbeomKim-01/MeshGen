using System.Numerics;
using MathNet.Numerics.LinearAlgebra.Double;
using MeshGenLite.Models;

namespace MeshGenLite.Engine.Heuristics;

public sealed class ElectrodePoseOptimizer
{
    public Matrix4x4 OptimizePose(IReadOnlyList<ElectrodeSamplePoint> samples, IReadOnlyList<SurfaceHitResult> hits)
    {
        var validPairs = new List<(Vector3 Source, Vector3 Target)>();
        foreach (var sample in samples)
        {
            var hit = hits.FirstOrDefault(h => h.SampleId == sample.SampleId);
            if (hit != null && hit.Success)
            {
                validPairs.Add((sample.WorldPosition, hit.HitPoint));
            }
        }

        if (validPairs.Count < 3)
        {
            return Matrix4x4.Identity;
        }

        return CalculateKabschTransform(validPairs);
    }

    private Matrix4x4 CalculateKabschTransform(List<(Vector3 Source, Vector3 Target)> pairs)
    {
        var sourceCentroid = ComputeCentroid(pairs.Select(p => p.Source));
        var targetCentroid = ComputeCentroid(pairs.Select(p => p.Target));

        var hMatrix = DenseMatrix.Create(3, 3, 0.0);
        foreach (var (source, target) in pairs)
        {
            var p = source - sourceCentroid;
            var q = target - targetCentroid;

            hMatrix[0, 0] += p.X * q.X;
            hMatrix[0, 1] += p.X * q.Y;
            hMatrix[0, 2] += p.X * q.Z;
            hMatrix[1, 0] += p.Y * q.X;
            hMatrix[1, 1] += p.Y * q.Y;
            hMatrix[1, 2] += p.Y * q.Z;
            hMatrix[2, 0] += p.Z * q.X;
            hMatrix[2, 1] += p.Z * q.Y;
            hMatrix[2, 2] += p.Z * q.Z;
        }

        var svd = hMatrix.Svd(true);
        var u = svd.U;
        var vt = svd.VT;

        var rMatrix = vt.TransposeThisAndMultiply(u.Transpose());
        
        if (rMatrix.Determinant() < 0)
        {
            vt[2, 0] *= -1;
            vt[2, 1] *= -1;
            vt[2, 2] *= -1;
            rMatrix = vt.TransposeThisAndMultiply(u.Transpose());
        }

        var rotation = new Matrix4x4(
            (float)rMatrix[0, 0], (float)rMatrix[1, 0], (float)rMatrix[2, 0], 0,
            (float)rMatrix[0, 1], (float)rMatrix[1, 1], (float)rMatrix[2, 1], 0,
            (float)rMatrix[0, 2], (float)rMatrix[1, 2], (float)rMatrix[2, 2], 0,
            0, 0, 0, 1);

        var sourceCentroidRotated = Vector3.Transform(sourceCentroid, rotation);
        var translation = targetCentroid - sourceCentroidRotated;

        return rotation * Matrix4x4.CreateTranslation(translation);
    }

    private Vector3 ComputeCentroid(IEnumerable<Vector3> points)
    {
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var p in points)
        {
            sum += p;
            count++;
        }
        return count == 0 ? Vector3.Zero : sum / count;
    }
}
