using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Kitware.VTK;
using MeshGenLite.Infrastructure;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace MeshGenLite.Models;

public enum PipelineStepStatus
{
    Pending,
    Ready,
    Running,
    Completed,
    Failed
}

public enum DeformationStepStatus
{
    Idle,
    Aligning,
    Extracting,
    Deforming,
    Validating,
    Done
}

public enum RoiRole
{
    Unknown,
    External,
    Tissue
}

public enum ImportSourceKind
{
    DicomSeries,
    NiftiLabelMap
}

public enum PlacementMode
{
    None,
    ElectrodePlacement
}

public sealed class StudyImportRequest
{
    public string CtSeriesPath { get; init; } = string.Empty;

    public IReadOnlyList<string> NiftiPaths { get; init; } = Array.Empty<string>();

    public string RtssPath { get; init; } = string.Empty;

    public double TargetSpacing { get; init; }
}

public sealed class CtSliceInfo
{
    public string Path { get; init; } = string.Empty;

    public Point3D ImagePosition { get; init; }

    public Vector3D Normal { get; init; }

    public double Projection { get; init; }
}

public sealed class RoiContourData
{
    public IReadOnlyList<Point3D> Points { get; init; } = Array.Empty<Point3D>();

    public double Z { get; init; }
}

public sealed class StudyContext
{
    public string CtSeriesPath { get; init; } = string.Empty;

    public IReadOnlyList<string> SourcePaths { get; init; } = Array.Empty<string>();

    public string RtssPath { get; init; } = string.Empty;

    public double[] Origin { get; init; } = Array.Empty<double>();

    public double[] Spacing { get; init; } = Array.Empty<double>();

    public int[] Dimensions { get; init; } = Array.Empty<int>();

    public double[] Bounds { get; init; } = Array.Empty<double>();

    public IReadOnlyList<CtSliceInfo> Slices { get; init; } = Array.Empty<CtSliceInfo>();

    public ObservableCollection<RoiDescriptor> Rois { get; init; } = new();

    public int SliceCount => Slices.Count;

    public int? ExternalRoiId { get; set; }

    public ImportSourceKind SourceKind { get; init; }

    public vtkImageData? NativeLabelMap { get; init; }
}

public sealed class RoiDescriptor : ObservableObject
{
    private bool visibility = true;
    private bool isSelected = true;
    private double opacity = 1.0;
    private WpfColor color = Colors.White;
    private RoiRole role;

    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool HasContours { get; init; }

    public List<RoiContourData> Contours { get; init; } = new();

    public RoiRole Role
    {
        get => role;
        set => SetProperty(ref role, value);
    }

    public bool Visibility
    {
        get => visibility;
        set => SetProperty(ref visibility, value);
    }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public double Opacity
    {
        get => opacity;
        set => SetProperty(ref opacity, value);
    }

    public WpfColor Color
    {
        get => color;
        set
        {
            if (SetProperty(ref color, value))
            {
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(ColorHex));
            }
        }
    }

    public SolidColorBrush ColorBrush => new(Color);

    public string ColorHex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
}

public sealed class ElectrodeSpec : ObservableObject
{
    private ElectrodeShape shape = ElectrodeShape.Circle;

    public ElectrodeSpec()
    {
        foreach (var layer in ElectrodeLayerPresets.CreateDefault())
        {
            Layers.Add(layer);
        }

        Layers.CollectionChanged += (_, _) => RaiseDerivedChanged();
    }

    public ElectrodeShape Shape
    {
        get => shape;
        set
        {
            if (SetProperty(ref shape, value))
            {
                RaiseDerivedChanged();
            }
        }
    }

    public ObservableCollection<ElectrodeLayer> Layers { get; } = new();

    public bool IsCircle => Shape == ElectrodeShape.Circle;
    public bool IsRectangle => Shape == ElectrodeShape.Rectangle;
    public bool IsTriangle => Shape == ElectrodeShape.Triangle;
    public bool IsRadiusVisible => Shape != ElectrodeShape.Rectangle;

    public double TotalThickness => Layers.Sum(layer => layer.Thickness);

    public double FootprintRadius
    {
        get
        {
            if (Layers.Count == 0)
            {
                return 0.0;
            }

            return Shape switch
            {
                ElectrodeShape.Rectangle => Math.Max(
                    Layers.Max(layer => layer.Width),
                    Layers.Max(layer => layer.Depth)) / 2.0,
                ElectrodeShape.Triangle => Layers.Max(layer => layer.Radius),
                _ => Layers.Max(layer => layer.Radius)
            };
        }
    }

    // The "skin contact" layer is index 0; its color is what users see at the placement footprint.
    public WpfColor PrimaryColor => Layers.Count > 0 ? Layers[0].Color : Colors.LightSkyBlue;

    public SolidColorBrush ColorBrush => new(PrimaryColor);

    public string ColorHex => $"#{PrimaryColor.R:X2}{PrimaryColor.G:X2}{PrimaryColor.B:X2}";

    // Legacy aliases used by older repository / pipeline code.
    public double Radius => FootprintRadius;

    public double Height => TotalThickness;

    public WpfColor Color => PrimaryColor;

    public void ResetToDefault()
    {
        Layers.Clear();
        foreach (var layer in ElectrodeLayerPresets.CreateDefault())
        {
            Layers.Add(layer);
        }
    }

    private void RaiseDerivedChanged()
    {
        OnPropertyChanged(nameof(IsCircle));
        OnPropertyChanged(nameof(IsRectangle));
        OnPropertyChanged(nameof(IsTriangle));
        OnPropertyChanged(nameof(IsRadiusVisible));
        OnPropertyChanged(nameof(TotalThickness));
        OnPropertyChanged(nameof(FootprintRadius));
        OnPropertyChanged(nameof(PrimaryColor));
        OnPropertyChanged(nameof(ColorBrush));
        OnPropertyChanged(nameof(ColorHex));
        OnPropertyChanged(nameof(Radius));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(Color));
    }
}

public sealed class MeshHitResult
{
    public bool Success { get; init; }

    public Point3D Position { get; init; }

    public Vector3D Normal { get; init; }

    public long CellId { get; init; }
}

public sealed class ElectrodePlacement : ObservableObject
{
    private Point3D surfacePoint;
    private Point3D worldPosition;
    private Vector3D worldNormal = new(0, 0, 1);

    public int Id { get; init; }

    public int? BodyMeshId { get; init; }

    public long PickedCellId { get; init; }

    public double Length { get; init; }

    public double Radius { get; init; }

    public double Clearance { get; init; }

    public Point3D SurfacePoint
    {
        get => surfacePoint;
        set
        {
            if (SetProperty(ref surfacePoint, value))
            {
                OnPropertyChanged(nameof(SurfaceSummary));
                OnPropertyChanged(nameof(RequestedPoint));
                OnPropertyChanged(nameof(RequestedSummary));
            }
        }
    }

    public Point3D WorldPosition
    {
        get => worldPosition;
        set
        {
            if (SetProperty(ref worldPosition, value))
            {
                OnPropertyChanged(nameof(WorldSummary));
                OnPropertyChanged(nameof(SnappedPoint));
                OnPropertyChanged(nameof(SnappedSummary));
            }
        }
    }

    public Vector3D WorldNormal
    {
        get => worldNormal;
        set
        {
            if (SetProperty(ref worldNormal, value))
            {
                OnPropertyChanged(nameof(NormalSummary));
                OnPropertyChanged(nameof(Normal));
            }
        }
    }

    // Legacy aliases kept so older pipeline code still compiles.
    public Point3D RequestedPoint
    {
        get => SurfacePoint;
        set => SurfacePoint = value;
    }

    public Point3D SnappedPoint
    {
        get => WorldPosition;
        set => WorldPosition = value;
    }

    public Vector3D Normal
    {
        get => WorldNormal;
        set => WorldNormal = value;
    }

    public string SurfaceSummary => $"{SurfacePoint.X:F1}, {SurfacePoint.Y:F1}, {SurfacePoint.Z:F1}";

    public string WorldSummary => $"{WorldPosition.X:F1}, {WorldPosition.Y:F1}, {WorldPosition.Z:F1}";

    public string RequestedSummary => SurfaceSummary;

    public string SnappedSummary => WorldSummary;

    public string NormalSummary => $"{WorldNormal.X:F2}, {WorldNormal.Y:F2}, {WorldNormal.Z:F2}";

    public string GeometrySummary => $"L {Length:F1} mm · R {Radius:F1} mm · C {Clearance:F1} mm";

    public string CellSummary => $"Cell {PickedCellId}";
}

public sealed class SurfaceMeshInfo
{
    public RoiDescriptor Roi { get; init; } = new();

    public vtkPolyData PolyData { get; init; } = vtkPolyData.New();

    public int VertexCount { get; init; }

    public int TriangleCount { get; init; }
}

public sealed class PipelineStats
{
    public int VisibleRoiCount { get; init; }

    public int Vertices { get; init; }

    public int Triangles { get; init; }

    public int ElectrodeCount { get; init; }

    public string Summary =>
        $"ROI {VisibleRoiCount} · Vertices {Vertices:N0} · Triangles {Triangles:N0} · Electrodes {ElectrodeCount}";
}

public sealed class PipelineArtifacts
{
    public vtkImageData? LabelMap { get; set; }

    public List<SurfaceMeshInfo> SurfaceMeshes { get; } = new();

    public vtkPolyData? ExternalSurface { get; set; }

    public List<vtkPolyData> ElectrodeMeshes { get; } = new();

    public vtkPolyData? MergedPreview { get; set; }

    public vtkPolyData? OriginalBodySurfaceBackup { get; set; }

    public vtkPolyData? DeformedBodySurface { get; set; }

    public ElasticAttachmentResult? AttachmentResult { get; set; }

    public PipelineStats Stats { get; set; } = new();
}

public sealed class PipelineProgress
{
    public string Message { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}
