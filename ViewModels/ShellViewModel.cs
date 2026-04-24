using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using Kitware.VTK;
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

    public ObservableCollection<RoiDescriptor> Rois { get; } = new();

    public IEnumerable<RoiDescriptor> ExternalCandidates => Rois.Where(roi => roi.HasContours);

    public RoiDescriptor? SelectedExternalRoi
    {
        get => selectedExternalRoi;
        set => SetProperty(ref selectedExternalRoi, value);
    }

    public void RefreshExternalCandidates() => OnPropertyChanged(nameof(ExternalCandidates));
}

public sealed class ElectrodePanelViewModel : ObservableObject
{
    private string colorHex = "#60CDFF";
    private PlacementMode placementMode;
    private ElectrodePlacement? selectedPlacement;

    public ElectrodePanelViewModel(ObservableCollection<ElectrodePlacement> placements)
    {
        Placements = placements;
    }

    public ElectrodeSpec Spec { get; } = new();

    public ObservableCollection<ElectrodePlacement> Placements { get; }

    public string ColorHex
    {
        get => colorHex;
        set => SetProperty(ref colorHex, value);
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
    private readonly VtkViewportService viewportService;
    private readonly MeshHitTestService meshHitTestService;
    private readonly ElectrodeFactory electrodeFactory;
    private readonly ElectrodeTransformBuilder transformBuilder;
    private readonly ElectrodeRepository electrodeRepository;
    private readonly ElectrodePlacementController placementController;
    private readonly Progress<PipelineProgress> progress;
    private CancellationTokenSource? currentCts;
    private StudyContext? study;
    private readonly PipelineArtifacts artifacts = new();
    private string volumeMeshSummary = "Step 7 tetrahedralization is intentionally deferred in this MVP.";

    public ShellViewModel(IPipelineService pipelineService, VtkViewportService viewportService)
    {
        this.pipelineService = pipelineService;
        this.viewportService = viewportService;
        meshHitTestService = new MeshHitTestService(viewportService);
        electrodeFactory = new ElectrodeFactory();
        transformBuilder = new ElectrodeTransformBuilder();
        electrodeRepository = new ElectrodeRepository();
        placementController = new ElectrodePlacementController(meshHitTestService, electrodeFactory, transformBuilder, viewportService);
        progress = new Progress<PipelineProgress>(OnProgressReported);

        Import = new ImportViewModel();
        RoiPanel = new RoiPanelViewModel();
        ElectrodePanel = new ElectrodePanelViewModel(electrodeRepository.Placements);
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
            new("6", "Review"),
            new("7", "Volume")
        };
        Steps[0].Status = PipelineStepStatus.Ready;

        BrowseCtSeriesCommand = new RelayCommand(BrowseCtSeries);
        BrowseVolumeFileCommand = new RelayCommand(BrowseVolumeFile);
        BrowseRtssCommand = new RelayCommand(BrowseRtss);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !StatusBar.IsBusy);
        BuildLabelMapCommand = new AsyncRelayCommand(BuildLabelMapAsync, () => study is not null && !StatusBar.IsBusy);
        BuildSurfaceCommand = new AsyncRelayCommand(BuildSurfaceAsync, () => artifacts.LabelMap is not null && !StatusBar.IsBusy);
        RemovePlacementCommand = new RelayCommand(RemovePlacement, () => ElectrodePanel.SelectedPlacement is not null);
        ToggleViewportPickCommand = new RelayCommand(TogglePlacementMode, () => !StatusBar.IsBusy);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => StatusBar.IsBusy);
        ResetCameraCommand = new RelayCommand(() => viewportService.ResetCamera(), () => viewportService.IsInitialized);
    }

    public ImportViewModel Import { get; }

    public RoiPanelViewModel RoiPanel { get; }

    public ElectrodePanelViewModel ElectrodePanel { get; }

    public StatusBarViewModel StatusBar { get; }

    public ObservableCollection<PipelineStepViewModel> Steps { get; }

    public ICommand BrowseCtSeriesCommand { get; }

    public ICommand BrowseVolumeFileCommand { get; }

    public ICommand BrowseRtssCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand BuildLabelMapCommand { get; }

    public ICommand BuildSurfaceCommand { get; }

    public ICommand RemovePlacementCommand { get; }

    public ICommand ToggleViewportPickCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand ResetCameraCommand { get; }

    public string VolumeMeshSummary
    {
        get => volumeMeshSummary;
        set => SetProperty(ref volumeMeshSummary, value);
    }

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

        ApplyElectrodeColor();
        if (placementController.UpdatePreview(x, y, ElectrodePanel.Spec, ElectrodePanel.Spec.Color))
        {
            var hit = placementController.CurrentHit;
            if (hit is not null)
            {
                StatusBar.Message = "Placement preview";
                StatusBar.Detail = $"Cell {hit.CellId} · {hit.Position.X:F1}, {hit.Position.Y:F1}, {hit.Position.Z:F1}";
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

        ApplyElectrodeColor();
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

        ElectrodePanel.SelectedPlacement = placement;
        artifacts.Stats = BuildStats();
        StatusBar.HudText = artifacts.Stats.Summary;
        StatusBar.Message = "Electrode placed";
        StatusBar.Detail = $"{placement.WorldSummary} · {placement.CellSummary}";
        SetStep(4, PipelineStepStatus.Completed);
        SetStep(5, PipelineStepStatus.Completed);
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
            Import.Summary = $"{study.SourceKind} · {sourceSummary} · spacing {study.Spacing[0]:F2}/{study.Spacing[1]:F2}/{study.Spacing[2]:F2} mm · ROI {study.Rois.Count(roi => roi.HasContours)}";
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
            VolumeMeshSummary = "Label map ready. Step 7 tetrahedralization is intentionally deferred in this MVP.";
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

            SetStep(2, PipelineStepStatus.Running);
            artifacts.SurfaceMeshes.Clear();
            var surfaces = await pipelineService.BuildSurfaceAsync(study, artifacts.LabelMap, RoiPanel.Rois.ToList(), progress, token);
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
        }
        catch (Exception exception)
        {
            MarkActiveStepFailed();
            StatusBar.Message = "Operation failed";
            StatusBar.Detail = exception.Message;
            VolumeMeshSummary = exception.Message;
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
        }
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
        artifacts.Stats = BuildStats();
        StatusBar.HudText = artifacts.Stats.Summary;
        StatusBar.Message = "Electrode removed";
        StatusBar.Detail = "The selected electrode was deleted.";
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

        ApplyElectrodeColor();
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

    private void ApplyElectrodeColor()
    {
        if (WpfColorConverter.ConvertFromString(ElectrodePanel.ColorHex) is WpfColor color)
        {
            ElectrodePanel.Spec.Color = color;
        }
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
        Notify(RemovePlacementCommand);
        Notify(ToggleViewportPickCommand);
        Notify(CancelCommand);
        Notify(ResetCameraCommand);
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
