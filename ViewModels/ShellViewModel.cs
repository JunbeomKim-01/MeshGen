using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using Kitware.VTK;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Infrastructure;
using MeshGenLite.Models;
using MeshGenLite.Services;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace MeshGenLite.ViewModels;

public sealed class PipelineStepViewModel : ObservableObject
{
    private PipelineStepStatus status;

    public PipelineStepViewModel(string title, string caption)
    {
        Title = title;
        Caption = caption;
    }

    public string Title { get; }

    public string Caption { get; }

    public PipelineStepStatus Status
    {
        get => status;
        set
        {
            if (SetProperty(ref status, value))
            {
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusGlyph));
            }
        }
    }

    public SolidColorBrush StatusBrush => Status switch
    {
        PipelineStepStatus.Completed => new((WpfColor)WpfColorConverter.ConvertFromString("#22C55E")),
        PipelineStepStatus.Running => new((WpfColor)WpfColorConverter.ConvertFromString("#60CDFF")),
        PipelineStepStatus.Ready => new((WpfColor)WpfColorConverter.ConvertFromString("#0078D4")),
        PipelineStepStatus.Failed => new((WpfColor)WpfColorConverter.ConvertFromString("#F43F5E")),
        _ => new((WpfColor)WpfColorConverter.ConvertFromString("#3A3A3A"))
    };

    public string StatusGlyph => Status switch
    {
        PipelineStepStatus.Completed => "OK",
        PipelineStepStatus.Running => "RUN",
        PipelineStepStatus.Ready => "RDY",
        PipelineStepStatus.Failed => "ERR",
        _ => "..."
    };
}

public sealed class ImportViewModel : ObservableObject
{
    private string ctSeriesPath = @"D:\testDicom\samsung1\anony0001\MR";
    private string rtssPath = @"D:\testDicom\samsung1\anony0001\MR\ai_rtstruct_nnt.dcm";
    private string targetSpacing = "2.0";
    private string summary = "Default DICOM test paths are prefilled. NIfTI (.nii/.nii.gz) is also supported.";
    private string niftiSelectionSummary = "No NIfTI files selected.";

    public ObservableCollection<string> NiftiPaths { get; } = new();

    public string CtSeriesPath
    {
        get => ctSeriesPath;
        set => SetProperty(ref ctSeriesPath, value);
    }

    public string RtssPath
    {
        get => rtssPath;
        set => SetProperty(ref rtssPath, value);
    }

    public string TargetSpacing
    {
        get => targetSpacing;
        set => SetProperty(ref targetSpacing, value);
    }

    public string Summary
    {
        get => summary;
        set => SetProperty(ref summary, value);
    }

    public string NiftiSelectionSummary
    {
        get => niftiSelectionSummary;
        set => SetProperty(ref niftiSelectionSummary, value);
    }

    public void SetNiftiPaths(IEnumerable<string> paths)
    {
        NiftiPaths.Clear();
        foreach (var path in paths)
        {
            NiftiPaths.Add(path);
        }

        NiftiSelectionSummary = NiftiPaths.Count switch
        {
            0 => "No NIfTI files selected.",
            1 => Path.GetFileName(NiftiPaths[0]),
            _ => $"{NiftiPaths.Count} files selected. First: {Path.GetFileName(NiftiPaths[0])}"
        };
    }
}

public sealed class RoiPanelViewModel : ObservableObject
{
    private RoiDescriptor? selectedExternalRoi;
    private string externalSmoothingIterations = "20";

    public ObservableCollection<RoiDescriptor> Rois { get; } = new();

    public IEnumerable<RoiDescriptor> ExternalCandidates => Rois.Where(roi => roi.HasContours);

    public RoiDescriptor? SelectedExternalRoi
    {
        get => selectedExternalRoi;
        set => SetProperty(ref selectedExternalRoi, value);
    }

    public string ExternalSmoothingIterations
    {
        get => externalSmoothingIterations;
        set => SetProperty(ref externalSmoothingIterations, value);
    }

    public int ResolveExternalSmoothingIterations()
        => int.TryParse(ExternalSmoothingIterations, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : 20;

    public void RefreshExternalCandidates() => OnPropertyChanged(nameof(ExternalCandidates));
}

public sealed class ElectrodePanelViewModel : ObservableObject
{
    private PlacementMode placementMode;
    private ElectrodePlacement? selectedPlacement;
    private ElectrodeLayer? selectedLayer;

    public ElectrodePanelViewModel(ObservableCollection<ElectrodePlacement> placements)
    {
        Placements = placements;
        ShapeOptions = (ElectrodeShape[])Enum.GetValues(typeof(ElectrodeShape));
        SelectedLayer = Spec.Layers.FirstOrDefault();
        Spec.Layers.CollectionChanged += (_, _) =>
        {
            if (SelectedLayer is null || !Spec.Layers.Contains(SelectedLayer))
            {
                SelectedLayer = Spec.Layers.FirstOrDefault();
            }
        };
    }

    public ElectrodeSpec Spec { get; } = new();

    public ObservableCollection<ElectrodePlacement> Placements { get; }

    public IReadOnlyList<ElectrodeShape> ShapeOptions { get; }

    public ElectrodeLayer? SelectedLayer
    {
        get => selectedLayer;
        set => SetProperty(ref selectedLayer, value);
    }

    public PlacementMode PlacementMode
    {
        get => placementMode;
        set
        {
            if (SetProperty(ref placementMode, value))
            {
                OnPropertyChanged(nameof(IsPlacementModeActive));
                OnPropertyChanged(nameof(PlacementButtonText));
                OnPropertyChanged(nameof(PlacementStatusText));
            }
        }
    }

    public bool IsPlacementModeActive => PlacementMode == PlacementMode.ElectrodePlacement;

    public string PlacementButtonText => IsPlacementModeActive ? "Cancel Placement" : "Place Electrode";

    public string PlacementStatusText => IsPlacementModeActive
        ? "Placement mode active. Hover the external mesh and left-click to place."
        : "Placement mode idle.";

    public ElectrodePlacement? SelectedPlacement
    {
        get => selectedPlacement;
        set => SetProperty(ref selectedPlacement, value);
    }
}

public sealed class DeformationConfigViewModel : ObservableObject
{
    private string smoothingIterations = "2";
    private string contactDistanceThreshold = "2.0";
    private string contactExpansionRing = "2";
    private string externalIndentationDepth = "1.0";
    private string softConstraintWeight = "0.35";
    private string hardConstraintWeight = "8.0";

    public string SmoothingIterations
    {
        get => smoothingIterations;
        set => SetProperty(ref smoothingIterations, value);
    }

    public string ContactDistanceThreshold
    {
        get => contactDistanceThreshold;
        set => SetProperty(ref contactDistanceThreshold, value);
    }

    public string ContactExpansionRing
    {
        get => contactExpansionRing;
        set => SetProperty(ref contactExpansionRing, value);
    }

    public string ExternalIndentationDepth
    {
        get => externalIndentationDepth;
        set => SetProperty(ref externalIndentationDepth, value);
    }

    public string SoftConstraintWeight
    {
        get => softConstraintWeight;
        set => SetProperty(ref softConstraintWeight, value);
    }

    public string HardConstraintWeight
    {
        get => hardConstraintWeight;
        set => SetProperty(ref hardConstraintWeight, value);
    }

    public ElasticEngineConfig BuildConfig()
    {
        return new ElasticEngineConfig
        {
            SmoothingIterations = ParseInt(SmoothingIterations, 2),
            ContactDistanceThresholdMm = ParseDouble(ContactDistanceThreshold, 2.0),
            ContactExpansionRing = ParseInt(ContactExpansionRing, 2),
            ExternalIndentationDepthMm = ParseDouble(ExternalIndentationDepth, 1.0),
            SoftConstraintWeight = ParseDouble(SoftConstraintWeight, 0.35),
            HardConstraintWeight = ParseDouble(HardConstraintWeight, 8.0)
        };
    }

    private static double ParseDouble(string text, double fallback)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

public sealed class AttachmentPanelViewModel : ObservableObject
{
    private DeformationStepStatus status;
    private string summary = "No attachment result. Place electrodes and run attachment.";
    private string qualitySummary = "Quality report will appear here.";
    private bool isContactPatchVisible;
    private bool isShowingDeformedBody;

    public DeformationStepStatus Status
    {
        get => status;
        set
        {
            if (SetProperty(ref status, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string Summary
    {
        get => summary;
        set => SetProperty(ref summary, value);
    }

    public string QualitySummary
    {
        get => qualitySummary;
        set => SetProperty(ref qualitySummary, value);
    }

    public bool IsContactPatchVisible
    {
        get => isContactPatchVisible;
        set
        {
            if (SetProperty(ref isContactPatchVisible, value))
            {
                OnPropertyChanged(nameof(ContactPatchButtonText));
            }
        }
    }

    public bool IsShowingDeformedBody
    {
        get => isShowingDeformedBody;
        set
        {
            if (SetProperty(ref isShowingDeformedBody, value))
            {
                OnPropertyChanged(nameof(BodyViewButtonText));
            }
        }
    }

    public string StatusText => Status switch
    {
        DeformationStepStatus.Aligning => "Aligning",
        DeformationStepStatus.Extracting => "Extracting Patch",
        DeformationStepStatus.Deforming => "Deforming",
        DeformationStepStatus.Validating => "Validating",
        DeformationStepStatus.Done => "Done",
        _ => "Idle"
    };

    public string ContactPatchButtonText => IsContactPatchVisible ? "Hide Contact Patch" : "Show Contact Patch";

    public string BodyViewButtonText => IsShowingDeformedBody ? "Show Original Body" : "Show Deformed Body";

    public void Reset(string summaryText)
    {
        Status = DeformationStepStatus.Idle;
        Summary = summaryText;
        QualitySummary = "Quality report will appear here.";
        IsContactPatchVisible = false;
        IsShowingDeformedBody = false;
    }
}

public sealed class VolumeMeshViewModel : ObservableObject
{
    private string alphaMm = "0";
    private string maxPoints = "5000";
    private string summary = "No volume mesh. Build the external surface (optionally run attachment) and click Generate.";

    public string AlphaMm
    {
        get => alphaMm;
        set => SetProperty(ref alphaMm, value);
    }

    public string MaxPoints
    {
        get => maxPoints;
        set => SetProperty(ref maxPoints, value);
    }

    public string Summary
    {
        get => summary;
        set => SetProperty(ref summary, value);
    }

    public double ResolveAlpha()
        => double.TryParse(AlphaMm, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : 0.0;

    public int ResolveMaxPoints()
        => int.TryParse(MaxPoints, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 0;
}

public sealed class StatusBarViewModel : ObservableObject
{
    private string message = "Ready";
    private string detail = "Mesh generation MVP";
    private bool isBusy;
    private string hudText = "No mesh loaded";

    public string Message
    {
        get => message;
        set => SetProperty(ref message, value);
    }

    public string Detail
    {
        get => detail;
        set => SetProperty(ref detail, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set => SetProperty(ref isBusy, value);
    }

    public string HudText
    {
        get => hudText;
        set => SetProperty(ref hudText, value);
    }
}

public sealed class ShellViewModel : ObservableObject
{
    private readonly IPipelineService pipelineService;
    private readonly IElasticDeformationService elasticDeformationService;
    private readonly VtkViewportService viewportService;
    private readonly IRtssDiscoveryService rtssDiscoveryService;
    private readonly MeshHitTestService meshHitTestService;
    private readonly ElectrodeFactory electrodeFactory;
    private readonly ElectrodeTransformBuilder transformBuilder;
    private readonly ElectrodeRepository electrodeRepository;
    private readonly ElectrodePlacementController placementController;
    private readonly IVolumeMeshService volumeMeshService;
    private readonly IMeshExportService meshExportService;
    private readonly Progress<PipelineProgress> progress;
    private CancellationTokenSource? currentCts;
    private StudyContext? study;
    private readonly PipelineArtifacts artifacts = new();
    private VolumeMeshResult? volumeMeshResult;

    public ShellViewModel(IPipelineService pipelineService, VtkViewportService viewportService)
    {
        this.pipelineService = pipelineService;
        this.viewportService = viewportService;
        elasticDeformationService = new ElasticDeformationService();
        rtssDiscoveryService = new RtssDiscoveryService();
        meshHitTestService = new MeshHitTestService(viewportService);
        electrodeFactory = new ElectrodeFactory();
        transformBuilder = new ElectrodeTransformBuilder();
        electrodeRepository = new ElectrodeRepository();
        placementController = new ElectrodePlacementController(meshHitTestService, electrodeFactory, transformBuilder, viewportService);
        volumeMeshService = new VolumeMeshService();
        meshExportService = new MeshExportService();
        progress = new Progress<PipelineProgress>(OnProgressReported);

        Import = new ImportViewModel();
        RoiPanel = new RoiPanelViewModel();
        ElectrodePanel = new ElectrodePanelViewModel(electrodeRepository.Placements);
        DeformationConfig = new DeformationConfigViewModel();
        Attachment = new AttachmentPanelViewModel();
        VolumeMesh = new VolumeMeshViewModel();
        StatusBar = new StatusBarViewModel();
        RoiPanel.PropertyChanged += OnRoiPanelPropertyChanged;
        ElectrodePanel.PropertyChanged += OnElectrodePanelPropertyChanged;

        Steps = new ObservableCollection<PipelineStepViewModel>
        {
            new("1", "Import"),
            new("2", "Label Map"),
            new("3", "Surface"),
            new("4", "Spec"),
            new("5", "Placement"),
            new("6", "Attachment"),
            new("7", "Volume")
        };
        Steps[0].Status = PipelineStepStatus.Ready;

        BrowseCtSeriesCommand = new RelayCommand(BrowseCtSeries);
        BrowseVolumeFileCommand = new RelayCommand(BrowseVolumeFile);
        BrowseRtssCommand = new RelayCommand(BrowseRtss);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !StatusBar.IsBusy);
        BuildLabelMapCommand = new AsyncRelayCommand(BuildLabelMapAsync, () => study is not null && !StatusBar.IsBusy);
        BuildSurfaceCommand = new AsyncRelayCommand(BuildSurfaceAsync, () => artifacts.LabelMap is not null && !StatusBar.IsBusy);
        AttachElectrodesCommand = new AsyncRelayCommand(AttachElectrodesAsync, CanAttachElectrodes);
        RemovePlacementCommand = new RelayCommand(RemovePlacement, () => ElectrodePanel.SelectedPlacement is not null);
        ToggleContactPatchCommand = new RelayCommand(ToggleContactPatch, () => artifacts.AttachmentResult is not null && !StatusBar.IsBusy);
        ToggleBodyViewCommand = new RelayCommand(ToggleBodyView, () => artifacts.AttachmentResult is not null && !StatusBar.IsBusy);
        ToggleViewportPickCommand = new RelayCommand(TogglePlacementMode, () => !StatusBar.IsBusy);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => StatusBar.IsBusy);
        ResetCameraCommand = new RelayCommand(() => viewportService.ResetCamera(), () => viewportService.IsInitialized);
        GenerateVolumeMeshCommand = new AsyncRelayCommand(GenerateVolumeMeshAsync, CanGenerateVolumeMesh);
        ExportStlCommand = new RelayCommand(ExportStl, CanExportSurface);
        ExportPolyCommand = new RelayCommand(ExportPoly, CanExportSurface);
        AddElectrodeLayerCommand = new RelayCommand(AddElectrodeLayer);
        RemoveElectrodeLayerCommand = new RelayCommand<ElectrodeLayer>(RemoveElectrodeLayer, layer => layer is not null && ElectrodePanel.Spec.Layers.Count > 1);
        MoveLayerUpCommand = new RelayCommand<ElectrodeLayer>(layer => MoveLayer(layer, -1), layer => layer is not null && ElectrodePanel.Spec.Layers.IndexOf(layer) > 0);
        MoveLayerDownCommand = new RelayCommand<ElectrodeLayer>(layer => MoveLayer(layer, 1), layer => layer is not null && ElectrodePanel.Spec.Layers.IndexOf(layer) >= 0 && ElectrodePanel.Spec.Layers.IndexOf(layer) < ElectrodePanel.Spec.Layers.Count - 1);
        ResetElectrodeLayersCommand = new RelayCommand(() => ElectrodePanel.Spec.ResetToDefault());
    }

    public ImportViewModel Import { get; }

    public RoiPanelViewModel RoiPanel { get; }

    public ElectrodePanelViewModel ElectrodePanel { get; }

    public DeformationConfigViewModel DeformationConfig { get; }

    public AttachmentPanelViewModel Attachment { get; }

    public VolumeMeshViewModel VolumeMesh { get; }

    public StatusBarViewModel StatusBar { get; }

    public ObservableCollection<PipelineStepViewModel> Steps { get; }

    public ICommand BrowseCtSeriesCommand { get; }

    public ICommand BrowseVolumeFileCommand { get; }

    public ICommand BrowseRtssCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand BuildLabelMapCommand { get; }

    public ICommand BuildSurfaceCommand { get; }

    public ICommand AttachElectrodesCommand { get; }

    public ICommand RemovePlacementCommand { get; }

    public ICommand ToggleContactPatchCommand { get; }

    public ICommand ToggleBodyViewCommand { get; }

    public ICommand ToggleViewportPickCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ResetCameraCommand { get; }

    public ICommand GenerateVolumeMeshCommand { get; }

    public ICommand ExportStlCommand { get; }

    public ICommand ExportPolyCommand { get; }

    public ICommand AddElectrodeLayerCommand { get; }

    public ICommand RemoveElectrodeLayerCommand { get; }

    public ICommand MoveLayerUpCommand { get; }

    public ICommand MoveLayerDownCommand { get; }

    public ICommand ResetElectrodeLayersCommand { get; }

    public void InitializeViewport(RenderWindowControl control)
    {
        viewportService.Initialize(control);
        RefreshCommandStates();
    }

    public void HandleViewportMove(int x, int y)
    {
        if (!placementController.IsActive || StatusBar.IsBusy)
        {
            return;
        }

        if (placementController.UpdatePreview(x, y, ElectrodePanel.Spec, ElectrodePanel.Spec.Color))
        {
            var hit = placementController.CurrentHit;
            if (hit is not null)
            {
                StatusBar.Message = "Placement preview";
                StatusBar.Detail = $"Cell {hit.CellId} 쨌 {hit.Position.X:F1}, {hit.Position.Y:F1}, {hit.Position.Z:F1}";
            }
        }
        else
        {
            StatusBar.Message = "Placement mode active";
            StatusBar.Detail = "Hover the external surface to preview an electrode.";
        }
    }

    public void HandleViewportMouseDown(int x, int y, MouseButtons button)
    {
        if (!placementController.IsActive || StatusBar.IsBusy)
        {
            return;
        }

        if (button == MouseButtons.Right)
        {
            EndPlacementMode("Placement cancelled", "Right-click cancelled placement mode.");
            return;
        }

        if (button != MouseButtons.Left)
        {
            return;
        }

        if (!placementController.HasPreview)
        {
            placementController.UpdatePreview(x, y, ElectrodePanel.Spec, ElectrodePanel.Spec.Color);
        }

        var placement = placementController.CommitPreview(
            electrodeRepository,
            ElectrodePanel.Spec,
            ElectrodePanel.Spec.Color,
            viewportService.ExternalBodyMeshId);

        if (placement is null)
        {
            StatusBar.Message = "Placement skipped";
            StatusBar.Detail = "Move over the external surface before clicking.";
            return;
        }

        InvalidateAttachment("Placement changed. Re-run attachment to refresh the deformation.");
        ElectrodePanel.SelectedPlacement = placement;
        artifacts.Stats = BuildStats();
        StatusBar.HudText = artifacts.Stats.Summary;
        StatusBar.Message = "Electrode placed";
        StatusBar.Detail = $"{placement.WorldSummary} 쨌 {placement.CellSummary}";
        UpdatePlacementAndAttachmentSteps();
        RefreshCommandStates();
    }

    public void HandleEscape()
    {
        if (placementController.IsActive)
        {
            EndPlacementMode("Placement cancelled", "ESC cancelled placement mode.");
        }
    }

    private async Task ImportAsync()
    {
        await RunTrackedAsync(async token =>
        {
            SetStep(0, PipelineStepStatus.Running);
            ResetFromStep(1);
            viewportService.Clear();
            electrodeRepository.Clear();
            placementController.CancelPlacement();
            ElectrodePanel.PlacementMode = PlacementMode.None;
            RoiPanel.Rois.Clear();
            artifacts.SurfaceMeshes.Clear();
            artifacts.ExternalSurface = null;
            artifacts.LabelMap = null;
            artifacts.MergedPreview = null;
            artifacts.ElectrodeMeshes.Clear();
            InvalidateAttachment("No attachment result. Import a study and build the body surface first.");

            if (string.IsNullOrWhiteSpace(Import.RtssPath) || !File.Exists(Import.RtssPath))
            {
                if (Directory.Exists(Import.CtSeriesPath))
                {
                    AutoDetectRtss(Import.CtSeriesPath);
                }
            }

            var request = new StudyImportRequest
            {
                CtSeriesPath = Import.CtSeriesPath,
                NiftiPaths = Import.NiftiPaths.ToArray(),
                RtssPath = Import.RtssPath,
                TargetSpacing = ParseDouble(Import.TargetSpacing, 2.0)
            };

            study = await pipelineService.ImportAsync(request, progress, token);
            RoiPanel.Rois.Clear();
            foreach (var roi in study.Rois)
            {
                roi.PropertyChanged += OnRoiPropertyChanged;
                RoiPanel.Rois.Add(roi);
            }

            RoiPanel.RefreshExternalCandidates();
            RoiPanel.SelectedExternalRoi = RoiPanel.Rois.FirstOrDefault(roi => roi.Id == study.ExternalRoiId)
                ?? RoiPanel.Rois.FirstOrDefault(roi => roi.HasContours);

            var sourceSummary = study.SourcePaths.Count > 1
                ? $"{study.SourcePaths.Count} files"
                : $"{study.SliceCount} slices";
            Import.Summary = $"{study.SourceKind} 쨌 {sourceSummary} 쨌 spacing {study.Spacing[0]:F2}/{study.Spacing[1]:F2}/{study.Spacing[2]:F2} mm 쨌 ROI {study.Rois.Count(roi => roi.HasContours)}";
            SetStep(0, PipelineStepStatus.Completed);
            SetStep(1, PipelineStepStatus.Ready);
            StatusBar.HudText = "Imported study ready";
            RefreshCommandStates();
        });
    }

    private async Task BuildLabelMapAsync()
    {
        if (study is null)
        {
            return;
        }

        await RunTrackedAsync(async token =>
        {
            SetStep(1, PipelineStepStatus.Running);
            var selected = RoiPanel.Rois.Where(roi => roi.IsSelected && roi.HasContours).ToList();
            study.ExternalRoiId = RoiPanel.SelectedExternalRoi?.Id;
            if (RoiPanel.SelectedExternalRoi is not null)
            {
                RoiPanel.SelectedExternalRoi.Role = RoiRole.External;
            }

            artifacts.LabelMap = await pipelineService.BuildLabelMapAsync(study, selected, ParseDouble(Import.TargetSpacing, 2.0), progress, token);
            SetStep(1, PipelineStepStatus.Completed);
            SetStep(2, PipelineStepStatus.Ready);
            RefreshCommandStates();
        });
    }

    private async Task BuildSurfaceAsync()
    {
        if (study is null || artifacts.LabelMap is null)
        {
            return;
        }

        await RunTrackedAsync(async token =>
        {
            placementController.CancelPlacement();
            ElectrodePanel.PlacementMode = PlacementMode.None;
            electrodeRepository.Clear();
            viewportService.ClearCommittedElectrodes();
            InvalidateAttachment("Surface changed. Place electrodes and run attachment.");

            SetStep(2, PipelineStepStatus.Running);
            artifacts.SurfaceMeshes.Clear();
            var surfaces = await pipelineService.BuildSurfaceAsync(
                study,
                artifacts.LabelMap,
                RoiPanel.Rois.ToList(),
                RoiPanel.ResolveExternalSmoothingIterations(),
                progress,
                token);
            foreach (var surface in surfaces)
            {
                artifacts.SurfaceMeshes.Add(surface);
            }

            EnsureExternalSelection();
            artifacts.ExternalSurface = artifacts.SurfaceMeshes.FirstOrDefault(mesh => mesh.Roi.Id == RoiPanel.SelectedExternalRoi?.Id)?.PolyData;
            artifacts.Stats = BuildStats();
            viewportService.ShowSurfaceMeshes(artifacts.SurfaceMeshes, RoiPanel.SelectedExternalRoi?.Id);
            StatusBar.HudText = artifacts.Stats.Summary;
            StatusBar.Message = "Surface ready";
            StatusBar.Detail = "You can start interactive electrode placement.";
            SetStep(2, PipelineStepStatus.Completed);
            SetStep(3, PipelineStepStatus.Ready);
            SetStep(4, PipelineStepStatus.Ready);
            SetStep(5, PipelineStepStatus.Pending);
            RefreshCommandStates();
        });
    }

    private async Task AttachElectrodesAsync()
    {
        if (artifacts.ExternalSurface is null)
        {
            return;
        }

        await RunTrackedAsync(async token =>
        {
            SetStep(5, PipelineStepStatus.Running);
            Attachment.Status = DeformationStepStatus.Aligning;
            Attachment.Summary = $"Attaching {electrodeRepository.Placements.Count} electrode(s) to the selected body surface.";
            Attachment.QualitySummary = "Running deformation pipeline...";

            var attachmentProgress = new Progress<PipelineProgress>(update =>
            {
                StatusBar.Message = update.Message;
                StatusBar.Detail = update.Detail;
                UpdateAttachmentStage(update.Detail);
            });

            var result = await elasticDeformationService.AttachAsync(
                artifacts.ExternalSurface,
                ElectrodePanel.Spec,
                electrodeRepository.Placements.ToList(),
                DeformationConfig.BuildConfig(),
                attachmentProgress,
                token);

            artifacts.AttachmentResult = result;
            artifacts.OriginalBodySurfaceBackup = result.OriginalBodyMesh is null
                ? null
                : MeshConverter.ToVtkPolyData(result.OriginalBodyMesh);
            artifacts.DeformedBodySurface = result.FinalBodyMesh is null
                ? null
                : MeshConverter.ToVtkPolyData(result.FinalBodyMesh);

            var layerColors = electrodeFactory.GetLayerColors(ElectrodePanel.Spec);
            foreach (var attachedElectrode in result.DeformedElectrodeMeshes.OrderBy(pair => pair.Key))
            {
                viewportService.AddCommittedElectrode(
                    attachedElectrode.Key,
                    MeshConverter.ToVtkPolyData(attachedElectrode.Value),
                    ElectrodePanel.Spec.Color,
                    1.0,
                    layerColors);
            }

            if (artifacts.OriginalBodySurfaceBackup is not null && artifacts.DeformedBodySurface is not null)
            {
                viewportService.ShowAttachmentResult(
                    artifacts.OriginalBodySurfaceBackup,
                    artifacts.DeformedBodySurface,
                    result.UnionContactPatchVertexIds);
            }

            Attachment.Status = DeformationStepStatus.Done;
            Attachment.IsContactPatchVisible = true;
            Attachment.IsShowingDeformedBody = true;
            Attachment.Summary = $"{result.PerElectrodeSessions.Count} electrodes attached; External surface indented under each footprint.";
            Attachment.QualitySummary = result.AggregateQualityReport?.Summary ?? "Attachment completed without quality details.";
            SetStep(5, PipelineStepStatus.Completed);
            SetStep(6, PipelineStepStatus.Ready);
            InvalidateVolumeMesh("Attachment changed. Regenerate the volume mesh.");
            RefreshCommandStates();
        });
    }

    private async Task GenerateVolumeMeshAsync()
    {
        var surface = ResolveExportSurface();
        if (surface is null)
        {
            return;
        }

        await RunTrackedAsync(async token =>
        {
            SetStep(6, PipelineStepStatus.Running);
            VolumeMesh.Summary = "Running Delaunay3D tetrahedralization...";
            var result = await volumeMeshService.GenerateAsync(
                surface,
                VolumeMesh.ResolveAlpha(),
                VolumeMesh.ResolveMaxPoints(),
                progress,
                token);
            volumeMeshResult = result;
            VolumeMesh.Summary = $"Volume mesh ready · {result.Summary}";
            SetStep(6, PipelineStepStatus.Completed);
            RefreshCommandStates();
        });
    }

    private void ExportStl()
    {
        var surface = ResolveExportSurface();
        if (surface is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "STL files (*.stl)|*.stl|All files (*.*)|*.*",
            DefaultExt = "stl",
            FileName = BuildDefaultExportFileName("stl")
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            meshExportService.WriteStl(surface, dialog.FileName);
            StatusBar.Message = "STL exported";
            StatusBar.Detail = Path.GetFileName(dialog.FileName);
        }
        catch (Exception exception)
        {
            StatusBar.Message = "STL export failed";
            StatusBar.Detail = exception.Message;
        }
    }

    private void ExportPoly()
    {
        var surface = ResolveExportSurface();
        if (surface is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "TetGen PLC (*.poly)|*.poly|All files (*.*)|*.*",
            DefaultExt = "poly",
            FileName = BuildDefaultExportFileName("poly")
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            meshExportService.WritePoly(surface, dialog.FileName);
            StatusBar.Message = "POLY exported";
            StatusBar.Detail = Path.GetFileName(dialog.FileName);
        }
        catch (Exception exception)
        {
            StatusBar.Message = "POLY export failed";
            StatusBar.Detail = exception.Message;
        }
    }

    private vtkPolyData? ResolveExportSurface()
    {
        var body = artifacts.DeformedBodySurface ?? artifacts.ExternalSurface;
        var electrodes = viewportService.GetCommittedElectrodeSurfaces();
        if (body is null && electrodes.Count == 0)
        {
            return null;
        }

        if (electrodes.Count == 0)
        {
            return body;
        }

        var append = vtkAppendPolyData.New();
        if (body is not null)
        {
            append.AddInputData(body);
        }

        foreach (var electrode in electrodes)
        {
            append.AddInputData(electrode);
        }

        append.Update();

        var cleaner = vtkCleanPolyData.New();
        cleaner.SetInputConnection(append.GetOutputPort());
        cleaner.PointMergingOn();
        cleaner.Update();

        var combined = vtkPolyData.New();
        combined.DeepCopy(cleaner.GetOutput());
        return combined;
    }

    private string BuildDefaultExportFileName(string extension)
    {
        var baseName = study is null
            ? "mesh"
            : Path.GetFileNameWithoutExtension(study.CtSeriesPath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "mesh";
        }

        var suffix = artifacts.DeformedBodySurface is not null ? "_deformed" : "_surface";
        return $"{baseName}{suffix}.{extension}";
    }

    private bool CanGenerateVolumeMesh()
        => ResolveExportSurface() is not null && !StatusBar.IsBusy;

    private bool CanExportSurface()
        => ResolveExportSurface() is not null && !StatusBar.IsBusy;

    private void InvalidateVolumeMesh(string summaryText)
    {
        volumeMeshResult = null;
        VolumeMesh.Summary = summaryText;
        if (Steps[6].Status == PipelineStepStatus.Completed)
        {
            SetStep(6, PipelineStepStatus.Ready);
        }
    }

    private async Task RunTrackedAsync(Func<CancellationToken, Task> action)
    {
        currentCts?.Cancel();
        currentCts = new CancellationTokenSource();
        StatusBar.IsBusy = true;
        RefreshCommandStates();

        try
        {
            await action(currentCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusBar.Message = "Operation cancelled";
            StatusBar.Detail = "The current pipeline step was cancelled.";
            if (Steps[5].Status == PipelineStepStatus.Running)
            {
                SetStep(5, electrodeRepository.Placements.Count > 0 ? PipelineStepStatus.Ready : PipelineStepStatus.Pending);
                Attachment.Status = DeformationStepStatus.Idle;
                Attachment.QualitySummary = "Attachment was cancelled.";
            }
        }
        catch (Exception exception)
        {
            MarkActiveStepFailed();
            StatusBar.Message = "Operation failed";
            StatusBar.Detail = exception.Message;
            if (Steps[6].Status == PipelineStepStatus.Running)
            {
                VolumeMesh.Summary = exception.Message;
            }
            if (Steps[5].Status == PipelineStepStatus.Failed)
            {
                Attachment.Status = DeformationStepStatus.Idle;
                Attachment.QualitySummary = exception.Message;
            }
        }
        finally
        {
            StatusBar.IsBusy = false;
            RefreshCommandStates();
        }
    }

    private void BrowseCtSeries()
    {
        using var dialog = new FolderBrowserDialog
        {
            InitialDirectory = ResolveInitialDirectory(Import.CtSeriesPath)
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            Import.CtSeriesPath = dialog.SelectedPath;
            Import.SetNiftiPaths(Array.Empty<string>());
            AutoDetectRtss(dialog.SelectedPath);
        }
    }

    private void AutoDetectRtss(string directoryPath)
    {
        var detected = rtssDiscoveryService.FindRtssFile(directoryPath);
        if (detected is null)
        {
            return;
        }

        Import.RtssPath = detected;
        StatusBar.Message = "RTSS auto-detected";
        StatusBar.Detail = System.IO.Path.GetFileName(detected);
    }

    private void BrowseVolumeFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "NIfTI files (*.nii;*.nii.gz)|*.nii;*.nii.gz|All files (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = ResolveInitialDirectory(Import.CtSeriesPath)
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            Import.CtSeriesPath = dialog.FileNames[0];
            Import.SetNiftiPaths(dialog.FileNames);
            Import.RtssPath = string.Empty;
        }
    }

    private void BrowseRtss()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "DICOM RT Structure Set (*.dcm)|*.dcm|All files (*.*)|*.*",
            InitialDirectory = ResolveInitialDirectory(Import.RtssPath)
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            Import.RtssPath = dialog.FileName;
        }
    }

    private void RemovePlacement()
    {
        if (ElectrodePanel.SelectedPlacement is null)
        {
            return;
        }

        viewportService.RemoveCommittedElectrode(ElectrodePanel.SelectedPlacement.Id);
        electrodeRepository.Remove(ElectrodePanel.SelectedPlacement);
        ElectrodePanel.SelectedPlacement = null;
        InvalidateAttachment("Placement changed. Re-run attachment to refresh the deformation.");
        artifacts.Stats = BuildStats();
        StatusBar.HudText = artifacts.Stats.Summary;
        StatusBar.Message = "Electrode removed";
        StatusBar.Detail = "The selected electrode was deleted.";
        UpdatePlacementAndAttachmentSteps();
        RefreshCommandStates();
    }

    private void ToggleContactPatch()
    {
        if (artifacts.AttachmentResult is null)
        {
            return;
        }

        if (Attachment.IsContactPatchVisible)
        {
            viewportService.HideContactPatch();
            Attachment.IsContactPatchVisible = false;
        }
        else
        {
            viewportService.ShowContactPatch();
            Attachment.IsContactPatchVisible = true;
        }

        RefreshCommandStates();
    }

    private void ToggleBodyView()
    {
        if (artifacts.AttachmentResult is null)
        {
            return;
        }

        if (Attachment.IsShowingDeformedBody)
        {
            viewportService.ShowOriginalBody();
            Attachment.IsShowingDeformedBody = false;
        }
        else
        {
            viewportService.ShowDeformedBody();
            Attachment.IsShowingDeformedBody = true;
        }

        RefreshCommandStates();
    }

    private void TogglePlacementMode()
    {
        if (placementController.IsActive)
        {
            EndPlacementMode("Placement mode stopped", "Interactive placement is now idle.");
            return;
        }

        EnsureExternalSelection();
        if (!viewportService.HasExternalSurface || artifacts.ExternalSurface is null)
        {
            StatusBar.Message = "Placement unavailable";
            StatusBar.Detail = "Build the external surface before placing electrodes.";
            return;
        }

        placementController.BeginPlacement();
        ElectrodePanel.PlacementMode = PlacementMode.ElectrodePlacement;
        SetStep(3, PipelineStepStatus.Completed);
        SetStep(4, PipelineStepStatus.Running);
        StatusBar.Message = "Placement mode active";
        StatusBar.Detail = "Hover the external surface and left-click to place an electrode.";
        RefreshCommandStates();
    }

    private void EndPlacementMode(string message, string detail)
    {
        placementController.CancelPlacement();
        ElectrodePanel.PlacementMode = PlacementMode.None;
        if (artifacts.ExternalSurface is not null)
        {
            SetStep(4, electrodeRepository.Placements.Count > 0 ? PipelineStepStatus.Completed : PipelineStepStatus.Ready);
        }

        StatusBar.Message = message;
        StatusBar.Detail = detail;
        RefreshCommandStates();
    }

    private void CancelCurrentOperation()
    {
        currentCts?.Cancel();
    }

    private void OnProgressReported(PipelineProgress progressUpdate)
    {
        StatusBar.Message = progressUpdate.Message;
        StatusBar.Detail = progressUpdate.Detail;
    }

    private void OnRoiPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RoiDescriptor roi)
        {
            return;
        }

        if (e.PropertyName is nameof(RoiDescriptor.Visibility) or nameof(RoiDescriptor.Opacity) or nameof(RoiDescriptor.Color))
        {
            viewportService.UpdateRoiAppearance(roi);
            artifacts.Stats = BuildStats();
            StatusBar.HudText = artifacts.Stats.Summary;
        }
    }

    private void OnRoiPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RoiPanelViewModel.SelectedExternalRoi))
        {
            return;
        }

        if (study is not null)
        {
            study.ExternalRoiId = RoiPanel.SelectedExternalRoi?.Id;
        }

        foreach (var roi in RoiPanel.Rois)
        {
            roi.Role = roi.Id == RoiPanel.SelectedExternalRoi?.Id ? RoiRole.External : RoiRole.Tissue;
        }

        InvalidateAttachment("External ROI changed. Re-run attachment for the new target surface.");
        artifacts.ExternalSurface = artifacts.SurfaceMeshes
            .FirstOrDefault(mesh => mesh.Roi.Id == RoiPanel.SelectedExternalRoi?.Id)?.PolyData;
        viewportService.SetExternalSurfaceRoi(RoiPanel.SelectedExternalRoi?.Id);

        if (placementController.IsActive && !viewportService.HasExternalSurface)
        {
            EndPlacementMode("Placement cancelled", "The selected external ROI is not available as a surface.");
            return;
        }

        if (artifacts.SurfaceMeshes.Count > 0 && RoiPanel.SelectedExternalRoi is not null)
        {
            StatusBar.Message = "External ROI updated";
            StatusBar.Detail = $"{RoiPanel.SelectedExternalRoi.Name} is now the placement target.";
        }

        UpdatePlacementAndAttachmentSteps();
        RefreshCommandStates();
    }

    private void OnElectrodePanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ElectrodePanelViewModel.SelectedPlacement))
        {
            RefreshCommandStates();
        }
    }

    private void EnsureExternalSelection()
    {
        if (RoiPanel.SelectedExternalRoi is not null)
        {
            viewportService.SetExternalSurfaceRoi(RoiPanel.SelectedExternalRoi.Id);
            return;
        }

        var fallback = RoiPanel.Rois.FirstOrDefault(roi => roi.Role == RoiRole.External && roi.HasContours)
            ?? RoiPanel.Rois.FirstOrDefault(roi => roi.HasContours);
        if (fallback is null)
        {
            viewportService.SetExternalSurfaceRoi(null);
            return;
        }

        RoiPanel.SelectedExternalRoi = fallback;
    }

    private void SetStep(int index, PipelineStepStatus status)
    {
        Steps[index].Status = status;
    }

    private void ResetFromStep(int startIndex)
    {
        for (var index = startIndex; index < Steps.Count; index++)
        {
            Steps[index].Status = PipelineStepStatus.Pending;
        }
    }

    private void MarkActiveStepFailed()
    {
        var active = Steps.FirstOrDefault(step => step.Status == PipelineStepStatus.Running);
        if (active is not null)
        {
            active.Status = PipelineStepStatus.Failed;
        }
    }

    private bool CanAttachElectrodes()
        => artifacts.ExternalSurface is not null &&
           electrodeRepository.Placements.Count > 0 &&
           !StatusBar.IsBusy;

    private void AddElectrodeLayer()
    {
        var layer = ElectrodeLayerPresets.CreateBlankLayer(ElectrodePanel.Spec.Layers.Count);
        ElectrodePanel.Spec.Layers.Add(layer);
        ElectrodePanel.SelectedLayer = layer;
        RefreshCommandStates();
    }

    private void RemoveElectrodeLayer(ElectrodeLayer? layer)
    {
        if (layer is null || ElectrodePanel.Spec.Layers.Count <= 1)
        {
            return;
        }

        ElectrodePanel.Spec.Layers.Remove(layer);
        RefreshCommandStates();
    }

    private void MoveLayer(ElectrodeLayer? layer, int direction)
    {
        if (layer is null)
        {
            return;
        }

        var layers = ElectrodePanel.Spec.Layers;
        var index = layers.IndexOf(layer);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= layers.Count)
        {
            return;
        }

        layers.Move(index, target);
        ElectrodePanel.SelectedLayer = layer;
        RefreshCommandStates();
    }

    private void UpdatePlacementAndAttachmentSteps()
    {
        if (artifacts.ExternalSurface is null)
        {
            SetStep(4, PipelineStepStatus.Pending);
            SetStep(5, PipelineStepStatus.Pending);
            return;
        }

        SetStep(4, electrodeRepository.Placements.Count > 0 ? PipelineStepStatus.Completed : PipelineStepStatus.Ready);
        if (artifacts.AttachmentResult is not null)
        {
            SetStep(5, PipelineStepStatus.Completed);
            SetStep(6, PipelineStepStatus.Ready);
        }
        else
        {
            SetStep(5, electrodeRepository.Placements.Count > 0 ? PipelineStepStatus.Ready : PipelineStepStatus.Pending);
        }
    }

    private void UpdateAttachmentStage(string detail)
    {
        var normalized = detail.ToLowerInvariant();
        if (normalized.Contains("align"))
        {
            Attachment.Status = DeformationStepStatus.Aligning;
        }
        else if (normalized.Contains("contact") || normalized.Contains("patch") || normalized.Contains("constraint"))
        {
            Attachment.Status = DeformationStepStatus.Extracting;
        }
        else if (normalized.Contains("deform"))
        {
            Attachment.Status = DeformationStepStatus.Deforming;
        }
        else if (normalized.Contains("valid"))
        {
            Attachment.Status = DeformationStepStatus.Validating;
        }
    }

    private void InvalidateAttachment(string summary)
    {
        artifacts.AttachmentResult = null;
        artifacts.OriginalBodySurfaceBackup = null;
        artifacts.DeformedBodySurface = null;
        viewportService.ClearAttachmentVisuals();
        Attachment.Reset(summary);
        InvalidateVolumeMesh("Attachment changed. Regenerate the volume mesh.");
    }

    private PipelineStats BuildStats()
    {
        return new PipelineStats
        {
            VisibleRoiCount = artifacts.SurfaceMeshes.Count(mesh => mesh.Roi.Visibility),
            Vertices = artifacts.SurfaceMeshes.Sum(mesh => mesh.VertexCount),
            Triangles = artifacts.SurfaceMeshes.Sum(mesh => mesh.TriangleCount),
            ElectrodeCount = electrodeRepository.Placements.Count
        };
    }

    private void RefreshCommandStates()
    {
        Notify(BrowseCtSeriesCommand);
        Notify(BrowseVolumeFileCommand);
        Notify(BrowseRtssCommand);
        Notify(ImportCommand);
        Notify(BuildLabelMapCommand);
        Notify(BuildSurfaceCommand);
        Notify(AttachElectrodesCommand);
        Notify(RemovePlacementCommand);
        Notify(ToggleContactPatchCommand);
        Notify(ToggleBodyViewCommand);
        Notify(ToggleViewportPickCommand);
        Notify(CancelCommand);
        Notify(ResetCameraCommand);
        Notify(GenerateVolumeMeshCommand);
        Notify(ExportStlCommand);
        Notify(ExportPolyCommand);
        Notify(AddElectrodeLayerCommand);
        Notify(RemoveElectrodeLayerCommand);
        Notify(MoveLayerUpCommand);
        Notify(MoveLayerDownCommand);
        Notify(ResetElectrodeLayersCommand);
    }

    private static void Notify(ICommand command)
    {
        switch (command)
        {
            case RelayCommand relay:
                relay.NotifyCanExecuteChanged();
                break;
            case AsyncRelayCommand asyncRelay:
                asyncRelay.NotifyCanExecuteChanged();
                break;
            case RelayCommand<ElectrodeLayer> layerRelay:
                layerRelay.NotifyCanExecuteChanged();
                break;
        }
    }

    private static double ParseDouble(string text, double fallback)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static string ResolveInitialDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Environment.CurrentDirectory;
        }

        if (Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : Environment.CurrentDirectory;
    }
}
