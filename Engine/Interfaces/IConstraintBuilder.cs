using MeshGenLite.Models;

namespace MeshGenLite.Engine.Interfaces;

/// <summary>
/// Builds solver constraints from the contact patch, anchors, and configuration.
/// </summary>
public interface IConstraintBuilder
{
    IReadOnlyList<ConstraintPoint> Build(
        MeshData bodyMesh,
        MeshData alignedElectrodeMesh,
        ContactPatch contactPatch,
        IReadOnlyList<int> anchorVertexIds,
        ElasticEngineConfig config);
}
