using System.Globalization;
using System.IO;
using System.Text;
using Kitware.VTK;

namespace MeshGenLite.Services;

public interface IMeshExportService
{
    void WriteStl(vtkPolyData surface, string path);

    void WritePoly(vtkPolyData surface, string path);

    byte[] ExportStlToBytes(vtkPolyData surface);
}

public sealed class MeshExportService : IMeshExportService
{
    public void WriteStl(vtkPolyData surface, string path)
    {
        if (surface.GetNumberOfPoints() == 0)
        {
            throw new InvalidOperationException("Cannot export STL: surface has no points.");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var triangulated = EnsureTriangulated(surface);
        var writer = vtkSTLWriter.New();
        writer.SetFileName(path);
        writer.SetInputData(triangulated);
        writer.SetFileTypeToBinary();
        writer.Write();
    }

    public byte[] ExportStlToBytes(vtkPolyData surface)
    {
        if (surface.GetNumberOfPoints() == 0)
        {
            return Array.Empty<byte>();
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"meshgen_{Guid.NewGuid():N}.stl");
        try
        {
            WriteStl(surface, tempPath);
            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public void WritePoly(vtkPolyData surface, string path)
    {
        if (surface.GetNumberOfPoints() == 0)
        {
            throw new InvalidOperationException("Cannot export POLY: surface has no points.");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var triangulated = EnsureTriangulated(surface);
        var pointCount = (int)triangulated.GetNumberOfPoints();
        var polys = triangulated.GetPolys();

        var invariant = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();

        builder.AppendLine("# MeshGenLite PLC export");
        builder.AppendLine(string.Create(invariant, $"{pointCount} 3 0 0"));
        for (var index = 0; index < pointCount; index++)
        {
            var point = triangulated.GetPoint(index);
            builder.AppendLine(string.Create(invariant, $"{index + 1} {point[0]:R} {point[1]:R} {point[2]:R}"));
        }

        var facetBuilder = new StringBuilder();
        var facetCount = 0;
        if (polys is not null)
        {
            polys.InitTraversal();
            var idList = vtkIdList.New();
            while (polys.GetNextCell(idList) != 0)
            {
                var cornerCount = (int)idList.GetNumberOfIds();
                if (cornerCount < 3)
                {
                    continue;
                }

                facetBuilder.AppendLine("1 0 0");
                facetBuilder.Append(cornerCount.ToString(invariant));
                for (var corner = 0; corner < cornerCount; corner++)
                {
                    facetBuilder.Append(' ');
                    facetBuilder.Append(((int)idList.GetId(corner) + 1).ToString(invariant));
                }

                facetBuilder.AppendLine();
                facetCount++;
            }
        }

        builder.AppendLine(string.Create(invariant, $"{facetCount} 0"));
        builder.Append(facetBuilder);
        builder.AppendLine("0");
        builder.AppendLine("0");

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static vtkPolyData EnsureTriangulated(vtkPolyData surface)
    {
        var triangleFilter = vtkTriangleFilter.New();
        triangleFilter.SetInputData(surface);
        triangleFilter.PassVertsOff();
        triangleFilter.PassLinesOff();
        triangleFilter.Update();

        var result = vtkPolyData.New();
        result.DeepCopy(triangleFilter.GetOutput());
        return result;
    }
}
