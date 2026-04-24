using MeshGenLite.Models;

namespace MeshGenLite.Engine.Interfaces;

/// <summary>
/// Extracts the local region of the body mesh that contacts an electrode.
/// </summary>
public interface IContactPatchExtractor
{
    ContactPatch Extract(MeshData bodyMesh, MeshData electrodeMesh, ContactPatchOptions options);
}
