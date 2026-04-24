using MeshGenLite.Models;

namespace MeshGenLite.Engine.Interfaces;

/// <summary>
/// Responsible for cleaning and preparing a body mesh before deformation.
/// </summary>
public interface IBodyMeshPreprocessor
{
    MeshData Clean(MeshData mesh);

    MeshData Smooth(MeshData mesh, int iterations, double lambda);

    MeshData RecomputeNormals(MeshData mesh);

    bool ValidateTopology(MeshData mesh);
}
