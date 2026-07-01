using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Kitware.VTK;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Integration.Mcp.Dtos;
using MeshGenLite.Models;
using MeshGenLite.Services;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace MeshGenLite.Integration.Mcp.Bridge;

/// <summary>
/// In-process bridge that calls the existing MeshGen services directly.
/// Used both for headless --mcp mode and (eventually) for the WPF view-model
/// once we route UI commands through this same surface.
///
/// All public methods are exception-safe: any thrown exception is converted
/// into a <see cref="CommonToolResponse{T}.Fail"/>, so MCP tools never need
/// their own try/catch boilerplate.
/// </summary>
public sealed class InProcessMeshGenCommandBridge : IMeshGenCommandBridge
{
    private readonly IPipelineService pipeline;
    private readonly SceneStateService sceneState;
    private readonly ElectrodeFactory electrodeFactory;
    private readonly ElectrodeTransformBuilder transformBuilder;
    private readonly IElasticDeformationService elasticDeformation;
    private readonly IVolumeMeshService volumeMeshService;
    private readonly IMeshExportService meshExport;
    private readonly IFemExportService femExport;

    public InProcessMeshGenCommandBridge(
        IPipelineService pipeline,
        SceneStateService sceneState,
        ElectrodeFactory electrodeFactory,
        ElectrodeTransformBuilder transformBuilder,
        IElasticDeformationService elasticDeformation,
        IVolumeMeshService volumeMeshService,
        IMeshExportService meshExport,
        IFemExportService femExport)
    {
        this.pipeline = pipeline;
        this.sceneState = sceneState;
        this.electrodeFactory = electrodeFactory;
        this.transformBuilder = transformBuilder;
        this.elasticDeformation = elasticDeformation;
        this.volumeMeshService = volumeMeshService;
        this.meshExport = meshExport;
        this.femExport = femExport;
    }

    public async Task<CommonToolResponse<ImportDicomStudyResult>> ImportDicomStudyAsync(
        ImportDicomStudyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DicomDirectory))
        {
            return CommonToolResponse<ImportDicomStudyResult>.Fail("DicomDirectory is required.");
        }

        if (!Directory.Exists(request.DicomDirectory))
        {
            return CommonToolResponse<ImportDicomStudyResult>.Fail($"Directory not found: {request.DicomDirectory}");
        }

        if (!string.IsNullOrEmpty(request.RtssPath) && !File.Exists(request.RtssPath))
        {
            return CommonToolResponse<ImportDicomStudyResult>.Fail($"RTSS file not found: {request.RtssPath}");
        }

        try
        {
            var importRequest = new StudyImportRequest
            {
                CtSeriesPath = request.DicomDirectory,
                RtssPath = request.RtssPath ?? string.Empty,
                TargetSpacing = request.TargetSpacingMm
            };

            var study = await pipeline.ImportAsync(importRequest, null, cancellationToken);
            sceneState.ResetStudy(study);
            return CommonToolResponse<ImportDicomStudyResult>.Ok(
                $"DICOM study imported. {study.SliceCount} slices, {study.Rois.Count} ROIs.",
                BuildImportResult(study));
        }
        catch (Exception ex)
        {
            return CommonToolResponse<ImportDicomStudyResult>.Fail($"Import failed: {ex.Message}");
        }
    }

    public async Task<CommonToolResponse<ImportDicomStudyResult>> ImportNiftiSegmentationAsync(
        ImportNiftiSegmentationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Paths is null || request.Paths.Count == 0)
        {
            return CommonToolResponse<ImportDicomStudyResult>.Fail("At least one NIfTI path is required.");
        }

        foreach (var path in request.Paths)
        {
            if (!File.Exists(path))
            {
                return CommonToolResponse<ImportDicomStudyResult>.Fail($"NIfTI file not found: {path}");
            }
        }

        try
        {
            var importRequest = new StudyImportRequest
            {
                CtSeriesPath = request.Paths[0],
                NiftiPaths = request.Paths.ToArray(),
                RtssPath = string.Empty,
                TargetSpacing = request.TargetSpacingMm
            };

            var study = await pipeline.ImportAsync(importRequest, null, cancellationToken);
            sceneState.ResetStudy(study);
            return CommonToolResponse<ImportDicomStudyResult>.Ok(
                $"NIfTI segmentation imported. {request.Paths.Count} file(s), {study.Rois.Count} labels.",
                BuildImportResult(study));
        }
        catch (Exception ex)
        {
            return CommonToolResponse<ImportDicomStudyResult>.Fail($"Import failed: {ex.Message}");
        }
    }

    public Task<CommonToolResponse<ListRoisResult>> ListRoisAsync(CancellationToken cancellationToken)
    {
        var study = sceneState.Study;
        if (study is null)
        {
            return Task.FromResult(CommonToolResponse<ListRoisResult>.Fail(
                "No study loaded. Call import_dicom_study or import_nifti_segmentation first."));
        }

        var rois = study.Rois.Select(roi => new RoiSummaryDto
        {
            Id = roi.Id,
            Name = roi.Name,
            Role = roi.Role.ToString(),
            HasContours = roi.HasContours,
            IsSelected = roi.IsSelected,
            Visible = roi.Visibility
        }).ToList();

        return Task.FromResult(CommonToolResponse<ListRoisResult>.Ok(
            $"{rois.Count} ROIs found.",
            new ListRoisResult { Rois = rois, ExternalRoiId = study.ExternalRoiId }));
    }

    public Task<CommonToolResponse<SetExternalRoiResult>> SetExternalRoiAsync(
        SetExternalRoiRequest request,
        CancellationToken cancellationToken)
    {
        var study = sceneState.Study;
        if (study is null)
        {
            return Task.FromResult(CommonToolResponse<SetExternalRoiResult>.Fail("No study loaded."));
        }

        var match = study.Rois.FirstOrDefault(roi => roi.Id == request.RoiId);
        if (match is null)
        {
            return Task.FromResult(CommonToolResponse<SetExternalRoiResult>.Fail(
                $"ROI {request.RoiId} not found in current study."));
        }

        if (!match.HasContours)
        {
            return Task.FromResult(CommonToolResponse<SetExternalRoiResult>.Fail(
                $"ROI {request.RoiId} ({match.Name}) has no contours; cannot be external."));
        }

        foreach (var roi in study.Rois)
        {
            roi.Role = roi.Id == request.RoiId ? RoiRole.External : RoiRole.Tissue;
        }
        study.ExternalRoiId = request.RoiId;

        return Task.FromResult(CommonToolResponse<SetExternalRoiResult>.Ok(
            $"External ROI set to {match.Name}.",
            new SetExternalRoiResult { ExternalRoiId = match.Id, ExternalRoiName = match.Name }));
    }

    public async Task<CommonToolResponse<BuildLabelMapResult>> BuildLabelMapAsync(
        BuildLabelMapRequest request,
        CancellationToken cancellationToken)
    {
        var study = sceneState.Study;
        if (study is null)
        {
            return CommonToolResponse<BuildLabelMapResult>.Fail("No study loaded.");
        }

        var selected = request.RoiIds is { Count: > 0 }
            ? study.Rois.Where(roi => request.RoiIds.Contains(roi.Id)).ToList()
            : study.Rois.Where(roi => roi.HasContours).ToList();

        if (selected.Count == 0)
        {
            return CommonToolResponse<BuildLabelMapResult>.Fail("No ROIs with contours selected.");
        }

        // Make sure the requested ROIs are flagged selected so the pipeline picks them up.
        foreach (var roi in selected)
        {
            roi.IsSelected = true;
        }

        try
        {
            var labelMap = await pipeline.BuildLabelMapAsync(study, selected, request.TargetSpacingMm, null, cancellationToken);
            lock (sceneState.Lock)
            {
                sceneState.Artifacts.LabelMap = labelMap;
            }

            var dimensions = labelMap.GetDimensions();
            var spacing = labelMap.GetSpacing();
            return CommonToolResponse<BuildLabelMapResult>.Ok(
                $"Label map built ({dimensions[0]}x{dimensions[1]}x{dimensions[2]}) for {selected.Count} ROIs.",
                new BuildLabelMapResult
                {
                    Dimensions = new[] { dimensions[0], dimensions[1], dimensions[2] },
                    Spacing = new[] { spacing[0], spacing[1], spacing[2] }
                });
        }
        catch (Exception ex)
        {
            return CommonToolResponse<BuildLabelMapResult>.Fail($"Label map build failed: {ex.Message}");
        }
    }

    public async Task<CommonToolResponse<BuildSurfaceMeshResult>> BuildSurfaceMeshAsync(
        BuildSurfaceMeshRequest request,
        CancellationToken cancellationToken)
    {
        var study = sceneState.Study;
        if (study is null)
        {
            return CommonToolResponse<BuildSurfaceMeshResult>.Fail("No study loaded.");
        }

        if (sceneState.Artifacts.LabelMap is null)
        {
            return CommonToolResponse<BuildSurfaceMeshResult>.Fail("No label map. Call build_label_map first.");
        }

        if (request.RoiIds is { Count: > 0 })
        {
            foreach (var roi in study.Rois)
            {
                roi.IsSelected = request.RoiIds.Contains(roi.Id);
            }
        }

        try
        {
            var meshes = await pipeline.BuildSurfaceAsync(
                study,
                sceneState.Artifacts.LabelMap,
                study.Rois.ToList(),
                Math.Max(request.ExternalSmoothingIterations, 0),
                null,
                cancellationToken);

            lock (sceneState.Lock)
            {
                sceneState.Artifacts.SurfaceMeshes.Clear();
                sceneState.Artifacts.SurfaceMeshes.AddRange(meshes);
                sceneState.Artifacts.ExternalSurface = meshes
                    .FirstOrDefault(mesh => mesh.Roi.Id == study.ExternalRoiId)?.PolyData;
            }

            var summaries = meshes.Select(mesh => new SurfaceMeshSummaryDto
            {
                RoiId = mesh.Roi.Id,
                RoiName = mesh.Roi.Name,
                VertexCount = mesh.VertexCount,
                TriangleCount = mesh.TriangleCount
            }).ToList();

            return CommonToolResponse<BuildSurfaceMeshResult>.Ok(
                $"Surface meshes built for {summaries.Count} ROIs.",
                new BuildSurfaceMeshResult { Meshes = summaries });
        }
        catch (Exception ex)
        {
            return CommonToolResponse<BuildSurfaceMeshResult>.Fail($"Surface build failed: {ex.Message}");
        }
    }

    public Task<CommonToolResponse<PlaceElectrodeAtPoseResult>> PlaceElectrodeAtPoseAsync(
        PlaceElectrodeAtPoseRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Position is null || request.Position.Length != 3)
        {
            return Task.FromResult(CommonToolResponse<PlaceElectrodeAtPoseResult>.Fail(
                "Position must be a 3-element [x, y, z] array."));
        }

        if (request.Normal is null || request.Normal.Length != 3)
        {
            return Task.FromResult(CommonToolResponse<PlaceElectrodeAtPoseResult>.Fail(
                "Normal must be a 3-element [nx, ny, nz] array."));
        }

        try
        {
            var spec = ResolveSpec(request.Shape, request.Layers);
            var position = new Point3D(request.Position[0], request.Position[1], request.Position[2]);
            var normal = new Vector3D(request.Normal[0], request.Normal[1], request.Normal[2]);
            if (normal.LengthSquared < 1e-12)
            {
                return Task.FromResult(CommonToolResponse<PlaceElectrodeAtPoseResult>.Fail(
                    "Normal vector cannot be zero."));
            }
            normal.Normalize();

            var clearance = Math.Max(0.0, request.Clearance);
            var worldPosition = position + (normal * clearance);
            var hit = new MeshHitResult
            {
                Success = true,
                Position = position,
                Normal = normal,
                CellId = -1
            };

            ElectrodePlacement placement;
            vtkPolyData worldMesh;
            lock (sceneState.Lock)
            {
                placement = sceneState.Electrodes.CreateAndAdd(
                    hit,
                    sceneState.Study?.ExternalRoiId,
                    spec,
                    clearance,
                    worldPosition);

                var localMesh = electrodeFactory.Build(spec, dense: false);
                var transform = transformBuilder.BuildTransform(position, normal, clearance);
                worldMesh = ApplyTransform(localMesh, transform);
                FemMetadataService.TagElectrodeSurface(worldMesh, placement.Id, spec, worldPosition, normal);
                sceneState.ElectrodeWorldMeshes[placement.Id] = worldMesh;
                sceneState.InvalidateAttachment();
            }

            return Task.FromResult(CommonToolResponse<PlaceElectrodeAtPoseResult>.Ok(
                $"Electrode #{placement.Id} placed at ({worldPosition.X:F2}, {worldPosition.Y:F2}, {worldPosition.Z:F2}).",
                new PlaceElectrodeAtPoseResult
                {
                    ElectrodeId = placement.Id,
                    WorldPosition = new[] { worldPosition.X, worldPosition.Y, worldPosition.Z },
                    WorldNormal = new[] { normal.X, normal.Y, normal.Z },
                    TotalThicknessMm = spec.TotalThickness,
                    FootprintRadiusMm = spec.FootprintRadius,
                    LayerCount = spec.Layers.Count
                }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CommonToolResponse<PlaceElectrodeAtPoseResult>.Fail(
                $"Placement failed: {ex.Message}"));
        }
    }

    public Task<CommonToolResponse<ListElectrodesResult>> ListElectrodesAsync(CancellationToken cancellationToken)
    {
        var summaries = sceneState.Electrodes.Placements.Select(p => new ElectrodeSummaryDto
        {
            Id = p.Id,
            WorldPosition = new[] { p.WorldPosition.X, p.WorldPosition.Y, p.WorldPosition.Z },
            WorldNormal = new[] { p.WorldNormal.X, p.WorldNormal.Y, p.WorldNormal.Z },
            LengthMm = p.Length,
            RadiusMm = p.Radius,
            ClearanceMm = p.Clearance
        }).ToList();

        return Task.FromResult(CommonToolResponse<ListElectrodesResult>.Ok(
            $"{summaries.Count} electrode(s) placed.",
            new ListElectrodesResult { Electrodes = summaries }));
    }

    public Task<CommonToolResponse<ClearElectrodesResult>> ClearElectrodesAsync(CancellationToken cancellationToken)
    {
        int count;
        lock (sceneState.Lock)
        {
            count = sceneState.Electrodes.Placements.Count;
            sceneState.Electrodes.Clear();
            sceneState.ElectrodeWorldMeshes.Clear();
            sceneState.InvalidateAttachment();
        }

        return Task.FromResult(CommonToolResponse<ClearElectrodesResult>.Ok(
            $"Removed {count} electrode(s).",
            new ClearElectrodesResult { RemovedCount = count }));
    }

    public Task<CommonToolResponse<SetDefaultElectrodeSpecResult>> SetDefaultElectrodeSpecAsync(
        SetDefaultElectrodeSpecRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Layers is null || request.Layers.Count == 0)
        {
            return Task.FromResult(CommonToolResponse<SetDefaultElectrodeSpecResult>.Fail(
                "At least one layer is required."));
        }

        try
        {
            var spec = sceneState.DefaultElectrodeSpec;
            spec.Shape = ParseShape(request.Shape);
            spec.Layers.Clear();
            foreach (var layerDto in request.Layers)
            {
                spec.Layers.Add(MapLayer(layerDto));
            }

            return Task.FromResult(CommonToolResponse<SetDefaultElectrodeSpecResult>.Ok(
                $"Default electrode spec updated ({spec.Shape}, {spec.Layers.Count} layers).",
                new SetDefaultElectrodeSpecResult
                {
                    Shape = spec.Shape.ToString(),
                    LayerCount = spec.Layers.Count,
                    TotalThicknessMm = spec.TotalThickness
                }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(CommonToolResponse<SetDefaultElectrodeSpecResult>.Fail(
                $"Spec update failed: {ex.Message}"));
        }
    }

    public async Task<CommonToolResponse<AttachElectrodesBatchResult>> AttachElectrodesBatchAsync(
        AttachElectrodesBatchRequest request,
        CancellationToken cancellationToken)
    {
        var external = sceneState.Artifacts.ExternalSurface;
        if (external is null)
        {
            return CommonToolResponse<AttachElectrodesBatchResult>.Fail(
                "External surface not built. Run build_surface_mesh and ensure an External ROI is set.");
        }

        var placements = sceneState.Electrodes.Placements.ToList();
        if (placements.Count == 0)
        {
            return CommonToolResponse<AttachElectrodesBatchResult>.Fail(
                "No electrode placements. Call place_electrode_at_pose first.");
        }

        try
        {
            var config = BuildEngineConfig(request.Parameters);
            var result = await elasticDeformation.AttachAsync(
                external,
                sceneState.DefaultElectrodeSpec,
                placements,
                config,
                null,
                cancellationToken);

            lock (sceneState.Lock)
            {
                sceneState.Artifacts.AttachmentResult = result;
                sceneState.Artifacts.OriginalBodySurfaceBackup = result.OriginalBodyMesh is null
                    ? null
                    : MeshConverter.ToVtkPolyData(result.OriginalBodyMesh);
                sceneState.Artifacts.DeformedBodySurface = result.FinalBodyMesh is null
                    ? null
                    : MeshConverter.ToVtkPolyData(result.FinalBodyMesh);
                sceneState.VolumeMesh = null; // attachment changed body, invalidate volume mesh
            }

            var quality = result.AggregateQualityReport;
            return CommonToolResponse<AttachElectrodesBatchResult>.Ok(
                $"{result.PerElectrodeSessions.Count} electrode(s) attached.",
                new AttachElectrodesBatchResult
                {
                    AttachedCount = result.PerElectrodeSessions.Count,
                    QualitySummary = quality?.Summary ?? string.Empty,
                    MaxDisplacementMm = quality?.MaxDisplacement ?? 0.0,
                    MeanDisplacementMm = quality?.MeanDisplacement ?? 0.0,
                    FlippedTriangleCount = quality?.FlippedTriangles ?? 0
                });
        }
        catch (Exception ex)
        {
            return CommonToolResponse<AttachElectrodesBatchResult>.Fail($"Attachment failed: {ex.Message}");
        }
    }

    public async Task<CommonToolResponse<GenerateVolumeMeshResult>> GenerateVolumeMeshAsync(
        GenerateVolumeMeshRequest request,
        CancellationToken cancellationToken)
    {
        var combined = ResolveExportSurface();
        if (combined is null)
        {
            return CommonToolResponse<GenerateVolumeMeshResult>.Fail(
                "No surface available. Build a surface mesh and/or place electrodes first.");
        }

        try
        {
            var result = await volumeMeshService.GenerateAsync(
                combined,
                Math.Max(request.AlphaMm, 0.0),
                Math.Max(request.MaxPoints, 0),
                null,
                cancellationToken);

            sceneState.VolumeMesh = result;
            return CommonToolResponse<GenerateVolumeMeshResult>.Ok(
                $"Volume mesh generated: {result.TetrahedronCount:N0} tetrahedra, {result.PointCount:N0} points.",
                new GenerateVolumeMeshResult
                {
                    PointCount = result.PointCount,
                    TetrahedronCount = result.TetrahedronCount,
                    InputPointCount = result.InputPointCount,
                    DecimatedPointCount = result.DecimatedPointCount
                });
        }
        catch (Exception ex)
        {
            return CommonToolResponse<GenerateVolumeMeshResult>.Fail($"Volume mesh failed: {ex.Message}");
        }
    }

    public Task<CommonToolResponse<ExportResult>> ExportSurfaceStlAsync(
        ExportSurfaceRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(ExportSurface(request, isStl: true, cancellationToken));

    public Task<CommonToolResponse<ExportResult>> ExportSurfacePolyAsync(
        ExportSurfaceRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(ExportSurface(request, isStl: false, cancellationToken));

    public async Task<CommonToolResponse<ExportFemPackageResult>> ExportFemPackageAsync(
        ExportFemPackageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return CommonToolResponse<ExportFemPackageResult>.Fail("OutputDirectory is required.");
        }

        if (File.Exists(request.OutputDirectory))
        {
            return CommonToolResponse<ExportFemPackageResult>.Fail(
                $"OutputDirectory points to a file, not a folder: {request.OutputDirectory}");
        }

        var combined = ResolveExportSurface();
        if (combined is null)
        {
            return CommonToolResponse<ExportFemPackageResult>.Fail(
                "No surface available. Build a surface mesh and/or place electrodes first.");
        }

        try
        {
            var volume = sceneState.VolumeMesh ?? await volumeMeshService.GenerateAsync(
                combined,
                Math.Max(request.AlphaMm, 0.0),
                Math.Max(request.MaxPoints, 0),
                null,
                cancellationToken);
            sceneState.VolumeMesh = volume;

            var result = femExport.Export(new FemExportRequest
            {
                VolumeGrid = volume.Grid,
                BodySurface = sceneState.Artifacts.DeformedBodySurface ?? sceneState.Artifacts.ExternalSurface,
                RoiSurfaces = sceneState.Artifacts.SurfaceMeshes.ToList(),
                Electrodes = BuildFemElectrodeExportInfos(),
                ElectrodeSpec = sceneState.DefaultElectrodeSpec,
                OutputDirectory = request.OutputDirectory,
                BaseName = request.BaseName
            });

            return CommonToolResponse<ExportFemPackageResult>.Ok(
                $"FEM package exported: {result.Summary}.",
                new ExportFemPackageResult
                {
                    OutputDirectory = result.OutputDirectory,
                    VtuPath = result.VtuPath,
                    NodesPath = result.NodesPath,
                    ElementsPath = result.ElementsPath,
                    BoundaryFacesPath = result.BoundaryFacesPath,
                    MaterialsPath = result.MaterialsPath,
                    ManifestPath = result.ManifestPath,
                    NodeCount = result.NodeCount,
                    TetrahedronCount = result.TetrahedronCount,
                    BoundaryFaceCount = result.BoundaryFaceCount,
                    MaterialCount = result.MaterialCount
                });
        }
        catch (Exception ex)
        {
            return CommonToolResponse<ExportFemPackageResult>.Fail($"FEM export failed: {ex.Message}");
        }
    }

    private CommonToolResponse<ExportResult> ExportSurface(ExportSurfaceRequest request, bool isStl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return CommonToolResponse<ExportResult>.Fail("OutputPath is required.");
        }

        if (!request.Overwrite && File.Exists(request.OutputPath))
        {
            return CommonToolResponse<ExportResult>.Fail($"File already exists: {request.OutputPath}. Set overwrite=true to replace.");
        }

        var combined = ResolveExportSurface();
        if (combined is null)
        {
            return CommonToolResponse<ExportResult>.Fail(
                "No surface available. Build a surface mesh and/or place electrodes first.");
        }

        try
        {
            if (isStl)
            {
                meshExport.WriteStl(combined, request.OutputPath);
            }
            else
            {
                meshExport.WritePoly(combined, request.OutputPath);
            }

            var info = new FileInfo(request.OutputPath);
            return CommonToolResponse<ExportResult>.Ok(
                $"{(isStl ? "STL" : "POLY")} written to {request.OutputPath}.",
                new ExportResult
                {
                    OutputPath = request.OutputPath,
                    FileSizeBytes = info.Exists ? info.Length : 0,
                    PointCount = (int)combined.GetNumberOfPoints(),
                    TriangleCount = (int)combined.GetNumberOfPolys()
                });
        }
        catch (Exception ex)
        {
            return CommonToolResponse<ExportResult>.Fail($"Export failed: {ex.Message}");
        }
    }

    public Task<PipelineStatusDto> GetPipelineStatusAsync(CancellationToken cancellationToken)
    {
        var study = sceneState.Study;
        var artifacts = sceneState.Artifacts;
        return Task.FromResult(new PipelineStatusDto
        {
            StudyLoaded = study is not null,
            SourceKind = study?.SourceKind.ToString() ?? string.Empty,
            RoiCount = study?.Rois.Count ?? 0,
            ExternalRoiId = study?.ExternalRoiId,
            LabelMapBuilt = artifacts.LabelMap is not null,
            SurfaceMeshCount = artifacts.SurfaceMeshes.Count,
            ElectrodePlacementCount = sceneState.Electrodes.Placements.Count,
            AttachmentCompleted = artifacts.AttachmentResult is not null,
            VolumeMeshGenerated = sceneState.VolumeMesh is not null
        });
    }



    public Task<CommonToolResponse<bool>> ShowMeshWindowAsync(CancellationToken cancellationToken)
    {
        var combined = ResolveExportSurface();
        if (combined is null)
        {
            return Task.FromResult(CommonToolResponse<bool>.Fail(
                "No surface available to show. Build a surface mesh and/or place electrodes first."));
        }

        // We use a dedicated STA thread to launch a WPF window containing the VTK control.
        // This is the most robust way to show a VTK window in a .NET environment.
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var win = new System.Windows.Window
                {
                    Title = "MeshGen 3D Preview (Native)",
                    Width = 1024,
                    Height = 768,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 26, 30))
                };

                var host = new System.Windows.Forms.Integration.WindowsFormsHost();
                var renderControl = new RenderWindowControl { Dock = System.Windows.Forms.DockStyle.Fill };
                host.Child = renderControl;
                win.Content = host;

                win.Loaded += (s, e) =>
                {
                    var renderer = renderControl.RenderWindow.GetRenderers().GetFirstRenderer();
                    renderer.SetBackground(0.06, 0.06, 0.07);
                    renderer.SetBackground2(0.11, 0.12, 0.15);
                    renderer.GradientBackgroundOn();

                    var mapper = vtkPolyDataMapper.New();
                    mapper.SetInputData(combined);
                    mapper.ScalarVisibilityOff();

                    var actor = vtkActor.New();
                    actor.SetMapper(mapper);
                    actor.GetProperty().SetColor(0.35, 0.65, 1.0);
                    actor.GetProperty().SetAmbient(0.3);
                    actor.GetProperty().SetDiffuse(0.7);

                    renderer.AddActor(actor);
                    renderer.ResetCamera();
                    renderControl.RenderWindow.Render();
                };

                win.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"WPF/VTK Popup Error: {ex.Message}");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();

        return Task.FromResult(CommonToolResponse<bool>.Ok("VTK preview window opened.", true));
    }

    private static ImportDicomStudyResult BuildImportResult(StudyContext study) => new()
    {
        StudyKind = study.SourceKind.ToString(),
        SliceCount = study.SliceCount,
        RoiCount = study.Rois.Count,
        RoiWithContoursCount = study.Rois.Count(roi => roi.HasContours),
        Spacing = study.Spacing.ToArray(),
        Dimensions = study.Dimensions.ToArray(),
        ExternalRoiId = study.ExternalRoiId
    };

    private ElectrodeSpec ResolveSpec(string? shape, IReadOnlyList<ElectrodeLayerDto>? layers)
    {
        var defaultSpec = sceneState.DefaultElectrodeSpec;
        if (shape is null && (layers is null || layers.Count == 0))
        {
            return defaultSpec;
        }

        // Build a per-call spec without mutating the default.
        var spec = new ElectrodeSpec();
        if (layers is { Count: > 0 })
        {
            spec.Layers.Clear();
            foreach (var layerDto in layers)
            {
                spec.Layers.Add(MapLayer(layerDto));
            }
        }
        if (shape is not null)
        {
            spec.Shape = ParseShape(shape);
        }
        return spec;
    }

    private static ElectrodeLayer MapLayer(ElectrodeLayerDto dto)
    {
        var layer = new ElectrodeLayer
        {
            Name = dto.Name,
            Radius = dto.Radius,
            Width = dto.Width,
            Depth = dto.Depth,
            Thickness = dto.Thickness,
            Conductivity = dto.Conductivity,
            Permittivity = dto.Permittivity
        };
        if (!string.IsNullOrWhiteSpace(dto.ColorHex))
        {
            try
            {
                if (WpfColorConverter.ConvertFromString(dto.ColorHex) is WpfColor parsed)
                {
                    layer.Color = parsed;
                }
            }
            catch
            {
                // ignore invalid color hex
            }
        }
        return layer;
    }

    private static ElectrodeShape ParseShape(string? shape)
    {
        if (string.IsNullOrWhiteSpace(shape))
        {
            return ElectrodeShape.Circle;
        }
        return Enum.TryParse<ElectrodeShape>(shape, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown electrode shape '{shape}'. Use Circle, Rectangle, or Triangle.");
    }

    private static ElasticEngineConfig BuildEngineConfig(AttachmentParametersDto? parameters)
    {
        if (parameters is null) return new ElasticEngineConfig();
        return new ElasticEngineConfig
        {
            ContactDistanceThresholdMm = parameters.ContactThresholdMm,
            ContactExpansionRing = parameters.ExpansionRings,
            SmoothingIterations = parameters.SmoothingIterations,
            HardConstraintWeight = parameters.HardWeight,
            SoftConstraintWeight = parameters.SoftWeight
        };
    }

    private vtkPolyData? ResolveExportSurface()
    {
        var body = sceneState.Artifacts.DeformedBodySurface ?? sceneState.Artifacts.ExternalSurface;
        var electrodes = sceneState.ElectrodeWorldMeshes.Values;
        if (body is null && electrodes.Count == 0) return null;
        if (electrodes.Count == 0) return body;

        var append = vtkAppendPolyData.New();
        if (body is not null) append.AddInputData(body);
        foreach (var mesh in electrodes) append.AddInputData(mesh);
        append.Update();

        var cleaner = vtkCleanPolyData.New();
        cleaner.SetInputConnection(append.GetOutputPort());
        cleaner.PointMergingOn();
        cleaner.Update();

        var combined = vtkPolyData.New();
        combined.DeepCopy(cleaner.GetOutput());
        return combined;
    }

    private IReadOnlyList<FemElectrodeExportInfo> BuildFemElectrodeExportInfos()
    {
        var result = new List<FemElectrodeExportInfo>();
        foreach (var placement in sceneState.Electrodes.Placements)
        {
            if (!sceneState.ElectrodeWorldMeshes.TryGetValue(placement.Id, out var surface))
            {
                continue;
            }

            var normal = placement.WorldNormal;
            if (normal.LengthSquared < 1e-8)
            {
                normal = new Vector3D(0, 0, 1);
            }
            normal.Normalize();

            result.Add(new FemElectrodeExportInfo
            {
                PlacementId = placement.Id,
                Surface = surface,
                BasePoint = placement.SurfacePoint + (normal * placement.Clearance),
                Normal = normal
            });
        }

        return result;
    }

    private static vtkPolyData ApplyTransform(vtkPolyData localMesh, vtkTransform transform)
    {
        var filter = vtkTransformPolyDataFilter.New();
        filter.SetInputData(localMesh);
        filter.SetTransform(transform);
        filter.Update();
        var world = vtkPolyData.New();
        world.DeepCopy(filter.GetOutput());
        return world;
    }

}
