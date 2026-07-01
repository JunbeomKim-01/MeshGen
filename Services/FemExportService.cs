using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Kitware.VTK;
using MeshGenLite.Models;

namespace MeshGenLite.Services;

public interface IFemExportService
{
    FemExportResult Export(FemExportRequest request);
}

public sealed class FemExportService : IFemExportService
{
    public FemExportResult Export(FemExportRequest request)
    {
        if (request.VolumeGrid.GetNumberOfPoints() == 0 || request.VolumeGrid.GetNumberOfCells() == 0)
        {
            throw new InvalidOperationException("No volume mesh is available for FEM export.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new InvalidOperationException("FEM export output directory is required.");
        }

        Directory.CreateDirectory(request.OutputDirectory);
        var baseName = SanitizeBaseName(request.BaseName);
        var paths = BuildPaths(request.OutputDirectory, baseName);

        var context = BuildContext(request);
        var gridSnapshot = SnapshotGrid(request.VolumeGrid);
        var tetrahedra = ExtractTetrahedra(gridSnapshot, context);
        if (tetrahedra.Count == 0)
        {
            throw new InvalidOperationException("The volume mesh does not contain tetrahedral cells.");
        }

        var boundaryFaces = ExtractBoundaryFaces(tetrahedra, gridSnapshot, context);
        ValidateLabels(tetrahedra, boundaryFaces, context);
        AddCellArrays(request.VolumeGrid, tetrahedra);

        Parallel.Invoke(
            () => WriteNodes(gridSnapshot.Points, paths.NodesPath),
            () => WriteElements(tetrahedra, paths.ElementsPath),
            () => WriteBoundaryFaces(boundaryFaces, paths.BoundaryFacesPath),
            () => WriteMaterials(context.Materials, context.Boundaries, paths.MaterialsPath),
            () => WriteVtu(gridSnapshot.Points, tetrahedra, paths.VtuPath),
            () => WriteManifest(paths, context, gridSnapshot.Points.Length, tetrahedra.Count, boundaryFaces.Count));

        return new FemExportResult
        {
            OutputDirectory = request.OutputDirectory,
            BaseName = baseName,
            VtuPath = paths.VtuPath,
            NodesPath = paths.NodesPath,
            ElementsPath = paths.ElementsPath,
            BoundaryFacesPath = paths.BoundaryFacesPath,
            MaterialsPath = paths.MaterialsPath,
            ManifestPath = paths.ManifestPath,
            NodeCount = gridSnapshot.Points.Length,
            TetrahedronCount = tetrahedra.Count,
            BoundaryFaceCount = boundaryFaces.Count,
            RegionCount = context.Materials.Select(material => material.RegionId).Distinct().Count(),
            MaterialCount = context.Materials.Count
        };
    }

    private static FemExportContext BuildContext(FemExportRequest request)
    {
        var materials = new List<FemMaterialRecord>();
        var boundaries = new List<FemBoundaryRecord>();
        var roiClassifiers = new List<RoiSurfaceClassifier>();

        foreach (var surface in request.RoiSurfaces)
        {
            if (surface.PolyData.GetNumberOfPoints() == 0)
            {
                continue;
            }

            var isExternal = surface.Roi.Role == RoiRole.External ||
                             request.BodySurface is not null &&
                             ReferenceEquals(surface.PolyData, request.BodySurface);
            var regionId = isExternal ? FemIds.BodyRegion(surface.Roi.Id) : surface.Roi.Id;
            var materialId = isExternal ? FemIds.DefaultBodyMaterialId : FemIds.BodyMaterial(surface.Roi.Id);
            var boundaryMarker = FemIds.RoiBoundary(surface.Roi.Id, isExternal);

            materials.Add(new FemMaterialRecord
            {
                RegionId = regionId,
                MaterialId = materialId,
                Name = string.IsNullOrWhiteSpace(surface.Roi.Name) ? $"ROI {surface.Roi.Id}" : surface.Roi.Name,
                SourceType = isExternal ? "ExternalBody" : "TissueRoi",
                Conductivity = FemMaterialDefaults.BodyConductivity,
                Permittivity = FemMaterialDefaults.BodyPermittivity,
                ThermalConductivity = FemMaterialDefaults.BodyThermalConductivity,
                Density = FemMaterialDefaults.BodyDensity,
                SpecificHeat = FemMaterialDefaults.BodySpecificHeat
            });
            boundaries.Add(new FemBoundaryRecord
            {
                BoundaryMarker = boundaryMarker,
                Name = isExternal ? "External Outer Surface" : $"{surface.Roi.Name} Surface",
                SourceType = isExternal ? "ExternalBody" : "TissueRoi",
                OwnerRegionId = regionId
            });
            roiClassifiers.Add(new RoiSurfaceClassifier(surface.Roi, surface.PolyData, regionId, materialId, boundaryMarker));
        }

        if (materials.Count == 0)
        {
            materials.Add(new FemMaterialRecord
            {
                RegionId = FemIds.DefaultBodyRegionId,
                MaterialId = FemIds.DefaultBodyMaterialId,
                Name = "Body",
                SourceType = "ExternalBody",
                Conductivity = FemMaterialDefaults.BodyConductivity,
                Permittivity = FemMaterialDefaults.BodyPermittivity,
                ThermalConductivity = FemMaterialDefaults.BodyThermalConductivity,
                Density = FemMaterialDefaults.BodyDensity,
                SpecificHeat = FemMaterialDefaults.BodySpecificHeat
            });
            boundaries.Add(new FemBoundaryRecord
            {
                BoundaryMarker = FemIds.ExternalBoundaryMarker,
                Name = "External Outer Surface",
                SourceType = "ExternalBody",
                OwnerRegionId = FemIds.DefaultBodyRegionId
            });
        }

        var electrodeClassifiers = new List<ElectrodeSurfaceClassifier>();
        foreach (var electrode in request.Electrodes)
        {
            if (electrode.Surface.GetNumberOfPoints() == 0)
            {
                continue;
            }

            var classifier = new ElectrodeSurfaceClassifier(electrode, request.ElectrodeSpec);
            electrodeClassifiers.Add(classifier);

            for (var layerIndex = 0; layerIndex < request.ElectrodeSpec.Layers.Count; layerIndex++)
            {
                var layer = request.ElectrodeSpec.Layers[layerIndex];
                if (layer.Thickness <= 0)
                {
                    continue;
                }

                var regionId = FemIds.ElectrodeLayerRegion(electrode.PlacementId, layerIndex);
                var materialId = regionId;
                materials.Add(new FemMaterialRecord
                {
                    RegionId = regionId,
                    MaterialId = materialId,
                    Name = $"Electrode {electrode.PlacementId} {layer.Name}",
                    SourceType = "ElectrodeLayer",
                    Conductivity = layer.Conductivity,
                    Permittivity = layer.Permittivity,
                    ThermalConductivity = FemMaterialDefaults.ElectrodeThermalConductivity,
                    Density = FemMaterialDefaults.ElectrodeDensity,
                    SpecificHeat = FemMaterialDefaults.ElectrodeSpecificHeat
                });
            }

            boundaries.Add(new FemBoundaryRecord
            {
                BoundaryMarker = FemIds.ElectrodeOuterBoundary(electrode.PlacementId),
                Name = $"Electrode {electrode.PlacementId} Outer Surface",
                SourceType = "Electrode",
                OwnerRegionId = FemIds.ElectrodeLayerRegion(electrode.PlacementId, Math.Max(request.ElectrodeSpec.Layers.Count - 1, 0))
            });
            boundaries.Add(new FemBoundaryRecord
            {
                BoundaryMarker = FemIds.ElectrodeContactBoundary(electrode.PlacementId),
                Name = $"Electrode {electrode.PlacementId} Contact Surface",
                SourceType = "ElectrodeContact",
                OwnerRegionId = FemIds.ElectrodeLayerRegion(electrode.PlacementId, 0)
            });
        }

        return new FemExportContext(
            materials
                .GroupBy(material => material.RegionId)
                .Select(group => group.First())
                .OrderBy(material => material.RegionId)
                .ToList(),
            boundaries
                .GroupBy(boundary => boundary.BoundaryMarker)
                .Select(group => group.First())
                .OrderBy(boundary => boundary.BoundaryMarker)
                .ToList(),
            roiClassifiers,
            electrodeClassifiers,
            request.ElectrodeSpec);
    }

    private static GridSnapshot SnapshotGrid(vtkUnstructuredGrid grid)
    {
        var points = new Point3[(int)grid.GetNumberOfPoints()];
        for (var i = 0; i < points.Length; i++)
        {
            var point = grid.GetPoint(i);
            points[i] = new Point3(point[0], point[1], point[2]);
        }

        var tetrahedra = new List<RawTetrahedron>();
        var cellCount = (int)grid.GetNumberOfCells();
        for (var cellId = 0; cellId < cellCount; cellId++)
        {
            var cell = grid.GetCell(cellId);
            if (cell is null || cell.GetNumberOfPoints() != 4)
            {
                continue;
            }

            var ids = cell.GetPointIds();
            tetrahedra.Add(new RawTetrahedron(
                cellId,
                (int)ids.GetId(0),
                (int)ids.GetId(1),
                (int)ids.GetId(2),
                (int)ids.GetId(3)));
        }

        return new GridSnapshot(points, tetrahedra);
    }

    private static List<FemTetrahedron> ExtractTetrahedra(GridSnapshot grid, FemExportContext context)
    {
        var result = new FemTetrahedron[grid.Tetrahedra.Count];
        Parallel.For(0, grid.Tetrahedra.Count, index =>
        {
            var raw = grid.Tetrahedra[index];
            var centroid = ComputeCentroid(grid.Points, raw.A, raw.B, raw.C, raw.D);
            var label = ClassifyRegion(centroid, context);
            result[index] = new FemTetrahedron(index, raw.OriginalCellId, raw.A, raw.B, raw.C, raw.D, label.RegionId, label.MaterialId);
        });

        return result.ToList();
    }

    private static List<FemBoundaryFace> ExtractBoundaryFaces(
        IReadOnlyList<FemTetrahedron> tetrahedra,
        GridSnapshot grid,
        FemExportContext context)
    {
        var faces = new Dictionary<FaceKey, FaceAccumulator>();
        foreach (var tetra in tetrahedra)
        {
            AddFace(faces, tetra, tetra.A, tetra.B, tetra.C);
            AddFace(faces, tetra, tetra.A, tetra.B, tetra.D);
            AddFace(faces, tetra, tetra.A, tetra.C, tetra.D);
            AddFace(faces, tetra, tetra.B, tetra.C, tetra.D);
        }

        var candidates = faces.Values.Where(face => face.Count == 1).ToList();
        var result = new FemBoundaryFace[candidates.Count];
        Parallel.For(0, candidates.Count, index =>
        {
            var face = candidates[index];
            var centroid = ComputeCentroid(grid.Points, face.A, face.B, face.C);
            var marker = ClassifyBoundary(centroid, context);
            result[index] = new FemBoundaryFace(
                index,
                face.A,
                face.B,
                face.C,
                marker.BoundaryMarker,
                face.OwnerRegionId);
        });

        return result.ToList();
    }

    private static FemRegionLabel ClassifyRegion(Point3 point, FemExportContext context)
    {
        foreach (var electrode in context.Electrodes)
        {
            if (!electrode.Contains(point))
            {
                continue;
            }

            var layerIndex = electrode.ResolveLayerIndex(point);
            var regionId = FemIds.ElectrodeLayerRegion(electrode.PlacementId, layerIndex);
            return new FemRegionLabel(regionId, regionId);
        }

        foreach (var roi in context.Rois.Where(roi => roi.SourceRoi.Role != RoiRole.External))
        {
            if (roi.Contains(point))
            {
                return new FemRegionLabel(roi.RegionId, roi.MaterialId);
            }
        }

        var body = context.Rois.FirstOrDefault(roi => roi.SourceRoi.Role == RoiRole.External);
        if (body is not null)
        {
            return new FemRegionLabel(body.RegionId, body.MaterialId);
        }

        return new FemRegionLabel(FemIds.DefaultBodyRegionId, FemIds.DefaultBodyMaterialId);
    }

    private static FemBoundaryLabel ClassifyBoundary(Point3 point, FemExportContext context)
    {
        var bestDistance = double.MaxValue;
        FemBoundaryLabel best = new(FemIds.ExternalBoundaryMarker);

        foreach (var electrode in context.Electrodes)
        {
            var distance = electrode.DistanceSquaredToClosestPoint(point);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            best = new FemBoundaryLabel(electrode.IsContactSide(point)
                ? FemIds.ElectrodeContactBoundary(electrode.PlacementId)
                : FemIds.ElectrodeOuterBoundary(electrode.PlacementId));
        }

        foreach (var roi in context.Rois)
        {
            var distance = roi.DistanceSquaredToClosestPoint(point);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            best = new FemBoundaryLabel(roi.BoundaryMarker);
        }

        return best;
    }

    private static void ValidateLabels(
        IReadOnlyList<FemTetrahedron> tetrahedra,
        IReadOnlyList<FemBoundaryFace> boundaryFaces,
        FemExportContext context)
    {
        var materialRegionIds = context.Materials.Select(material => material.RegionId).ToHashSet();
        var missingRegion = tetrahedra.FirstOrDefault(tetra => !materialRegionIds.Contains(tetra.RegionId));
        if (missingRegion is not null)
        {
            throw new InvalidOperationException(
                $"FEM validation failed: element {missingRegion.Id} uses RegionId {missingRegion.RegionId}, but no material table row exists.");
        }

        var boundaryMarkers = context.Boundaries.Select(boundary => boundary.BoundaryMarker).ToHashSet();
        var missingBoundary = boundaryFaces.FirstOrDefault(face => !boundaryMarkers.Contains(face.BoundaryMarker));
        if (missingBoundary is not null)
        {
            throw new InvalidOperationException(
                $"FEM validation failed: boundary face {missingBoundary.Id} uses marker {missingBoundary.BoundaryMarker}, but no boundary table row exists.");
        }

        if (tetrahedra.Any(tetra => tetra.RegionId <= 0 || tetra.MaterialId <= 0))
        {
            throw new InvalidOperationException("FEM validation failed: every tetrahedron must have positive RegionId and MaterialId.");
        }

        if (boundaryFaces.Count > 0 && boundaryFaces.Any(face => face.BoundaryMarker <= 0))
        {
            throw new InvalidOperationException("FEM validation failed: every boundary face must have a positive BoundaryMarker.");
        }
    }

    private static void AddCellArrays(vtkUnstructuredGrid grid, IReadOnlyList<FemTetrahedron> tetrahedra)
    {
        var regionIds = vtkIntArray.New();
        regionIds.SetName(FemArrayNames.RegionId);
        regionIds.SetNumberOfComponents(1);

        var materialIds = vtkIntArray.New();
        materialIds.SetName(FemArrayNames.MaterialId);
        materialIds.SetNumberOfComponents(1);

        for (var i = 0; i < grid.GetNumberOfCells(); i++)
        {
            regionIds.InsertNextTuple1(0);
            materialIds.InsertNextTuple1(0);
        }

        foreach (var tetra in tetrahedra)
        {
            regionIds.SetTuple1(tetra.OriginalCellId, tetra.RegionId);
            materialIds.SetTuple1(tetra.OriginalCellId, tetra.MaterialId);
        }

        grid.GetCellData().AddArray(regionIds);
        grid.GetCellData().AddArray(materialIds);
        grid.GetCellData().SetScalars(regionIds);
        grid.Modified();
    }

    private static void WriteNodes(IReadOnlyList<Point3> points, string path)
    {
        var invariant = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("node_id,x,y,z");
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            builder.AppendLine(string.Create(invariant, $"{i},{point.X:R},{point.Y:R},{point.Z:R}"));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteElements(IReadOnlyList<FemTetrahedron> tetrahedra, string path)
    {
        var invariant = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("element_id,node0,node1,node2,node3,region_id,material_id");
        foreach (var tetra in tetrahedra)
        {
            builder.AppendLine(string.Create(
                invariant,
                $"{tetra.Id},{tetra.A},{tetra.B},{tetra.C},{tetra.D},{tetra.RegionId},{tetra.MaterialId}"));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteBoundaryFaces(IReadOnlyList<FemBoundaryFace> faces, string path)
    {
        var invariant = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("face_id,node0,node1,node2,boundary_marker,owner_region_id");
        foreach (var face in faces)
        {
            builder.AppendLine(string.Create(
                invariant,
                $"{face.Id},{face.A},{face.B},{face.C},{face.BoundaryMarker},{face.OwnerRegionId}"));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteMaterials(
        IReadOnlyList<FemMaterialRecord> materials,
        IReadOnlyList<FemBoundaryRecord> boundaries,
        string path)
    {
        var payload = new
        {
            units = new
            {
                length = "mm",
                electricalConductivity = "S/m",
                relativePermittivity = "dimensionless",
                thermalConductivity = "W/(m*K)",
                density = "kg/m^3",
                specificHeat = "J/(kg*K)"
            },
            regions = materials,
            boundaries
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, options), new UTF8Encoding(false));
    }

    private static void WriteVtu(IReadOnlyList<Point3> points, IReadOnlyList<FemTetrahedron> tetrahedra, string path)
    {
        var invariant = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\"?>");
        builder.AppendLine("<VTKFile type=\"UnstructuredGrid\" version=\"0.1\" byte_order=\"LittleEndian\">");
        builder.AppendLine("  <UnstructuredGrid>");
        builder.AppendLine(string.Create(invariant, $"    <Piece NumberOfPoints=\"{points.Count}\" NumberOfCells=\"{tetrahedra.Count}\">"));
        builder.AppendLine("      <Points>");
        builder.AppendLine("        <DataArray type=\"Float64\" NumberOfComponents=\"3\" format=\"ascii\">");
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            builder.AppendLine(string.Create(invariant, $"          {point.X:R} {point.Y:R} {point.Z:R}"));
        }
        builder.AppendLine("        </DataArray>");
        builder.AppendLine("      </Points>");
        builder.AppendLine("      <Cells>");
        builder.AppendLine("        <DataArray type=\"Int32\" Name=\"connectivity\" format=\"ascii\">");
        foreach (var tetra in tetrahedra)
        {
            builder.AppendLine(string.Create(invariant, $"          {tetra.A} {tetra.B} {tetra.C} {tetra.D}"));
        }
        builder.AppendLine("        </DataArray>");
        builder.AppendLine("        <DataArray type=\"Int32\" Name=\"offsets\" format=\"ascii\">");
        for (var i = 1; i <= tetrahedra.Count; i++)
        {
            builder.AppendLine(string.Create(invariant, $"          {i * 4}"));
        }
        builder.AppendLine("        </DataArray>");
        builder.AppendLine("        <DataArray type=\"UInt8\" Name=\"types\" format=\"ascii\">");
        foreach (var _ in tetrahedra)
        {
            builder.AppendLine("          10");
        }
        builder.AppendLine("        </DataArray>");
        builder.AppendLine("      </Cells>");
        builder.AppendLine("      <CellData Scalars=\"RegionId\">");
        builder.AppendLine("        <DataArray type=\"Int32\" Name=\"RegionId\" format=\"ascii\">");
        foreach (var tetra in tetrahedra)
        {
            builder.AppendLine(string.Create(invariant, $"          {tetra.RegionId}"));
        }
        builder.AppendLine("        </DataArray>");
        builder.AppendLine("        <DataArray type=\"Int32\" Name=\"MaterialId\" format=\"ascii\">");
        foreach (var tetra in tetrahedra)
        {
            builder.AppendLine(string.Create(invariant, $"          {tetra.MaterialId}"));
        }
        builder.AppendLine("        </DataArray>");
        builder.AppendLine("      </CellData>");
        builder.AppendLine("    </Piece>");
        builder.AppendLine("  </UnstructuredGrid>");
        builder.AppendLine("</VTKFile>");

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteManifest(
        FemExportPaths paths,
        FemExportContext context,
        int nodeCount,
        int tetrahedronCount,
        int boundaryFaceCount)
    {
        var payload = new
        {
            format = "MeshGenLite FEM package",
            version = 1,
            generator = "MeshGenLite",
            volumeMesher = "vtkDelaunay3D",
            note = "This package carries FEM labels and materials. For strict multi-region conforming tetrahedra, replace the meshing step with the PLC/TetGen or Gmsh path described in 핵심 수정.md.",
            files = new
            {
                geometryMesh = Path.GetFileName(paths.VtuPath),
                nodes = Path.GetFileName(paths.NodesPath),
                elements = Path.GetFileName(paths.ElementsPath),
                boundaryFaces = Path.GetFileName(paths.BoundaryFacesPath),
                materialTable = Path.GetFileName(paths.MaterialsPath)
            },
            counts = new
            {
                nodes = nodeCount,
                tetrahedra = tetrahedronCount,
                boundaryFaces = boundaryFaceCount,
                regions = context.Materials.Count,
                boundaries = context.Boundaries.Count
            },
            cellData = new[] { FemArrayNames.RegionId, FemArrayNames.MaterialId },
            faceData = new[] { FemArrayNames.BoundaryMarker }
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(paths.ManifestPath, JsonSerializer.Serialize(payload, options), new UTF8Encoding(false));
    }

    private static FemExportPaths BuildPaths(string outputDirectory, string baseName)
        => new(
            Path.Combine(outputDirectory, $"{baseName}.vtu"),
            Path.Combine(outputDirectory, $"{baseName}.nodes.csv"),
            Path.Combine(outputDirectory, $"{baseName}.elements.csv"),
            Path.Combine(outputDirectory, $"{baseName}.boundary_faces.csv"),
            Path.Combine(outputDirectory, $"{baseName}.materials.json"),
            Path.Combine(outputDirectory, $"{baseName}.manifest.json"));

    private static string SanitizeBaseName(string baseName)
    {
        var fallback = string.IsNullOrWhiteSpace(baseName) ? "meshgen_fem" : Path.GetFileNameWithoutExtension(baseName);
        var invalid = Path.GetInvalidFileNameChars();
        var chars = fallback.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static void AddFace(Dictionary<FaceKey, FaceAccumulator> faces, FemTetrahedron tetra, int a, int b, int c)
    {
        var key = FaceKey.Create(a, b, c);
        if (faces.TryGetValue(key, out var accumulator))
        {
            accumulator.Count++;
            return;
        }

        faces[key] = new FaceAccumulator(a, b, c, tetra.RegionId) { Count = 1 };
    }

    private static Point3 ComputeCentroid(IReadOnlyList<Point3> points, int a, int b, int c, int d)
    {
        var p0 = points[a];
        var p1 = points[b];
        var p2 = points[c];
        var p3 = points[d];
        return new Point3(
            (p0.X + p1.X + p2.X + p3.X) * 0.25,
            (p0.Y + p1.Y + p2.Y + p3.Y) * 0.25,
            (p0.Z + p1.Z + p2.Z + p3.Z) * 0.25);
    }

    private static Point3 ComputeCentroid(IReadOnlyList<Point3> points, int a, int b, int c)
    {
        var p0 = points[a];
        var p1 = points[b];
        var p2 = points[c];
        return new Point3(
            (p0.X + p1.X + p2.X) / 3.0,
            (p0.Y + p1.Y + p2.Y) / 3.0,
            (p0.Z + p1.Z + p2.Z) / 3.0);
    }

    private class SurfaceClassifier
    {
        private readonly double[] bounds;
        private readonly Point3[] vertices;
        private readonly List<Triangle3> triangles;

        protected SurfaceClassifier(vtkPolyData surface)
        {
            var triangulated = EnsureTriangulated(surface);
            bounds = triangulated.GetBounds().ToArray();
            vertices = ReadPoints(triangulated);
            triangles = ReadTriangles(triangulated);
        }

        public bool Contains(Point3 point)
        {
            const double eps = 1e-7;
            if (point.X < bounds[0] - eps || point.X > bounds[1] + eps ||
                point.Y < bounds[2] - eps || point.Y > bounds[3] + eps ||
                point.Z < bounds[4] - eps || point.Z > bounds[5] + eps)
            {
                return false;
            }

            var intersections = 0;
            foreach (var triangle in triangles)
            {
                if (RayIntersectsTriangle(point, triangle))
                {
                    intersections++;
                }
            }

            return intersections % 2 == 1;
        }

        public double DistanceSquaredToClosestPoint(Point3 point)
        {
            if (vertices.Length == 0)
            {
                return double.MaxValue;
            }

            var best = double.MaxValue;
            foreach (var vertex in vertices)
            {
                var dx = vertex.X - point.X;
                var dy = vertex.Y - point.Y;
                var dz = vertex.Z - point.Z;
                var distance = (dx * dx) + (dy * dy) + (dz * dz);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
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

        private static Point3[] ReadPoints(vtkPolyData surface)
        {
            var result = new Point3[(int)surface.GetNumberOfPoints()];
            for (var id = 0; id < result.Length; id++)
            {
                result[id] = PointFrom(surface, id);
            }

            return result;
        }

        private static List<Triangle3> ReadTriangles(vtkPolyData surface)
        {
            var result = new List<Triangle3>();
            var polys = surface.GetPolys();
            if (polys is null)
            {
                return result;
            }

            polys.InitTraversal();
            var ids = vtkIdList.New();
            while (polys.GetNextCell(ids) != 0)
            {
                if (ids.GetNumberOfIds() != 3)
                {
                    continue;
                }

                result.Add(new Triangle3(
                    PointFrom(surface, (int)ids.GetId(0)),
                    PointFrom(surface, (int)ids.GetId(1)),
                    PointFrom(surface, (int)ids.GetId(2))));
            }

            return result;
        }

        private static Point3 PointFrom(vtkPolyData surface, int id)
        {
            var point = surface.GetPoint(id);
            return new Point3(point[0], point[1], point[2]);
        }

        private static bool RayIntersectsTriangle(Point3 origin, Triangle3 triangle)
        {
            const double eps = 1e-9;
            var edge1 = triangle.B - triangle.A;
            var edge2 = triangle.C - triangle.A;
            var h = Point3.Cross(Point3.UnitX, edge2);
            var determinant = Point3.Dot(edge1, h);
            if (Math.Abs(determinant) < eps)
            {
                return false;
            }

            var inverse = 1.0 / determinant;
            var s = origin - triangle.A;
            var u = inverse * Point3.Dot(s, h);
            if (u < 0.0 || u > 1.0)
            {
                return false;
            }

            var q = Point3.Cross(s, edge1);
            var v = inverse * Point3.Dot(Point3.UnitX, q);
            if (v < 0.0 || u + v > 1.0)
            {
                return false;
            }

            var t = inverse * Point3.Dot(edge2, q);
            return t > eps;
        }
    }

    private sealed class RoiSurfaceClassifier : SurfaceClassifier
    {
        public RoiSurfaceClassifier(RoiDescriptor roi, vtkPolyData surface, int regionId, int materialId, int boundaryMarker)
            : base(surface)
        {
            SourceRoi = roi;
            RegionId = regionId;
            MaterialId = materialId;
            BoundaryMarker = boundaryMarker;
        }

        public RoiDescriptor SourceRoi { get; }

        public int RegionId { get; }

        public int MaterialId { get; }

        public int BoundaryMarker { get; }
    }

    private sealed class ElectrodeSurfaceClassifier : SurfaceClassifier
    {
        private readonly Point3 basePoint;
        private readonly Point3 normal;
        private readonly double[] cumulativeThickness;

        public ElectrodeSurfaceClassifier(FemElectrodeExportInfo electrode, ElectrodeSpec spec)
            : base(electrode.Surface)
        {
            PlacementId = electrode.PlacementId;
            basePoint = new Point3(electrode.BasePoint.X, electrode.BasePoint.Y, electrode.BasePoint.Z);
            var n = electrode.Normal;
            if (n.LengthSquared < 1e-8)
            {
                n = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
            }
            n.Normalize();
            normal = new Point3(n.X, n.Y, n.Z);
            cumulativeThickness = BuildCumulativeThickness(spec);
        }

        public int PlacementId { get; }

        public int ResolveLayerIndex(Point3 point)
        {
            var axial = Point3.Dot(point - basePoint, normal);
            for (var i = 0; i < cumulativeThickness.Length; i++)
            {
                if (axial <= cumulativeThickness[i])
                {
                    return i;
                }
            }

            return Math.Max(cumulativeThickness.Length - 1, 0);
        }

        public bool IsContactSide(Point3 point)
        {
            var axial = Point3.Dot(point - basePoint, normal);
            var tolerance = Math.Max(cumulativeThickness.LastOrDefault() * 0.05, 0.25);
            return axial <= tolerance;
        }

        private static double[] BuildCumulativeThickness(ElectrodeSpec spec)
        {
            var result = new List<double>();
            var z = 0.0;
            foreach (var layer in spec.Layers)
            {
                if (layer.Thickness <= 0)
                {
                    continue;
                }

                z += layer.Thickness;
                result.Add(z);
            }

            return result.Count == 0 ? new[] { 1.0 } : result.ToArray();
        }
    }

    private sealed record FemExportContext(
        List<FemMaterialRecord> Materials,
        List<FemBoundaryRecord> Boundaries,
        List<RoiSurfaceClassifier> Rois,
        List<ElectrodeSurfaceClassifier> Electrodes,
        ElectrodeSpec ElectrodeSpec);

    private sealed record FemExportPaths(
        string VtuPath,
        string NodesPath,
        string ElementsPath,
        string BoundaryFacesPath,
        string MaterialsPath,
        string ManifestPath);

    private readonly record struct FemRegionLabel(int RegionId, int MaterialId);

    private readonly record struct FemBoundaryLabel(int BoundaryMarker);

    private sealed record GridSnapshot(Point3[] Points, List<RawTetrahedron> Tetrahedra);

    private readonly record struct RawTetrahedron(int OriginalCellId, int A, int B, int C, int D);

    private sealed record FemTetrahedron(int Id, int OriginalCellId, int A, int B, int C, int D, int RegionId, int MaterialId);

    private sealed record FemBoundaryFace(int Id, int A, int B, int C, int BoundaryMarker, int OwnerRegionId);

    private sealed class FaceAccumulator
    {
        public FaceAccumulator(int a, int b, int c, int ownerRegionId)
        {
            A = a;
            B = b;
            C = c;
            OwnerRegionId = ownerRegionId;
        }

        public int A { get; }

        public int B { get; }

        public int C { get; }

        public int OwnerRegionId { get; }

        public int Count { get; set; }
    }

    private readonly record struct FaceKey(int A, int B, int C)
    {
        public static FaceKey Create(int a, int b, int c)
        {
            Span<int> values = stackalloc[] { a, b, c };
            values.Sort();
            return new FaceKey(values[0], values[1], values[2]);
        }
    }

    private readonly record struct Triangle3(Point3 A, Point3 B, Point3 C);

    private readonly record struct Point3(double X, double Y, double Z)
    {
        public static Point3 UnitX => new(1.0, 0.0, 0.0);

        public static Point3 operator -(Point3 left, Point3 right)
            => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static double Dot(Point3 left, Point3 right)
            => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

        public static Point3 Cross(Point3 left, Point3 right)
            => new(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));
    }
}
