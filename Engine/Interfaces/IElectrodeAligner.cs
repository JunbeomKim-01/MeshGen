using MeshGenLite.Models;

namespace MeshGenLite.Engine.Interfaces;

/// <summary>
/// Performs rigid pre-alignment of an electrode mesh onto the body surface.
/// </summary>
public interface IElectrodeAligner
{
    MeshData Align(MeshData bodyMesh, MeshData electrodeMesh, ElectrodeAlignmentOptions options);
}
