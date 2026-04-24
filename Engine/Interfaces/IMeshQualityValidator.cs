using MeshGenLite.Models;

namespace MeshGenLite.Engine.Interfaces;

/// <summary>
/// Validates the quality of a deformed mesh against the original and electrode geometry.
/// </summary>
public interface IMeshQualityValidator
{
    DeformationQualityReport Validate(
        MeshData originalMesh,
        MeshData deformedMesh,
        MeshData electrodeMesh);
}
