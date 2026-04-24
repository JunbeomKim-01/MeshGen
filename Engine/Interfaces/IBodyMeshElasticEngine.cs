using MeshGenLite.Models;

namespace MeshGenLite.Engine.Interfaces;

/// <summary>
/// Facade that orchestrates the full elastic deformation pipeline:
/// preprocess → align → extract contact → build constraints → deform → validate.
/// </summary>
public interface IBodyMeshElasticEngine
{
    void SetBodyMesh(MeshData bodyMesh);

    void SetElectrodeMesh(MeshData electrodeMesh);

    void Configure(ElasticEngineConfig config);

    Task PreprocessBodyMeshAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    void SetPreprocessedBodyMesh(MeshData mesh);

    Task AlignElectrodeRigidAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task ExtractContactPatchAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task BuildConstraintsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task DeformSurfaceAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task ValidateResultAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    ElasticEngineSession GetSession();

    MeshData? GetDeformedBodyMesh();

    ContactPatch? GetContactPatch();

    DeformationQualityReport? GetQualityReport();
}
