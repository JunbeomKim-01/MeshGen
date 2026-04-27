using System.Numerics;
using Kitware.VTK;
using MeshGenLite.Engine;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;

namespace MeshGenLite.Services;

public sealed class ElasticDeformationService : IElasticDeformationService
{
    private readonly ElectrodeFactory electrodeFactory;
    private readonly ElectrodeTransformBuilder transformBuilder;

    public ElasticDeformationService()
        : this(new ElectrodeFactory(), new ElectrodeTransformBuilder())
    {
    }

    public ElasticDeformationService(ElectrodeFactory electrodeFactory, ElectrodeTransformBuilder transformBuilder)
    {
        this.electrodeFactory = electrodeFactory;
        this.transformBuilder = transformBuilder;
    }

    public async Task<ElasticAttachmentResult> AttachAsync(
        vtkPolyData bodySurface,
        ElectrodeSpec spec,
        IReadOnlyList<ElectrodePlacement> placements,
        ElasticEngineConfig? config,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bodyMesh = MeshConverter.FromVtkPolyData(bodySurface);
        var placementSnapshot = placements.OrderBy(placement => placement.Id).ToList();
        return await Task.Run(
            () => AttachCore(bodyMesh, spec, placementSnapshot, config, progress, cancellationToken),
            cancellationToken);
    }

    private ElasticAttachmentResult AttachCore(
        MeshData originalBodyMesh,
        ElectrodeSpec spec,
        IReadOnlyList<ElectrodePlacement> placements,
        ElasticEngineConfig? config,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (placements.Count == 0)
        {
            throw new InvalidOperationException("At least one electrode placement is required for attachment.");
        }

        config ??= new ElasticEngineConfig();
        var bodyMesh = originalBodyMesh.DeepCopy();
        var result = new ElasticAttachmentResult
        {
            OriginalBodyMesh = originalBodyMesh.DeepCopy()
        };
        var unionPatch = new HashSet<int>();
        var reports = new List<DeformationQualityReport>();
        var heuristicEngine = new MeshGenLite.Engine.Heuristics.HeuristicAttachmentEngine();
        
        foreach (var placement in placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PipelineProgress
            {
                Message = $"Pressing electrode {placement.Id}",
                Detail = "Building electrode mesh"
            });

            var bodyBeforePass = bodyMesh.DeepCopy();
            var electrodeMesh = BuildElectrodeMesh(spec, placement);
            
            progress?.Report(new PipelineProgress
            {
                Message = $"Pressing electrode {placement.Id}",
                Detail = "Applying Heuristic Attachment Algorithm"
            });

            // Convert ElasticEngineConfig to AttachmentParameters
            var heuristicParams = new MeshGenLite.Models.AttachmentParameters
            {
                ContactThresholdMm = (float)config.ContactDistanceThresholdMm,
                ExpansionRings = config.ContactExpansionRing,
                HardWeight = (float)config.HardConstraintWeight,
                SoftWeight = (float)config.SoftConstraintWeight
            };

            var session = heuristicEngine.Attach(bodyMesh, electrodeMesh, heuristicParams);
            session.ElectrodeId = placement.Id;
            bodyMesh = session.DeformedBodyMesh ?? bodyMesh;
            
            result.PerElectrodeSessions.Add(session);
            result.DeformedElectrodeMeshes[placement.Id] = session.AlignedElectrodeMesh?.DeepCopy() ?? electrodeMesh;
            if (session.ContactPatch is not null)
            {
                foreach (var vertexId in session.ContactPatch.VertexIds)
                {
                    unionPatch.Add(vertexId);
                }
            }

            if (session.QualityReport is not null)
            {
                reports.Add(session.QualityReport);
            }
        }

        result.FinalBodyMesh = bodyMesh.DeepCopy();
        result.UnionContactPatchVertexIds.AddRange(unionPatch.OrderBy(id => id));
        result.AggregateQualityReport = DeformationQualityReport.AggregateWorstCase(reports);

        progress?.Report(new PipelineProgress
        {
            Message = "Attachment complete",
            Detail = result.AggregateQualityReport?.Summary ?? "External surface indented by electrodes"
        });

        return result;
    }

    private MeshData BuildElectrodeMesh(ElectrodeSpec spec, ElectrodePlacement placement)
    {
        var localMesh = electrodeFactory.CreateNeedle(spec);
        var transform = transformBuilder.BuildTransform(placement.SurfacePoint, placement.WorldNormal, placement.Clearance);

        var filter = vtkTransformPolyDataFilter.New();
        filter.SetInputData(localMesh);
        filter.SetTransform(transform);
        filter.Update();

        var worldMesh = vtkPolyData.New();
        worldMesh.DeepCopy(filter.GetOutput());
        return MeshConverter.FromVtkPolyData(worldMesh);
    }

    private static Vector3 ToVector3(System.Windows.Media.Media3D.Vector3D vector)
        => new((float)vector.X, (float)vector.Y, (float)vector.Z);
}
