using Kitware.VTK;
using MeshGenLite.Models;

namespace MeshGenLite.Services;

public sealed class VolumeMeshResult
{
    public vtkUnstructuredGrid Grid { get; init; } = vtkUnstructuredGrid.New();

    public int PointCount { get; init; }

    public int TetrahedronCount { get; init; }

    public int InputPointCount { get; init; }

    public int DecimatedPointCount { get; init; }

    public string Summary =>
        InputPointCount == DecimatedPointCount
            ? $"Points {PointCount:N0} · Tetrahedra {TetrahedronCount:N0}"
            : $"Points {PointCount:N0} · Tetrahedra {TetrahedronCount:N0} · Decimated {InputPointCount:N0}→{DecimatedPointCount:N0}";
}

public interface IVolumeMeshService
{
    Task<VolumeMeshResult> GenerateAsync(
        vtkPolyData surface,
        double alpha,
        int maxPoints,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class VolumeMeshService : IVolumeMeshService
{
    public Task<VolumeMeshResult> GenerateAsync(
        vtkPolyData surface,
        double alpha,
        int maxPoints,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputPointCount = (int)surface.GetNumberOfPoints();
            if (inputPointCount < 4)
            {
                throw new InvalidOperationException("Surface does not contain enough points for tetrahedralization.");
            }

            progress?.Report(new PipelineProgress
            {
                Message = "Cleaning surface points",
                Detail = $"{inputPointCount:N0} input points"
            });

            var cleaner = vtkCleanPolyData.New();
            cleaner.SetInputData(surface);
            cleaner.PointMergingOn();
            cleaner.ConvertLinesToPointsOff();
            cleaner.ConvertPolysToLinesOff();
            cleaner.ConvertStripsToPolysOff();
            cleaner.Update();

            var cleaned = cleaner.GetOutput();
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = cleaned;
            var cleanedPointCount = (int)cleaned.GetNumberOfPoints();
            var decimatedPointCount = cleanedPointCount;

            if (maxPoints > 0 && cleanedPointCount > maxPoints)
            {
                var reduction = 1.0 - ((double)maxPoints / cleanedPointCount);
                progress?.Report(new PipelineProgress
                {
                    Message = "Decimating surface",
                    Detail = $"{cleanedPointCount:N0}→~{maxPoints:N0} points (reduction {reduction:P0})"
                });

                var decimator = vtkQuadricDecimation.New();
                decimator.SetInputData(cleaned);
                decimator.SetTargetReduction(reduction);
                decimator.Update();

                candidate = decimator.GetOutput();
                decimatedPointCount = (int)candidate.GetNumberOfPoints();
                cancellationToken.ThrowIfCancellationRequested();
            }

            progress?.Report(new PipelineProgress
            {
                Message = "Running Delaunay3D",
                Detail = $"alpha {alpha:F2} mm · {decimatedPointCount:N0} points"
            });

            var delaunay = vtkDelaunay3D.New();
            delaunay.SetInputData(candidate);
            if (alpha > 0)
            {
                delaunay.SetAlpha(alpha);
                delaunay.AlphaTetsOn();
                delaunay.AlphaTrisOff();
                delaunay.AlphaLinesOff();
                delaunay.AlphaVertsOff();
            }
            delaunay.SetTolerance(1e-3);
            delaunay.SetOffset(2.5);
            delaunay.BoundingTriangulationOff();
            delaunay.Update();

            cancellationToken.ThrowIfCancellationRequested();

            var grid = vtkUnstructuredGrid.New();
            grid.DeepCopy(delaunay.GetOutput());

            var pointCount = (int)grid.GetNumberOfPoints();
            var cellCount = (int)grid.GetNumberOfCells();

            progress?.Report(new PipelineProgress
            {
                Message = "Volume mesh ready",
                Detail = $"{cellCount:N0} tetrahedra"
            });

            return new VolumeMeshResult
            {
                Grid = grid,
                PointCount = pointCount,
                TetrahedronCount = cellCount,
                InputPointCount = inputPointCount,
                DecimatedPointCount = decimatedPointCount
            };
        }, cancellationToken);
    }
}
