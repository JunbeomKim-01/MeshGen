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

    private readonly object vtkLock = new();

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
        var result = new ElasticAttachmentResult
        {
            OriginalBodyMesh = originalBodyMesh.DeepCopy()
        };

        progress?.Report(new PipelineProgress { Message = "Preparing global context", Detail = "Building KD-Tree and Topology" });
        var bodyTree = new KdTree(originalBodyMesh.Vertices);
        var bodyTopology = new MeshTopology(originalBodyMesh);
        
        var heuristicParams = new MeshGenLite.Models.AttachmentParameters
        {
            ContactThresholdMm = (float)config.ContactDistanceThresholdMm,
            ExpansionRings = config.ContactExpansionRing,
            HardWeight = (float)config.HardConstraintWeight,
            SoftWeight = (float)config.SoftConstraintWeight
        };

        // --- Phase 1: Compute Constraints in Parallel ---
        progress?.Report(new PipelineProgress { Message = "Analyzing placements", Detail = $"Processing {placements.Count} electrodes in parallel" });
        
        var sessions = placements
            .AsParallel()
            .WithCancellation(cancellationToken)
            .Select(placement =>
            {
                // Instantiate engine inside the loop for thread isolation
                var threadEngine = new MeshGenLite.Engine.Heuristics.HeuristicAttachmentEngine();
                var electrodeMesh = BuildElectrodeMesh(spec, placement);
                var session = threadEngine.ComputeConstraints(originalBodyMesh, bodyTree, bodyTopology, electrodeMesh, heuristicParams);
                session.ElectrodeId = placement.Id;
                return session;
            })
            .ToList();

        // --- Phase 2: Merge Constraints and Deform Body in Single Pass ---
        progress?.Report(new PipelineProgress { Message = "Applying deformation", Detail = "Solving Laplacian system for all constraints" });
        
        // Optimization: Downsample constraints if too many are generated per electrode
        // This keeps the solver fast even with high-resolution electrode meshes.
        const int MaxConstraintsPerElectrode = 300;
        var allConstraints = new List<ConstraintPoint>();
        foreach (var session in sessions)
        {
            if (session.Constraints.Count <= MaxConstraintsPerElectrode)
            {
                allConstraints.AddRange(session.Constraints);
            }
            else
            {
                // Simple stride-based downsampling for performance
                int stride = (int)Math.Ceiling((double)session.Constraints.Count / MaxConstraintsPerElectrode);
                for (int i = 0; i < session.Constraints.Count; i += stride)
                {
                    allConstraints.Add(session.Constraints[i]);
                }
            }
        }

        var surfaceDeformer = new MeshGenLite.Engine.LaplacianSurfaceDeformer();
        var deformedBody = surfaceDeformer.Deform(originalBodyMesh, bodyTopology, allConstraints, config);
        
        // --- Phase 3: Finalize and Quality Check in Parallel ---
        progress?.Report(new PipelineProgress { Message = "Finalizing attachment", Detail = "Resolving residual penetrations and evaluating quality" });
        
        var unionPatch = new HashSet<int>();
        var finalSessions = sessions
            .AsParallel()
            .WithCancellation(cancellationToken)
            .Select(session =>
            {
                var threadEngine = new MeshGenLite.Engine.Heuristics.HeuristicAttachmentEngine();
                var finalized = threadEngine.FinalizeAttachment(session, deformedBody, heuristicParams);
                return finalized;
            })
            .ToList();

        foreach (var session in finalSessions)
        {
            result.PerElectrodeSessions.Add(session);
            result.DeformedElectrodeMeshes[session.ElectrodeId] = session.AlignedElectrodeMesh?.DeepCopy() ?? session.ElectrodeMesh;
            
            if (session.ContactPatch is not null)
            {
                foreach (var vId in session.ContactPatch.VertexIds) unionPatch.Add(vId);
            }
        }

        result.FinalBodyMesh = deformedBody;
        result.UnionContactPatchVertexIds.AddRange(unionPatch.OrderBy(id => id));
        result.AggregateQualityReport = DeformationQualityReport.AggregateWorstCase(finalSessions.Select(s => s.QualityReport).Where(r => r != null).ToList()!);

        progress?.Report(new PipelineProgress
        {
            Message = "Attachment complete",
            Detail = result.AggregateQualityReport?.Summary ?? "External surface indented by electrodes"
        });

        return result;
    }

    private MeshData BuildElectrodeMesh(ElectrodeSpec spec, ElectrodePlacement placement)
    {
        lock (vtkLock)
        {
            var localMesh = electrodeFactory.CreateNeedle(spec);
            var transform = transformBuilder.BuildTransform(placement.SurfacePoint, placement.WorldNormal, placement.Clearance);

            var filter = vtkTransformPolyDataFilter.New();
            filter.SetInputData(localMesh);
            filter.SetTransform(transform);
            filter.Update();

            var worldMesh = vtkPolyData.New();
            worldMesh.DeepCopy(filter.GetOutput());
            var normal = placement.WorldNormal;
            if (normal.LengthSquared < 1e-8)
            {
                normal = new System.Windows.Media.Media3D.Vector3D(0, 0, 1);
            }
            normal.Normalize();
            var basePoint = placement.SurfacePoint + (normal * placement.Clearance);
            FemMetadataService.TagElectrodeSurface(worldMesh, placement.Id, spec, basePoint, normal);
            return MeshConverter.FromVtkPolyData(worldMesh);
        }
    }

    private static Vector3 ToVector3(System.Windows.Media.Media3D.Vector3D vector)
        => new((float)vector.X, (float)vector.Y, (float)vector.Z);
}
