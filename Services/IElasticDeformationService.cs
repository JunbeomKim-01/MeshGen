using Kitware.VTK;
using MeshGenLite.Models;

namespace MeshGenLite.Services;

public interface IElasticDeformationService
{
    Task<ElasticAttachmentResult> AttachAsync(
        vtkPolyData bodySurface,
        ElectrodeSpec spec,
        IReadOnlyList<ElectrodePlacement> placements,
        ElasticEngineConfig? config,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}
