using Kitware.VTK;
using MeshGenLite.Models;
using MeshGenLite.Services;

namespace MeshGenLite.Integration.Mcp;

/// <summary>
/// Holds the live MeshGen pipeline state (study, surfaces, label maps, electrodes,
/// attachment results) shared between the WPF view-model and the MCP bridge.
/// In headless --mcp mode this is the only state container; in WPF mode the
/// view-model still drives interactive flows but this service mirrors the data
/// for state-query MCP resources.
/// </summary>
public sealed class SceneStateService
{
    private readonly object stateLock = new();

    public StudyContext? Study { get; set; }

    public PipelineArtifacts Artifacts { get; } = new();

    public ElectrodeRepository Electrodes { get; } = new();

    public Dictionary<int, vtkPolyData> ElectrodeWorldMeshes { get; } = new();

    public ElectrodeSpec DefaultElectrodeSpec { get; } = new();

    public VolumeMeshResult? VolumeMesh { get; set; }

    public object Lock => stateLock;

    public bool HasStudy
    {
        get
        {
            lock (stateLock)
            {
                return Study is not null;
            }
        }
    }

    public bool HasLabelMap
    {
        get
        {
            lock (stateLock)
            {
                return Artifacts.LabelMap is not null;
            }
        }
    }

    public int SurfaceMeshCount
    {
        get
        {
            lock (stateLock)
            {
                return Artifacts.SurfaceMeshes.Count;
            }
        }
    }

    public bool AttachmentCompleted
    {
        get
        {
            lock (stateLock)
            {
                return Artifacts.AttachmentResult is not null;
            }
        }
    }

    public void ResetStudy(StudyContext study)
    {
        lock (stateLock)
        {
            Study = study;
            Artifacts.LabelMap = null;
            Artifacts.SurfaceMeshes.Clear();
            Artifacts.ExternalSurface = null;
            Artifacts.MergedPreview = null;
            Artifacts.OriginalBodySurfaceBackup = null;
            Artifacts.DeformedBodySurface = null;
            Artifacts.AttachmentResult = null;
            Artifacts.ElectrodeMeshes.Clear();
            Electrodes.Clear();
            ElectrodeWorldMeshes.Clear();
            VolumeMesh = null;
        }
    }

    public void InvalidateAttachment()
    {
        lock (stateLock)
        {
            Artifacts.AttachmentResult = null;
            Artifacts.OriginalBodySurfaceBackup = null;
            Artifacts.DeformedBodySurface = null;
            VolumeMesh = null;
        }
    }
}
