using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using FellowOakDicom;
using Kitware.VTK;
using MeshGenLite.Models;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace MeshGenLite.Services;

public interface IPipelineService
{
    Task<StudyContext> ImportAsync(StudyImportRequest request, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken);

    Task<vtkImageData> BuildLabelMapAsync(StudyContext study, IReadOnlyList<RoiDescriptor> selectedRois, double spacing, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken);

    Task<IReadOnlyList<SurfaceMeshInfo>> BuildSurfaceAsync(StudyContext study, vtkImageData labelMap, IReadOnlyList<RoiDescriptor> selectedRois, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken);

    Task<IReadOnlyList<vtkPolyData>> CreateElectrodesAsync(ElectrodeSpec spec, IReadOnlyList<ElectrodePlacement> placements, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken);

    Task<(IReadOnlyList<vtkPolyData> Electrodes, vtkPolyData MergedPreview)> SnapElectrodesAsync(
        vtkPolyData externalSurface,
        ElectrodeSpec spec,
        IReadOnlyList<ElectrodePlacement> placements,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class PipelineService : IPipelineService
{
    private static readonly WpfColor[] Palette =
    {
        (WpfColor)WpfColorConverter.ConvertFromString("#60CDFF"),
        (WpfColor)WpfColorConverter.ConvertFromString("#0078D4"),
        (WpfColor)WpfColorConverter.ConvertFromString("#22C55E"),
        (WpfColor)WpfColorConverter.ConvertFromString("#EAB308"),
        (WpfColor)WpfColorConverter.ConvertFromString("#F97316"),
        (WpfColor)WpfColorConverter.ConvertFromString("#F43F5E"),
        (WpfColor)WpfColorConverter.ConvertFromString("#A78BFA"),
        (WpfColor)WpfColorConverter.ConvertFromString("#14B8A6")
    };

    private const int VtkChar = 2;
    private const int VtkUnsignedChar = 3;
    private const int VtkShort = 4;
    private const int VtkUnsignedShort = 5;
    private const int VtkInt = 6;
    private const int VtkUnsignedInt = 7;
    private const int VtkLong = 8;
    private const int VtkUnsignedLong = 9;
    private const int VtkFloat = 10;
    private const int VtkDouble = 11;
    private const int VtkSignedChar = 15;
    private const int VtkLongLong = 16;
    private const int VtkUnsignedLongLong = 17;
    private const int VtkBool = 18;

    public Task<StudyContext> ImportAsync(StudyImportRequest request, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (request.NiftiPaths.Count > 0 || IsNiftiPath(request.CtSeriesPath))
            {
                return ImportMergedNiftiStudy(request, progress, cancellationToken);
            }

            return ImportDicomStudy(request, progress, cancellationToken);
        }, cancellationToken);
    }

    public Task<vtkImageData> BuildLabelMapAsync(StudyContext study, IReadOnlyList<RoiDescriptor> selectedRois, double spacing, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (study.SourceKind == ImportSourceKind.NiftiLabelMap && study.NativeLabelMap is not null)
            {
                progress?.Report(new PipelineProgress
                {
                    Message = "Using NIfTI label map",
                    Detail = $"Native spacing {study.Spacing[0]:F2}/{study.Spacing[1]:F2}/{study.Spacing[2]:F2} mm"
                });

                var labelMap = vtkImageData.New();
                labelMap.DeepCopy(study.NativeLabelMap);
                return labelMap;
            }

            if (spacing <= 0)
            {
                throw new InvalidOperationException("Target spacing must be greater than zero.");
            }

            var rois = selectedRois.Where(roi => roi.IsSelected && roi.HasContours).ToList();
            if (rois.Count == 0)
            {
                throw new InvalidOperationException("Select at least one ROI with contours to build the label map.");
            }

            progress?.Report(new PipelineProgress { Message = "Creating label map", Detail = $"Spacing {spacing:F2} mm" });

            var minX = study.Bounds[0];
            var maxX = study.Bounds[1];
            var minY = study.Bounds[2];
            var maxY = study.Bounds[3];
            var minZ = study.Bounds[4];
            var maxZ = study.Bounds[5];

            var width = Math.Max(2, (int)Math.Ceiling((maxX - minX) / spacing) + 2);
            var height = Math.Max(2, (int)Math.Ceiling((maxY - minY) / spacing) + 2);
            var depth = Math.Max(2, (int)Math.Ceiling((maxZ - minZ) / spacing) + 2);
            var labels = new ushort[width * height * depth];

            foreach (var roi in rois.OrderBy(roi => roi.Role == RoiRole.External ? 0 : 1).ThenBy(roi => roi.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new PipelineProgress { Message = $"Rasterizing {roi.Name}", Detail = $"ROI {roi.Id}" });

                var planeMasks = new Dictionary<int, bool[]>();
                foreach (var contour in roi.Contours)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var zIndex = Clamp((int)Math.Round((contour.Z - minZ) / spacing), 0, depth - 1);
                    if (!planeMasks.TryGetValue(zIndex, out var mask))
                    {
                        mask = new bool[width * height];
                        planeMasks[zIndex] = mask;
                    }

                    RasterizeContour(mask, width, height, spacing, minX, minY, contour.Points);
                }

                if (planeMasks.Count == 0)
                {
                    continue;
                }

                var orderedPlanes = planeMasks.Keys.OrderBy(index => index).ToArray();
                for (var index = 0; index < orderedPlanes.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var currentPlane = orderedPlanes[index];
                    ApplyPlaneMask(labels, width, height, depth, currentPlane, planeMasks[currentPlane], (ushort)roi.Id);

                    if (index == orderedPlanes.Length - 1)
                    {
                        continue;
                    }

                    var nextPlane = orderedPlanes[index + 1];
                    var gap = nextPlane - currentPlane;
                    if (gap <= 1)
                    {
                        continue;
                    }

                    var currentMask = planeMasks[currentPlane];
                    var nextMask = planeMasks[nextPlane];
                    for (var fillPlane = currentPlane + 1; fillPlane < nextPlane; fillPlane++)
                    {
                        var distanceToCurrent = fillPlane - currentPlane;
                        var distanceToNext = nextPlane - fillPlane;
                        bool[] sourceMask;
                        if (distanceToCurrent == distanceToNext)
                        {
                            sourceMask = MergeMasks(currentMask, nextMask);
                        }
                        else
                        {
                            sourceMask = distanceToCurrent < distanceToNext ? currentMask : nextMask;
                        }

                        ApplyPlaneMask(labels, width, height, depth, fillPlane, sourceMask, (ushort)roi.Id);
                    }
                }
            }

            progress?.Report(new PipelineProgress { Message = "Copying label map to VTK", Detail = $"{width}x{height}x{depth}" });
            return CreateImageData(labels, width, height, depth, spacing, spacing, spacing, minX, minY, minZ);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SurfaceMeshInfo>> BuildSurfaceAsync(StudyContext study, vtkImageData labelMap, IReadOnlyList<RoiDescriptor> selectedRois, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var results = new List<SurfaceMeshInfo>();
            foreach (var roi in selectedRois.Where(roi => roi.IsSelected && (roi.HasContours || study.SourceKind == ImportSourceKind.NiftiLabelMap)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new PipelineProgress { Message = $"Generating surface", Detail = roi.Name });

                var threshold = vtkImageThreshold.New();
                threshold.SetInputData(labelMap);
                threshold.ThresholdBetween(roi.Id, roi.Id);
                threshold.ReplaceInOn();
                threshold.SetInValue(1);
                threshold.ReplaceOutOn();
                threshold.SetOutValue(0);
                threshold.SetOutputScalarTypeToUnsignedChar();
                threshold.Update();

                var marchingCubes = vtkDiscreteMarchingCubes.New();
                marchingCubes.SetInputConnection(threshold.GetOutputPort());
                marchingCubes.GenerateValues(1, 1, 1);
                marchingCubes.Update();

                var smoother = vtkWindowedSincPolyDataFilter.New();
                smoother.SetInputConnection(marchingCubes.GetOutputPort());
                smoother.SetNumberOfIterations(10);
                smoother.BoundarySmoothingOff();
                smoother.FeatureEdgeSmoothingOff();
                smoother.SetPassBand(0.15);
                smoother.NonManifoldSmoothingOn();
                smoother.NormalizeCoordinatesOn();
                smoother.Update();

                var decimator = vtkDecimatePro.New();
                decimator.SetInputConnection(smoother.GetOutputPort());
                decimator.SetTargetReduction(0.20);
                decimator.PreserveTopologyOn();
                decimator.Update();

                var normals = vtkPolyDataNormals.New();
                normals.SetInputConnection(decimator.GetOutputPort());
                normals.ConsistencyOn();
                normals.SplittingOff();
                normals.AutoOrientNormalsOn();
                normals.Update();

                var mesh = vtkPolyData.New();
                mesh.DeepCopy(normals.GetOutput());
                if (mesh.GetNumberOfPoints() == 0)
                {
                    continue;
                }

                results.Add(new SurfaceMeshInfo
                {
                    Roi = roi,
                    PolyData = mesh,
                    VertexCount = (int)mesh.GetNumberOfPoints(),
                    TriangleCount = (int)mesh.GetNumberOfPolys()
                });
            }

            progress?.Report(new PipelineProgress
            {
                Message = "Surface generation complete",
                Detail = $"{results.Count} ROI meshes"
            });

            return (IReadOnlyList<SurfaceMeshInfo>)results;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<vtkPolyData>> CreateElectrodesAsync(ElectrodeSpec spec, IReadOnlyList<ElectrodePlacement> placements, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var results = new List<vtkPolyData>();
            foreach (var placement in placements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new PipelineProgress { Message = $"Creating electrode {placement.Id}", Detail = placement.RequestedSummary });

                var source = vtkCylinderSource.New();
                source.SetRadius(spec.Radius);
                source.SetHeight(spec.Height);
                source.SetResolution(48);
                source.CappingOn();
                source.Update();

                var transform = CreatePlacementTransform(placement.RequestedPoint, placement.Normal, spec.Height);
                var filter = vtkTransformPolyDataFilter.New();
                filter.SetInputConnection(source.GetOutputPort());
                filter.SetTransform(transform);
                filter.Update();

                var polyData = vtkPolyData.New();
                polyData.DeepCopy(filter.GetOutput());
                results.Add(polyData);
            }

            return (IReadOnlyList<vtkPolyData>)results;
        }, cancellationToken);
    }

    public Task<(IReadOnlyList<vtkPolyData> Electrodes, vtkPolyData MergedPreview)> SnapElectrodesAsync(
        vtkPolyData externalSurface,
        ElectrodeSpec spec,
        IReadOnlyList<ElectrodePlacement> placements,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var normalFilter = vtkPolyDataNormals.New();
            normalFilter.SetInputData(externalSurface);
            normalFilter.ComputePointNormalsOn();
            normalFilter.SplittingOff();
            normalFilter.AutoOrientNormalsOn();
            normalFilter.Update();

            var surfaceWithNormals = vtkPolyData.New();
            surfaceWithNormals.DeepCopy(normalFilter.GetOutput());

            var pointLocator = vtkPointLocator.New();
            pointLocator.SetDataSet(surfaceWithNormals);
            pointLocator.BuildLocator();

            var normals = surfaceWithNormals.GetPointData().GetNormals();
            if (normals is null)
            {
                throw new InvalidOperationException("External surface does not contain normals.");
            }

            var append = vtkAppendPolyData.New();
            var snappedMeshes = new List<vtkPolyData>();
            foreach (var placement in placements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new PipelineProgress { Message = $"Snapping electrode {placement.Id}", Detail = placement.RequestedSummary });

                var pointId = pointLocator.FindClosestPoint(placement.RequestedPoint.X, placement.RequestedPoint.Y, placement.RequestedPoint.Z);
                var closestPoint = surfaceWithNormals.GetPoint(pointId);
                var normalTuple = normals.GetTuple3(pointId);
                var normal = new Vector3D(normalTuple[0], normalTuple[1], normalTuple[2]);
                normal.Normalize();

                placement.SnappedPoint = new Point3D(closestPoint[0], closestPoint[1], closestPoint[2]);
                placement.Normal = normal;

                var source = vtkCylinderSource.New();
                source.SetRadius(spec.Radius);
                source.SetHeight(spec.Height);
                source.SetResolution(48);
                source.CappingOn();
                source.Update();

                var transform = CreatePlacementTransform(placement.SnappedPoint, placement.Normal, spec.Height);
                var filter = vtkTransformPolyDataFilter.New();
                filter.SetInputConnection(source.GetOutputPort());
                filter.SetTransform(transform);
                filter.Update();

                var snapped = vtkPolyData.New();
                snapped.DeepCopy(filter.GetOutput());
                snappedMeshes.Add(snapped);
                append.AddInputData(snapped);
            }

            var clean = vtkCleanPolyData.New();
            clean.SetInputConnection(append.GetOutputPort());
            clean.Update();

            var merged = vtkPolyData.New();
            merged.DeepCopy(clean.GetOutput());
            return ((IReadOnlyList<vtkPolyData>)snappedMeshes, merged);
        }, cancellationToken);
    }

    private StudyContext ImportDicomStudy(StudyImportRequest request, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(request.CtSeriesPath))
        {
            throw new DirectoryNotFoundException($"DICOM series path not found: {request.CtSeriesPath}");
        }

        if (!File.Exists(request.RtssPath))
        {
            throw new FileNotFoundException("RTSS file not found.", request.RtssPath);
        }

        progress?.Report(new PipelineProgress { Message = "Reading DICOM series", Detail = request.CtSeriesPath });

        var sliceRecords = new List<(CtSliceInfo Slice, int Rows, int Columns, double[] PixelSpacing)>();
        foreach (var path in Directory.EnumerateFiles(request.CtSeriesPath, "*.dcm", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dataset = DicomFile.Open(path).Dataset;
            var modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, string.Empty);
            if (!string.Equals(modality, "CT", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(modality, "MR", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ipp = dataset.GetValues<double>(DicomTag.ImagePositionPatient);
            var iop = dataset.GetValues<double>(DicomTag.ImageOrientationPatient);
            var rowDirection = new Vector3D(iop[0], iop[1], iop[2]);
            var columnDirection = new Vector3D(iop[3], iop[4], iop[5]);
            var normal = Vector3D.CrossProduct(rowDirection, columnDirection);
            normal.Normalize();

            var position = new Point3D(ipp[0], ipp[1], ipp[2]);
            var projection = Vector3D.DotProduct((Vector3D)position, normal);
            var rows = dataset.GetSingleValue<int>(DicomTag.Rows);
            var columns = dataset.GetSingleValue<int>(DicomTag.Columns);
            var pixelSpacing = dataset.GetValues<double>(DicomTag.PixelSpacing);

            sliceRecords.Add((
                new CtSliceInfo
                {
                    Path = path,
                    ImagePosition = position,
                    Normal = normal,
                    Projection = projection
                },
                rows,
                columns,
                pixelSpacing));
        }

        if (sliceRecords.Count == 0)
        {
            throw new InvalidOperationException("No CT or MR slices were found in the selected folder.");
        }

        sliceRecords.Sort((a, b) => a.Slice.Projection.CompareTo(b.Slice.Projection));
        var first = sliceRecords[0];
        var spacingX = first.PixelSpacing[1];
        var spacingY = first.PixelSpacing[0];
        var spacingZ = sliceRecords.Count > 1
            ? Math.Abs(sliceRecords[1].Slice.Projection - sliceRecords[0].Slice.Projection)
            : 1.0;

        var dimensions = new[] { first.Columns, first.Rows, sliceRecords.Count };
        var origin = new[]
        {
            sliceRecords.Min(s => s.Slice.ImagePosition.X),
            sliceRecords.Min(s => s.Slice.ImagePosition.Y),
            sliceRecords.Min(s => s.Slice.ImagePosition.Z)
        };
        var maxBounds = new[]
        {
            origin[0] + spacingX * dimensions[0],
            origin[1] + spacingY * dimensions[1],
            origin[2] + spacingZ * dimensions[2]
        };

        progress?.Report(new PipelineProgress { Message = "Reading RT structure set", Detail = request.RtssPath });
        var rtss = DicomFile.Open(request.RtssPath).Dataset;
        var structureSet = rtss.GetSequence(DicomTag.StructureSetROISequence);
        var contourSet = rtss.GetSequence(DicomTag.ROIContourSequence);

        var roiNames = structureSet.Items
            .ToDictionary(
                item => item.GetSingleValue<int>(DicomTag.ROINumber),
                item => item.GetSingleValueOrDefault(DicomTag.ROIName, $"ROI {item.GetSingleValue<int>(DicomTag.ROINumber)}"));

        var contoursByRoi = new Dictionary<int, List<RoiContourData>>();
        foreach (var contourItem in contourSet.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var roiId = contourItem.GetSingleValue<int>(DicomTag.ReferencedROINumber);
            if (!contourItem.TryGetSequence(DicomTag.ContourSequence, out var contourSequence))
            {
                continue;
            }

            var list = contoursByRoi.TryGetValue(roiId, out var existing)
                ? existing
                : contoursByRoi[roiId] = new List<RoiContourData>();

            foreach (var sliceContour in contourSequence.Items)
            {
                if (!sliceContour.TryGetValues(DicomTag.ContourData, out double[]? rawPoints) || rawPoints is null || rawPoints.Length < 9)
                {
                    continue;
                }

                var points = new List<Point3D>(rawPoints.Length / 3);
                for (var index = 0; index < rawPoints.Length; index += 3)
                {
                    points.Add(new Point3D(rawPoints[index], rawPoints[index + 1], rawPoints[index + 2]));
                }

                list.Add(new RoiContourData
                {
                    Points = points,
                    Z = points.Average(point => point.Z)
                });
            }
        }

        var rois = BuildRoiCollectionFromContours(roiNames, contoursByRoi);
        var study = new StudyContext
        {
            CtSeriesPath = request.CtSeriesPath,
            SourcePaths = new[] { request.CtSeriesPath },
            RtssPath = request.RtssPath,
            Origin = origin,
            Spacing = new[] { spacingX, spacingY, spacingZ },
            Dimensions = dimensions,
            Bounds = new[] { origin[0], maxBounds[0], origin[1], maxBounds[1], origin[2], maxBounds[2] },
            Slices = sliceRecords.Select(record => record.Slice).ToArray(),
            Rois = rois,
            ExternalRoiId = rois.FirstOrDefault(roi => roi.Role == RoiRole.External && roi.HasContours)?.Id,
            SourceKind = ImportSourceKind.DicomSeries
        };

        progress?.Report(new PipelineProgress
        {
            Message = "Import complete",
            Detail = $"{study.SliceCount} DICOM slices · {study.Rois.Count(roi => roi.HasContours)} contour ROI"
        });

        return study;
    }

    private StudyContext ImportMergedNiftiStudy(StudyImportRequest request, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        var niftiPaths = ResolveNiftiPaths(request);
        if (niftiPaths.Count == 0)
        {
            throw new InvalidOperationException("Select at least one NIfTI file.");
        }

        progress?.Report(new PipelineProgress
        {
            Message = "Reading NIfTI label maps",
            Detail = $"{niftiPaths.Count} file(s)"
        });

        var referenceImage = ReadNiftiImage(niftiPaths[0]);
        if (referenceImage.GetNumberOfPoints() == 0)
        {
            throw new InvalidOperationException("The selected NIfTI file did not produce any voxel data.");
        }

        var origin = referenceImage.GetOrigin();
        var spacing = referenceImage.GetSpacing();
        var dimensions = referenceImage.GetDimensions();
        var bounds = referenceImage.GetBounds();
        var width = dimensions[0];
        var height = dimensions[1];
        var depth = dimensions[2];
        var mergedLabels = new ushort[width * height * depth];
        var importedLabels = new List<NiftiImportedLabel>();
        var nextGlobalLabel = 1;

        for (var pathIndex = 0; pathIndex < niftiPaths.Count; pathIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = niftiPaths[pathIndex];
            progress?.Report(new PipelineProgress
            {
                Message = $"Scanning NIfTI {pathIndex + 1}/{niftiPaths.Count}",
                Detail = Path.GetFileName(path)
            });

            var image = pathIndex == 0 ? referenceImage : ReadNiftiImage(path);
            ValidateNiftiGeometry(referenceImage, image, path);

            var scalarBytes = CopyScalarBytesToManaged(image, dimensions, cancellationToken);
            var sliceResults = ScanNiftiSlicesParallel(scalarBytes, image.GetScalarType(), width, height, depth, cancellationToken);
            var localLabels = sliceResults
                .Where(result => result is not null)
                .SelectMany(result => result!.PlaneMasks.Keys)
                .Distinct()
                .OrderBy(label => label)
                .ToArray();

            if (localLabels.Length == 0)
            {
                continue;
            }

            var sourceName = GetNiftiSourceName(path);
            var labelMap = new Dictionary<int, NiftiImportedLabel>();
            foreach (var localLabel in localLabels)
            {
                if (nextGlobalLabel > ushort.MaxValue)
                {
                    throw new InvalidOperationException("Too many merged labels for a UInt16 label map.");
                }

                var labelName = localLabels.Length == 1
                    ? sourceName
                    : $"{sourceName} / Label {localLabel}";
                var importedLabel = new NiftiImportedLabel(
                    (ushort)nextGlobalLabel++,
                    labelName,
                    DetectRole(labelName));

                importedLabels.Add(importedLabel);
                labelMap[localLabel] = importedLabel;
            }

            for (var z = 0; z < sliceResults.Length; z++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sliceResult = sliceResults[z];
                if (sliceResult is null)
                {
                    continue;
                }

                foreach (var pair in sliceResult.PlaneMasks)
                {
                    var importedLabel = labelMap[pair.Key];
                    importedLabel.PlaneMasks[z] = pair.Value;
                    importedLabel.VoxelCount += sliceResult.VoxelCounts[pair.Key];
                    ApplyPlaneMask(mergedLabels, width, height, depth, z, pair.Value, importedLabel.GlobalLabel);
                }
            }
        }

        if (importedLabels.Count == 0)
        {
            throw new InvalidOperationException("The selected NIfTI files do not contain any non-zero label values.");
        }

        var rois = BuildRoiCollectionFromImportedNifti(importedLabels, origin, spacing, width, height);
        var external = rois
            .Where(roi => roi.Role == RoiRole.External && roi.HasContours)
            .OrderByDescending(roi => importedLabels.First(label => label.GlobalLabel == roi.Id).VoxelCount)
            .ThenBy(roi => roi.Id)
            .FirstOrDefault()
            ?? rois
                .Where(roi => roi.HasContours)
                .OrderByDescending(roi => importedLabels.First(label => label.GlobalLabel == roi.Id).VoxelCount)
                .ThenBy(roi => roi.Id)
                .FirstOrDefault();

        if (external is not null)
        {
            external.Role = RoiRole.External;
            external.Opacity = 0.22;
        }

        var mergedImage = CreateImageData(
            mergedLabels,
            width,
            height,
            depth,
            spacing[0],
            spacing[1],
            spacing[2],
            origin[0],
            origin[1],
            origin[2]);

        progress?.Report(new PipelineProgress
        {
            Message = "NIfTI import complete",
            Detail = $"{niftiPaths.Count} files · {rois.Count} merged labels"
        });

        return new StudyContext
        {
            CtSeriesPath = niftiPaths[0],
            SourcePaths = niftiPaths,
            RtssPath = string.Empty,
            Origin = new[] { origin[0], origin[1], origin[2] },
            Spacing = new[] { spacing[0], spacing[1], spacing[2] },
            Dimensions = new[] { width, height, depth },
            Bounds = new[] { bounds[0], bounds[1], bounds[2], bounds[3], bounds[4], bounds[5] },
            Slices = BuildPseudoSlices(niftiPaths[0], origin, spacing, dimensions),
            Rois = rois,
            ExternalRoiId = external?.Id,
            SourceKind = ImportSourceKind.NiftiLabelMap,
            NativeLabelMap = mergedImage
        };
    }

    private StudyContext ImportNiftiStudy(StudyImportRequest request, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.CtSeriesPath))
        {
            throw new FileNotFoundException("NIfTI file not found.", request.CtSeriesPath);
        }

        progress?.Report(new PipelineProgress { Message = "Reading NIfTI label map", Detail = request.CtSeriesPath });

        var reader = vtkNIFTIImageReader.New();
        reader.SetFileName(request.CtSeriesPath);
        reader.Update();

        var image = vtkImageData.New();
        image.DeepCopy(reader.GetOutput());
        if (image.GetNumberOfPoints() == 0)
        {
            throw new InvalidOperationException("The selected NIfTI file did not produce any voxel data.");
        }

        var origin = image.GetOrigin();
        var spacing = image.GetSpacing();
        var dimensions = image.GetDimensions();
        var bounds = image.GetBounds();
        var rois = BuildRoiCollectionFromNifti(image, progress, cancellationToken);
        if (rois.Count == 0)
        {
            throw new InvalidOperationException("The selected NIfTI file does not contain any non-zero label values.");
        }

        var external = rois
            .Where(roi => roi.HasContours)
            .OrderByDescending(roi => roi.Contours.Count)
            .ThenBy(roi => roi.Id)
            .FirstOrDefault();

        if (external is not null)
        {
            external.Role = RoiRole.External;
            external.Opacity = 0.22;
        }

        progress?.Report(new PipelineProgress
        {
            Message = "NIfTI import complete",
            Detail = $"{dimensions[2]} slices · {rois.Count} labels"
        });

        return new StudyContext
        {
            CtSeriesPath = request.CtSeriesPath,
            RtssPath = string.Empty,
            Origin = new[] { origin[0], origin[1], origin[2] },
            Spacing = new[] { spacing[0], spacing[1], spacing[2] },
            Dimensions = new[] { dimensions[0], dimensions[1], dimensions[2] },
            Bounds = new[] { bounds[0], bounds[1], bounds[2], bounds[3], bounds[4], bounds[5] },
            Slices = BuildPseudoSlices(request.CtSeriesPath, origin, spacing, dimensions),
            Rois = rois,
            ExternalRoiId = external?.Id,
            SourceKind = ImportSourceKind.NiftiLabelMap,
            NativeLabelMap = image
        };
    }

    private static ObservableCollection<RoiDescriptor> BuildRoiCollectionFromContours(
        IReadOnlyDictionary<int, string> roiNames,
        IReadOnlyDictionary<int, List<RoiContourData>> contoursByRoi)
    {
        var rois = new ObservableCollection<RoiDescriptor>();
        var paletteIndex = 0;
        foreach (var pair in roiNames.OrderBy(pair => pair.Key))
        {
            contoursByRoi.TryGetValue(pair.Key, out var contourList);
            var role = DetectRole(pair.Value);
            rois.Add(new RoiDescriptor
            {
                Id = pair.Key,
                Name = pair.Value,
                Role = role,
                HasContours = contourList is { Count: > 0 },
                Contours = contourList ?? new List<RoiContourData>(),
                Visibility = contourList is { Count: > 0 },
                IsSelected = contourList is { Count: > 0 },
                Opacity = role == RoiRole.External ? 0.22 : 1.0,
                Color = Palette[paletteIndex++ % Palette.Length]
            });
        }

        return rois;
    }

    private static ObservableCollection<RoiDescriptor> BuildRoiCollectionFromNifti(
        vtkImageData image,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var dimensions = image.GetDimensions();
        var origin = image.GetOrigin();
        var spacing = image.GetSpacing();
        var labelPlanes = new Dictionary<int, Dictionary<int, bool[]>>();
        var voxelCounts = new Dictionary<int, int>();

        progress?.Report(new PipelineProgress
        {
            Message = "Copying NIfTI voxels",
            Detail = $"{dimensions[0]}x{dimensions[1]}x{dimensions[2]}"
        });

        var scalarBytes = CopyScalarBytesToManaged(image, dimensions, cancellationToken);

        progress?.Report(new PipelineProgress
        {
            Message = "Scanning labels in parallel",
            Detail = $"{scalarBytes.Length / Math.Max(1, dimensions[2]):N0} bytes per slice"
        });

        var sliceResults = ScanNiftiSlicesParallel(
            scalarBytes,
            image.GetScalarType(),
            dimensions[0],
            dimensions[1],
            dimensions[2],
            cancellationToken);

        for (var z = 0; z < sliceResults.Length; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sliceResult = sliceResults[z];
            if (sliceResult is null)
            {
                continue;
            }

            foreach (var pair in sliceResult.PlaneMasks)
            {
                if (!labelPlanes.TryGetValue(pair.Key, out var planeMap))
                {
                    planeMap = new Dictionary<int, bool[]>();
                    labelPlanes[pair.Key] = planeMap;
                }

                planeMap[z] = pair.Value;
            }

            foreach (var pair in sliceResult.VoxelCounts)
            {
                voxelCounts[pair.Key] = voxelCounts.TryGetValue(pair.Key, out var count)
                    ? count + pair.Value
                    : pair.Value;
            }
        }

        progress?.Report(new PipelineProgress
        {
            Message = "Building ROI summaries",
            Detail = $"{labelPlanes.Count} labels"
        });

        var orderedLabels = labelPlanes.Keys.OrderBy(value => value).ToArray();
        var roiBuffer = new RoiDescriptor[orderedLabels.Length];
        Parallel.For(
            0,
            orderedLabels.Length,
            new ParallelOptions { CancellationToken = cancellationToken },
            index =>
            {
                var label = orderedLabels[index];
                var contours = new List<RoiContourData>();
                foreach (var plane in labelPlanes[label].OrderBy(pair => pair.Key))
                {
                    var points = BuildBoundingContourFromMask(
                        plane.Value,
                        dimensions[0],
                        dimensions[1],
                        origin[0],
                        origin[1],
                        origin[2] + plane.Key * spacing[2],
                        spacing[0],
                        spacing[1]);

                    if (points.Count > 0)
                    {
                        contours.Add(new RoiContourData { Points = points, Z = points.Average(point => point.Z) });
                    }
                }

                roiBuffer[index] = new RoiDescriptor
                {
                    Id = label,
                    Name = $"Label {label}",
                    Role = RoiRole.Tissue,
                    HasContours = voxelCounts.TryGetValue(label, out var count) && count > 0,
                    Contours = contours,
                    Visibility = true,
                    IsSelected = true,
                    Opacity = 1.0,
                    Color = Palette[index % Palette.Length]
                };
            });

        return new ObservableCollection<RoiDescriptor>(roiBuffer);
    }

    private static ObservableCollection<RoiDescriptor> BuildRoiCollectionFromImportedNifti(
        IReadOnlyList<NiftiImportedLabel> importedLabels,
        double[] origin,
        double[] spacing,
        int width,
        int height)
    {
        var rois = new ObservableCollection<RoiDescriptor>();
        for (var index = 0; index < importedLabels.Count; index++)
        {
            var importedLabel = importedLabels[index];
            var contours = new List<RoiContourData>();
            foreach (var plane in importedLabel.PlaneMasks.OrderBy(pair => pair.Key))
            {
                var points = BuildBoundingContourFromMask(
                    plane.Value,
                    width,
                    height,
                    origin[0],
                    origin[1],
                    origin[2] + plane.Key * spacing[2],
                    spacing[0],
                    spacing[1]);

                if (points.Count > 0)
                {
                    contours.Add(new RoiContourData { Points = points, Z = points.Average(point => point.Z) });
                }
            }

            rois.Add(new RoiDescriptor
            {
                Id = importedLabel.GlobalLabel,
                Name = importedLabel.Name,
                Role = importedLabel.Role,
                HasContours = importedLabel.VoxelCount > 0,
                Contours = contours,
                Visibility = true,
                IsSelected = true,
                Opacity = importedLabel.Role == RoiRole.External ? 0.22 : 1.0,
                Color = Palette[index % Palette.Length]
            });
        }

        return rois;
    }

    private static IReadOnlyList<string> ResolveNiftiPaths(StudyImportRequest request)
    {
        if (request.NiftiPaths.Count > 0)
        {
            return request.NiftiPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return IsNiftiPath(request.CtSeriesPath)
            ? new[] { request.CtSeriesPath }
            : Array.Empty<string>();
    }

    private static vtkImageData ReadNiftiImage(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("NIfTI file not found.", path);
        }

        var reader = vtkNIFTIImageReader.New();
        reader.SetFileName(path);
        reader.Update();

        var image = vtkImageData.New();
        image.DeepCopy(reader.GetOutput());
        return image;
    }

    private static void ValidateNiftiGeometry(vtkImageData referenceImage, vtkImageData candidateImage, string path)
    {
        var referenceDimensions = referenceImage.GetDimensions();
        var candidateDimensions = candidateImage.GetDimensions();
        if (referenceDimensions[0] != candidateDimensions[0] ||
            referenceDimensions[1] != candidateDimensions[1] ||
            referenceDimensions[2] != candidateDimensions[2])
        {
            throw new InvalidOperationException($"NIfTI dimensions do not match for {Path.GetFileName(path)}.");
        }

        var referenceOrigin = referenceImage.GetOrigin();
        var candidateOrigin = candidateImage.GetOrigin();
        var referenceSpacing = referenceImage.GetSpacing();
        var candidateSpacing = candidateImage.GetSpacing();
        for (var index = 0; index < 3; index++)
        {
            if (!NearlyEqual(referenceOrigin[index], candidateOrigin[index]) ||
                !NearlyEqual(referenceSpacing[index], candidateSpacing[index]))
            {
                throw new InvalidOperationException($"NIfTI geometry does not match for {Path.GetFileName(path)}.");
            }
        }
    }

    private static string GetNiftiSourceName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".nii.gz", StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^7];
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static byte[] CopyScalarBytesToManaged(vtkImageData image, int[] dimensions, CancellationToken cancellationToken)
    {
        if (image.GetNumberOfScalarComponents() != 1)
        {
            throw new InvalidOperationException("Only single-component NIfTI label maps are supported.");
        }

        var voxelCount = checked(dimensions[0] * dimensions[1] * dimensions[2]);
        var scalarSize = image.GetScalarSize();
        var byteCount = checked(voxelCount * scalarSize);
        var scalarPointer = image.GetScalarPointer();
        if (scalarPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("The NIfTI image does not expose scalar voxel data.");
        }

        var scalarBytes = new byte[byteCount];
        Marshal.Copy(scalarPointer, scalarBytes, 0, byteCount);
        cancellationToken.ThrowIfCancellationRequested();
        return scalarBytes;
    }

    private static NiftiSliceScanResult?[] ScanNiftiSlicesParallel(
        byte[] scalarBytes,
        int scalarType,
        int width,
        int height,
        int depth,
        CancellationToken cancellationToken)
    {
        var results = new NiftiSliceScanResult?[depth];
        var options = new ParallelOptions { CancellationToken = cancellationToken };

        switch (scalarType)
        {
            case VtkUnsignedChar:
                Parallel.For(0, depth, options, z => results[z] = ScanUnsignedByteSlice(scalarBytes, z, width, height));
                break;
            case VtkChar:
            case VtkSignedChar:
                Parallel.For(0, depth, options, z => results[z] = ScanSignedByteSlice(scalarBytes, z, width, height));
                break;
            case VtkShort:
                Parallel.For(0, depth, options, z => results[z] = ScanInt16Slice(scalarBytes, z, width, height));
                break;
            case VtkUnsignedShort:
                Parallel.For(0, depth, options, z => results[z] = ScanUInt16Slice(scalarBytes, z, width, height));
                break;
            case VtkInt:
                Parallel.For(0, depth, options, z => results[z] = ScanInt32Slice(scalarBytes, z, width, height));
                break;
            case VtkUnsignedInt:
                Parallel.For(0, depth, options, z => results[z] = ScanUInt32Slice(scalarBytes, z, width, height));
                break;
            case VtkLong:
            case VtkLongLong:
                Parallel.For(0, depth, options, z => results[z] = ScanInt64Slice(scalarBytes, z, width, height));
                break;
            case VtkUnsignedLong:
            case VtkUnsignedLongLong:
                Parallel.For(0, depth, options, z => results[z] = ScanUInt64Slice(scalarBytes, z, width, height));
                break;
            case VtkFloat:
                Parallel.For(0, depth, options, z => results[z] = ScanSingleSlice(scalarBytes, z, width, height));
                break;
            case VtkDouble:
                Parallel.For(0, depth, options, z => results[z] = ScanDoubleSlice(scalarBytes, z, width, height));
                break;
            case VtkBool:
                Parallel.For(0, depth, options, z => results[z] = ScanBoolSlice(scalarBytes, z, width, height));
                break;
            default:
                throw new NotSupportedException($"Unsupported NIfTI scalar type: {scalarType} ({imageScalarTypeName(scalarType)}).");
        }

        return results;
    }

    private static NiftiSliceScanResult? ScanUnsignedByteSlice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize;
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++)
        {
            var label = scalarBytes[offset + index];
            if (label <= 0)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, label, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanSignedByteSlice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize;
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++)
        {
            var label = unchecked((sbyte)scalarBytes[offset + index]);
            if (label <= 0)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, label, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanInt16Slice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize * sizeof(short);
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++, offset += sizeof(short))
        {
            var label = BitConverter.ToInt16(scalarBytes, offset);
            if (label <= 0)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, label, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanUInt16Slice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize * sizeof(ushort);
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++, offset += sizeof(ushort))
        {
            var label = BitConverter.ToUInt16(scalarBytes, offset);
            if (label <= 0)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, label, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanInt32Slice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize * sizeof(int);
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++, offset += sizeof(int))
        {
            var label = BitConverter.ToInt32(scalarBytes, offset);
            if (label <= 0)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, label, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanUInt32Slice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize * sizeof(uint);
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++, offset += sizeof(uint))
        {
            var label = checked((int)BitConverter.ToUInt32(scalarBytes, offset));
            if (label <= 0)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, label, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanInt64Slice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize * sizeof(long);
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++, offset += sizeof(long))
        {
            var value = BitConverter.ToInt64(scalarBytes, offset);
            if (value <= 0 || value > int.MaxValue)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, (int)value, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanUInt64Slice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize * sizeof(ulong);
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++, offset += sizeof(ulong))
        {
            var value = BitConverter.ToUInt64(scalarBytes, offset);
            if (value == 0 || value > int.MaxValue)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, (int)value, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanSingleSlice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize * sizeof(float);
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++, offset += sizeof(float))
        {
            var value = BitConverter.ToSingle(scalarBytes, offset);
            var label = (int)Math.Round(value);
            if (label <= 0 || Math.Abs(value - label) > 0.01)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, label, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanDoubleSlice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize * sizeof(double);
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++, offset += sizeof(double))
        {
            var value = BitConverter.ToDouble(scalarBytes, offset);
            var label = (int)Math.Round(value);
            if (label <= 0 || Math.Abs(value - label) > 0.01)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, label, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static NiftiSliceScanResult? ScanBoolSlice(byte[] scalarBytes, int z, int width, int height)
    {
        var sliceSize = width * height;
        var offset = z * sliceSize;
        Dictionary<int, bool[]>? masks = null;
        Dictionary<int, int>? counts = null;

        for (var index = 0; index < sliceSize; index++)
        {
            if (scalarBytes[offset + index] == 0)
            {
                continue;
            }

            masks ??= new Dictionary<int, bool[]>();
            counts ??= new Dictionary<int, int>();
            SetSliceVoxel(masks, counts, 1, index, sliceSize);
        }

        return masks is null || counts is null ? null : new NiftiSliceScanResult(masks, counts);
    }

    private static void SetSliceVoxel(
        Dictionary<int, bool[]> masks,
        Dictionary<int, int> counts,
        int label,
        int voxelIndex,
        int sliceSize)
    {
        if (!masks.TryGetValue(label, out var mask))
        {
            mask = new bool[sliceSize];
            masks[label] = mask;
        }

        mask[voxelIndex] = true;
        counts[label] = counts.TryGetValue(label, out var count) ? count + 1 : 1;
    }

    private static string imageScalarTypeName(int scalarType) => scalarType switch
    {
        VtkChar => "char",
        VtkUnsignedChar => "unsigned char",
        VtkShort => "short",
        VtkUnsignedShort => "unsigned short",
        VtkInt => "int",
        VtkUnsignedInt => "unsigned int",
        VtkLong => "long",
        VtkUnsignedLong => "unsigned long",
        VtkFloat => "float",
        VtkDouble => "double",
        VtkSignedChar => "signed char",
        VtkLongLong => "long long",
        VtkUnsignedLongLong => "unsigned long long",
        VtkBool => "bool",
        _ => "unknown"
    };

    private static List<Point3D> BuildBoundingContourFromMask(
        bool[] mask,
        int width,
        int height,
        double originX,
        double originY,
        double z,
        double spacingX,
        double spacingY)
    {
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!mask[(y * width) + x])
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return new List<Point3D>();
        }

        var x0 = originX + minX * spacingX;
        var x1 = originX + (maxX + 1) * spacingX;
        var y0 = originY + minY * spacingY;
        var y1 = originY + (maxY + 1) * spacingY;

        return new List<Point3D>
        {
            new(x0, y0, z),
            new(x1, y0, z),
            new(x1, y1, z),
            new(x0, y1, z)
        };
    }

    private static IReadOnlyList<CtSliceInfo> BuildPseudoSlices(string path, double[] origin, double[] spacing, int[] dimensions)
    {
        var slices = new List<CtSliceInfo>(dimensions[2]);
        for (var z = 0; z < dimensions[2]; z++)
        {
            slices.Add(new CtSliceInfo
            {
                Path = path,
                ImagePosition = new Point3D(origin[0], origin[1], origin[2] + z * spacing[2]),
                Normal = new Vector3D(0, 0, 1),
                Projection = origin[2] + z * spacing[2]
            });
        }

        return slices;
    }

    private static vtkImageData CreateImageData(
        ushort[] labels,
        int width,
        int height,
        int depth,
        double spacingX,
        double spacingY,
        double spacingZ,
        double originX,
        double originY,
        double originZ)
    {
        var importer = vtkImageImport.New();
        var handle = GCHandle.Alloc(labels, GCHandleType.Pinned);
        try
        {
            importer.CopyImportVoidPointer(handle.AddrOfPinnedObject(), labels.Length * sizeof(ushort));
            importer.SetDataScalarTypeToUnsignedShort();
            importer.SetNumberOfScalarComponents(1);
            importer.SetDataExtent(0, width - 1, 0, height - 1, 0, depth - 1);
            importer.SetWholeExtent(0, width - 1, 0, height - 1, 0, depth - 1);
            importer.SetDataSpacing(spacingX, spacingY, spacingZ);
            importer.SetDataOrigin(originX, originY, originZ);
            importer.Update();

            var imageData = vtkImageData.New();
            imageData.DeepCopy(importer.GetOutput());
            return imageData;
        }
        finally
        {
            handle.Free();
        }
    }

    private static void RasterizeContour(
        bool[] mask,
        int width,
        int height,
        double spacing,
        double originX,
        double originY,
        IReadOnlyList<Point3D> points)
    {
        if (points.Count < 3)
        {
            return;
        }

        var polygon = points
            .Select(point => new RasterPoint((point.X - originX) / spacing, (point.Y - originY) / spacing))
            .ToArray();

        var minY = Math.Max(0, (int)Math.Floor(polygon.Min(point => point.Y)));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(polygon.Max(point => point.Y)));

        for (var y = minY; y <= maxY; y++)
        {
            var scanY = y + 0.5;
            var intersections = new List<double>();
            for (var index = 0; index < polygon.Length; index++)
            {
                var current = polygon[index];
                var next = polygon[(index + 1) % polygon.Length];

                var condition = current.Y <= scanY && next.Y > scanY || next.Y <= scanY && current.Y > scanY;
                if (!condition)
                {
                    continue;
                }

                var ratio = (scanY - current.Y) / (next.Y - current.Y);
                intersections.Add(current.X + ratio * (next.X - current.X));
            }

            intersections.Sort();
            for (var index = 0; index + 1 < intersections.Count; index += 2)
            {
                var startX = Math.Max(0, (int)Math.Floor(intersections[index]));
                var endX = Math.Min(width - 1, (int)Math.Ceiling(intersections[index + 1]));
                for (var x = startX; x <= endX; x++)
                {
                    mask[(y * width) + x] = true;
                }
            }
        }
    }

    private static void ApplyPlaneMask(ushort[] labels, int width, int height, int depth, int zIndex, bool[] mask, ushort label)
    {
        if (zIndex < 0 || zIndex >= depth)
        {
            return;
        }

        var planeOffset = zIndex * width * height;
        for (var index = 0; index < mask.Length; index++)
        {
            if (mask[index])
            {
                labels[planeOffset + index] = label;
            }
        }
    }

    private static bool[] MergeMasks(bool[] first, bool[] second)
    {
        var merged = new bool[first.Length];
        for (var index = 0; index < first.Length; index++)
        {
            merged[index] = first[index] || second[index];
        }

        return merged;
    }

    private static vtkTransform CreatePlacementTransform(Point3D surfacePoint, Vector3D normal, double height)
    {
        var normalized = normal;
        if (normalized.LengthSquared < 1e-8)
        {
            normalized = new Vector3D(0, 0, 1);
        }

        normalized.Normalize();
        var sourceAxis = new Vector3D(0, 1, 0);
        var rotationAxis = Vector3D.CrossProduct(sourceAxis, normalized);
        var dot = Clamp(Vector3D.DotProduct(sourceAxis, normalized), -1.0, 1.0);
        var angle = Math.Acos(dot) * 180.0 / Math.PI;

        var transform = vtkTransform.New();
        transform.PostMultiply();
        if (rotationAxis.LengthSquared > 1e-8)
        {
            rotationAxis.Normalize();
            transform.RotateWXYZ(angle, rotationAxis.X, rotationAxis.Y, rotationAxis.Z);
        }
        else if (dot < 0)
        {
            transform.RotateWXYZ(180.0, 1.0, 0.0, 0.0);
        }

        var center = surfacePoint + normalized * (height / 2.0);
        transform.Translate(center.X, center.Y, center.Z);
        return transform;
    }

    private static RoiRole DetectRole(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("external") || normalized.Contains("body") || normalized.Contains("skin") || normalized.Contains("scalp"))
        {
            return RoiRole.External;
        }

        return RoiRole.Tissue;
    }

    private static bool IsNiftiPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.EndsWith(".nii", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".nii.gz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool NearlyEqual(double first, double second, double tolerance = 1e-3)
        => Math.Abs(first - second) <= tolerance;

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    private sealed class NiftiImportedLabel
    {
        public NiftiImportedLabel(ushort globalLabel, string name, RoiRole role)
        {
            GlobalLabel = globalLabel;
            Name = name;
            Role = role;
        }

        public ushort GlobalLabel { get; }

        public string Name { get; }

        public RoiRole Role { get; }

        public Dictionary<int, bool[]> PlaneMasks { get; } = new();

        public int VoxelCount { get; set; }
    }

    private sealed record NiftiSliceScanResult(Dictionary<int, bool[]> PlaneMasks, Dictionary<int, int> VoxelCounts);

    private readonly record struct RasterPoint(double X, double Y);
}
