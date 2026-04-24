using Kitware.VTK;
using MeshGenLite.Engine;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
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
            async () => await AttachCoreAsync(bodyMesh, spec, placementSnapshot, config, progress, cancellationToken),
            cancellationToken);
    }

    private async Task<ElasticAttachmentResult> AttachCoreAsync(
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
        var currentBodyMesh = originalBodyMesh.DeepCopy();
        var result = new ElasticAttachmentResult
        {
            OriginalBodyMesh = originalBodyMesh.DeepCopy()
        };
        var unionPatch = new HashSet<int>();
        var reports = new List<DeformationQualityReport>();
        var isFirstElectrode = true;

        foreach (var placement in placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PipelineProgress
            {
                Message = $"Attaching electrode {placement.Id}",
                Detail = "Preprocess"
            });

            var electrodeMesh = BuildElectrodeMesh(spec, placement);
            var engine = CreateEngine();
            engine.Configure(config);
            engine.SetBodyMesh(currentBodyMesh);
            engine.SetElectrodeMesh(electrodeMesh);

            var stageProgress = new Progress<string>(detail =>
            {
                progress?.Report(new PipelineProgress
                {
                    Message = $"Attaching electrode {placement.Id}",
                    Detail = detail
                });
            });

            if (isFirstElectrode)
            {
                await engine.PreprocessBodyMeshAsync(stageProgress, cancellationToken);
                isFirstElectrode = false;
            }
            else
            {
                ((IProgress<string>)stageProgress).Report("Reusing preprocessed body mesh");
                engine.SetPreprocessedBodyMesh(currentBodyMesh);
            }
            await engine.AlignElectrodeRigidAsync(stageProgress, cancellationToken);
            await engine.ExtractContactPatchAsync(stageProgress, cancellationToken);
            await engine.BuildConstraintsAsync(stageProgress, cancellationToken);
            await engine.DeformSurfaceAsync(stageProgress, cancellationToken);
            await engine.ValidateResultAsync(stageProgress, cancellationToken);

            var session = engine.GetSession();
            session.ElectrodeId = placement.Id;
            result.PerElectrodeSessions.Add(session);
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

            currentBodyMesh = session.DeformedBodyMesh?.DeepCopy() ?? currentBodyMesh;
        }

        result.FinalBodyMesh = currentBodyMesh;
        result.UnionContactPatchVertexIds.AddRange(unionPatch.OrderBy(id => id));
        result.AggregateQualityReport = DeformationQualityReport.AggregateWorstCase(reports);

        progress?.Report(new PipelineProgress
        {
            Message = "Attachment complete",
            Detail = result.AggregateQualityReport?.Summary ?? "No quality report"
        });

        return result;
    }

    private IBodyMeshElasticEngine CreateEngine()
    {
        return new BodyMeshElasticEngine(
            new BodyMeshPreprocessor(),
            new ElectrodeRigidAligner(),
            new ContactPatchExtractor(),
            new ConstraintBuilder(),
            new LaplacianSurfaceDeformer(),
            new MeshQualityValidator());
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
}
