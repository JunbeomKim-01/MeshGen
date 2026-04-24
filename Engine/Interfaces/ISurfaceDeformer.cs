using MeshGenLite.Models;

namespace MeshGenLite.Engine.Interfaces;

/// <summary>
/// Applies surface deformation to a body mesh given a set of constraints.
/// </summary>
public interface ISurfaceDeformer
{
    MeshData Deform(
        MeshData bodyMesh,
        IReadOnlyList<ConstraintPoint> constraints,
        ElasticEngineConfig config);
}
