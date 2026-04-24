using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class BodyMeshElasticEngine : IBodyMeshElasticEngine
{
    private readonly IBodyMeshPreprocessor preprocessor;
    private readonly IElectrodeAligner aligner;
    private readonly IContactPatchExtractor patchExtractor;
    private readonly IConstraintBuilder constraintBuilder;
    private readonly ISurfaceDeformer deformer;
    private readonly IMeshQualityValidator qualityValidator;
    private readonly ElasticEngineSession session = new();
    private ElasticEngineConfig config = new();

    public BodyMeshElasticEngine(
        IBodyMeshPreprocessor preprocessor,
        IElectrodeAligner aligner,
        IContactPatchExtractor patchExtractor,
        IConstraintBuilder constraintBuilder,
        ISurfaceDeformer deformer,
        IMeshQualityValidator qualityValidator)
    {
        this.preprocessor = preprocessor;
        this.aligner = aligner;
        this.patchExtractor = patchExtractor;
        this.constraintBuilder = constraintBuilder;
        this.deformer = deformer;
        this.qualityValidator = qualityValidator;
    }

    public void SetBodyMesh(MeshData bodyMesh)
    {
        session.OriginalBodyMesh = bodyMesh.DeepCopy();
    }

    public void SetElectrodeMesh(MeshData electrodeMesh)
    {
        session.ElectrodeMesh = electrodeMesh.DeepCopy();
    }

    public void Configure(ElasticEngineConfig config)
    {
        this.config = config;
    }

    public Task PreprocessBodyMeshAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Preprocessing body mesh");
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBodyMesh();
        var cleaned = preprocessor.Clean(session.OriginalBodyMesh!);
        var smoothed = preprocessor.Smooth(cleaned, config.SmoothingIterations, config.SmoothingLambda);
        session.PreprocessedBodyMesh = preprocessor.RecomputeNormals(smoothed);
        if (!preprocessor.ValidateTopology(session.PreprocessedBodyMesh))
        {
            throw new InvalidOperationException("Body mesh topology is invalid after preprocessing.");
        }

        return Task.CompletedTask;
    }

    public void SetPreprocessedBodyMesh(MeshData mesh)
    {
        session.OriginalBodyMesh = mesh.DeepCopy();
        session.PreprocessedBodyMesh = mesh.HasNormals ? mesh.DeepCopy() : preprocessor.RecomputeNormals(mesh.DeepCopy());
    }

    public Task AlignElectrodeRigidAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Aligning electrode");
        cancellationToken.ThrowIfCancellationRequested();
        EnsurePreprocessedMeshes();
        session.AlignedElectrodeMesh = aligner.Align(
            session.PreprocessedBodyMesh!,
            session.ElectrodeMesh!,
            new ElectrodeAlignmentOptions { ClearanceMm = config.AlignmentClearanceMm });
        return Task.CompletedTask;
    }

    public Task ExtractContactPatchAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Extracting contact patch");
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAlignedMeshes();
        session.ContactPatch = patchExtractor.Extract(
            session.PreprocessedBodyMesh!,
            session.AlignedElectrodeMesh!,
            new ContactPatchOptions
            {
                DistanceThresholdMm = config.ContactDistanceThresholdMm,
                ExpansionRings = config.ContactExpansionRing
            });
        return Task.CompletedTask;
    }

    public Task BuildConstraintsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Building constraints");
        cancellationToken.ThrowIfCancellationRequested();
        EnsureContactPatch();
        session.AnchorVertexIds.Clear();
        session.AnchorVertexIds.AddRange(SelectAnchorVertices());
        session.Constraints.Clear();
        session.Constraints.AddRange(constraintBuilder.Build(
            session.PreprocessedBodyMesh!,
            session.AlignedElectrodeMesh!,
            session.ContactPatch!,
            session.AnchorVertexIds,
            config));
        return Task.CompletedTask;
    }

    public Task DeformSurfaceAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Deforming surface");
        cancellationToken.ThrowIfCancellationRequested();
        EnsureConstraints();
        session.DeformedBodyMesh = deformer.Deform(
            session.PreprocessedBodyMesh!,
            session.Constraints,
            config);
        return Task.CompletedTask;
    }

    public Task ValidateResultAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Validating deformation");
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDeformed();
        session.QualityReport = qualityValidator.Validate(
            session.OriginalBodyMesh!,
            session.DeformedBodyMesh!,
            session.AlignedElectrodeMesh!);
        return Task.CompletedTask;
    }

    public ElasticEngineSession GetSession() => session;

    public MeshData? GetDeformedBodyMesh() => session.DeformedBodyMesh;

    public ContactPatch? GetContactPatch() => session.ContactPatch;

    public DeformationQualityReport? GetQualityReport() => session.QualityReport;

    private IReadOnlyList<int> SelectAnchorVertices()
    {
        var topology = new MeshTopology(session.PreprocessedBodyMesh!);
        var anchors = topology.GetDistantVertices(
            session.ContactPatch!.VertexIds,
            session.PreprocessedBodyMesh!.Vertices.Count,
            config.AnchorMinRingDistance);

        if (anchors.Count <= config.MaxAnchorCount)
        {
            return anchors;
        }

        var sampled = new List<int>(config.MaxAnchorCount);
        var step = anchors.Count / (double)config.MaxAnchorCount;
        for (var sample = 0; sample < config.MaxAnchorCount; sample++)
        {
            var index = (int)Math.Round(sample * step);
            index = Math.Clamp(index, 0, anchors.Count - 1);
            sampled.Add(anchors[index]);
        }

        return sampled.Distinct().ToList();
    }

    private void EnsureBodyMesh()
    {
        if (session.OriginalBodyMesh is null)
        {
            throw new InvalidOperationException("Body mesh is not set.");
        }
    }

    private void EnsurePreprocessedMeshes()
    {
        EnsureBodyMesh();
        if (session.PreprocessedBodyMesh is null || session.ElectrodeMesh is null)
        {
            throw new InvalidOperationException("Meshes are not ready for alignment.");
        }
    }

    private void EnsureAlignedMeshes()
    {
        EnsurePreprocessedMeshes();
        if (session.AlignedElectrodeMesh is null)
        {
            throw new InvalidOperationException("Electrode mesh is not aligned.");
        }
    }

    private void EnsureContactPatch()
    {
        EnsureAlignedMeshes();
        if (session.ContactPatch is null || session.ContactPatch.IsEmpty)
        {
            throw new InvalidOperationException("Contact patch extraction produced no vertices.");
        }
    }

    private void EnsureConstraints()
    {
        EnsureContactPatch();
        if (session.Constraints.Count == 0)
        {
            throw new InvalidOperationException("No deformation constraints are available.");
        }
    }

    private void EnsureDeformed()
    {
        if (session.DeformedBodyMesh is null)
        {
            throw new InvalidOperationException("Surface deformation has not been executed.");
        }
    }
}
