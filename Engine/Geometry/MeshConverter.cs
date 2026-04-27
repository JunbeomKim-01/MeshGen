using System.Numerics;
using Kitware.VTK;
using MeshGenLite.Models;

namespace MeshGenLite.Engine.Geometry;

/// <summary>
/// Converts between VTK rendering objects (<see cref="vtkPolyData"/>)
/// and the engine's pure data model (<see cref="MeshData"/>).
/// This adapter lives at the boundary between Infrastructure and Core,
/// ensuring that the Core engine never depends on VTK types directly.
/// </summary>
public static class MeshConverter
{
    private const string LayerIndexArrayName = "LayerIndex";

    /// <summary>
    /// Converts a <see cref="vtkPolyData"/> surface mesh to a <see cref="MeshData"/>.
    /// </summary>
    public static MeshData FromVtkPolyData(vtkPolyData polyData)
    {
        var mesh = new MeshData();

        // Extract vertices
        var points = polyData.GetPoints();
        if (points is null)
        {
            return mesh;
        }

        var pointCount = (int)points.GetNumberOfPoints();
        for (var i = 0; i < pointCount; i++)
        {
            var p = points.GetPoint(i);
            mesh.Vertices.Add(new Vector3((float)p[0], (float)p[1], (float)p[2]));
        }

        // Extract triangle indices
        var polys = polyData.GetPolys();
        if (polys is not null)
        {
            polys.InitTraversal();
            var idList = vtkIdList.New();
            var cellScalars = polyData.GetCellData()?.GetScalars();
            if (cellScalars is not null && cellScalars.GetNumberOfTuples() < polyData.GetNumberOfCells())
            {
                cellScalars = null;
            }

            var cellId = 0;
            var layerIndices = cellScalars is null ? null : new List<int>();
            while (polys.GetNextCell(idList) != 0)
            {
                var layerIndex = cellScalars is null ? 0 : (int)Math.Round(cellScalars.GetTuple1(cellId));
                if (idList.GetNumberOfIds() == 3)
                {
                    mesh.Indices.Add((int)idList.GetId(0));
                    mesh.Indices.Add((int)idList.GetId(1));
                    mesh.Indices.Add((int)idList.GetId(2));
                    layerIndices?.Add(layerIndex);
                }
                else if (idList.GetNumberOfIds() == 4)
                {
                    // Triangulate quads
                    mesh.Indices.Add((int)idList.GetId(0));
                    mesh.Indices.Add((int)idList.GetId(1));
                    mesh.Indices.Add((int)idList.GetId(2));
                    layerIndices?.Add(layerIndex);
                    mesh.Indices.Add((int)idList.GetId(0));
                    mesh.Indices.Add((int)idList.GetId(2));
                    mesh.Indices.Add((int)idList.GetId(3));
                    layerIndices?.Add(layerIndex);
                }

                cellId++;
            }

            if (layerIndices is not null && layerIndices.Count == mesh.TriangleCount)
            {
                mesh.CellLayerIndices = layerIndices;
            }
        }

        // Extract normals if available
        var normalData = polyData.GetPointData()?.GetNormals();
        if (normalData is not null && normalData.GetNumberOfTuples() == pointCount)
        {
            mesh.Normals = new List<Vector3>(pointCount);
            for (var i = 0; i < pointCount; i++)
            {
                var n = normalData.GetTuple3(i);
                mesh.Normals.Add(new Vector3((float)n[0], (float)n[1], (float)n[2]));
            }
        }

        return mesh;
    }

    /// <summary>
    /// Converts a <see cref="MeshData"/> back to a <see cref="vtkPolyData"/> for rendering.
    /// </summary>
    public static vtkPolyData ToVtkPolyData(MeshData mesh)
    {
        var points = vtkPoints.New();
        foreach (var vertex in mesh.Vertices)
        {
            points.InsertNextPoint(vertex.X, vertex.Y, vertex.Z);
        }

        var triangles = vtkCellArray.New();
        var layerScalars = mesh.CellLayerIndices is null ? null : vtkIntArray.New();
        if (layerScalars is not null)
        {
            layerScalars.SetName(LayerIndexArrayName);
            layerScalars.SetNumberOfComponents(1);
        }

        for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            if (mesh.Indices[i] < 0 ||
                mesh.Indices[i + 1] < 0 ||
                mesh.Indices[i + 2] < 0 ||
                mesh.Indices[i] >= mesh.Vertices.Count ||
                mesh.Indices[i + 1] >= mesh.Vertices.Count ||
                mesh.Indices[i + 2] >= mesh.Vertices.Count)
            {
                continue;
            }

            var triangle = vtkTriangle.New();
            triangle.GetPointIds().SetId(0, mesh.Indices[i]);
            triangle.GetPointIds().SetId(1, mesh.Indices[i + 1]);
            triangle.GetPointIds().SetId(2, mesh.Indices[i + 2]);
            triangles.InsertNextCell(triangle);
            if (layerScalars is not null)
            {
                var triangleId = i / 3;
                var layerIndex = triangleId < mesh.CellLayerIndices!.Count ? mesh.CellLayerIndices[triangleId] : 0;
                layerScalars.InsertNextTuple1(layerIndex);
            }
        }

        var polyData = vtkPolyData.New();
        polyData.SetPoints(points);
        polyData.SetPolys(triangles);
        if (layerScalars is not null)
        {
            polyData.GetCellData().SetScalars(layerScalars);
        }

        // Set normals if available
        if (mesh.Normals is not null && mesh.Normals.Count == mesh.Vertices.Count)
        {
            var normals = vtkFloatArray.New();
            normals.SetNumberOfComponents(3);
            normals.SetName("Normals");
            foreach (var n in mesh.Normals)
            {
                normals.InsertNextTuple3(n.X, n.Y, n.Z);
            }

            polyData.GetPointData().SetNormals(normals);
        }

        return polyData;
    }

    /// <summary>
    /// Creates a VTK polydata containing only the specified vertex subset,
    /// useful for highlighting contact patches.
    /// </summary>
    public static vtkPolyData CreateVertexSubset(vtkPolyData source, IReadOnlyCollection<int> vertexIds)
    {
        var vertexSet = new HashSet<int>(vertexIds);
        var points = vtkPoints.New();
        var cells = vtkCellArray.New();

        // Map old vertex IDs → new sequential IDs
        var indexMap = new Dictionary<int, long>();
        foreach (var id in vertexIds)
        {
            if (id >= 0 && id < source.GetNumberOfPoints())
            {
                var p = source.GetPoint(id);
                indexMap[id] = points.InsertNextPoint(p[0], p[1], p[2]);
            }
        }

        // Copy triangles where all three vertices are in the subset
        var polys = source.GetPolys();
        if (polys is not null)
        {
            polys.InitTraversal();
            var idList = vtkIdList.New();
            while (polys.GetNextCell(idList) != 0)
            {
                if (idList.GetNumberOfIds() != 3)
                {
                    continue;
                }

                var v0 = (int)idList.GetId(0);
                var v1 = (int)idList.GetId(1);
                var v2 = (int)idList.GetId(2);

                if (indexMap.ContainsKey(v0) && indexMap.ContainsKey(v1) && indexMap.ContainsKey(v2))
                {
                    var triangle = vtkTriangle.New();
                    triangle.GetPointIds().SetId(0, indexMap[v0]);
                    triangle.GetPointIds().SetId(1, indexMap[v1]);
                    triangle.GetPointIds().SetId(2, indexMap[v2]);
                    cells.InsertNextCell(triangle);
                }
            }
        }

        var result = vtkPolyData.New();
        result.SetPoints(points);
        result.SetPolys(cells);
        return result;
    }
}
