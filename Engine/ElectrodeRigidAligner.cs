using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Engine.Interfaces;
using MeshGenLite.Models;

namespace MeshGenLite.Engine;

public sealed class ElectrodeRigidAligner : IElectrodeAligner
{
    public MeshData Align(MeshData bodyMesh, MeshData electrodeMesh, ElectrodeAlignmentOptions options)
    {
        var aligned = electrodeMesh.DeepCopy();
        if (aligned.Vertices.Count == 0 || bodyMesh.Vertices.Count == 0)
        {
            return aligned;
        }

        var underside = ElectrodeSampling.GetUndersideCap(aligned, bodyMesh);
        var bodyTree = new KdTree(bodyMesh.Vertices);
        var nearestId = bodyTree.FindNearest(underside.Centroid);
        if (nearestId < 0)
        {
            return aligned;
        }

        var bodyNormal = bodyMesh.HasNormals
            ? bodyMesh.Normals![nearestId]
            : Vector3.UnitZ;
        if (bodyNormal.LengthSquared() <= 1e-8f)
        {
            bodyNormal = Vector3.UnitZ;
        }

        bodyNormal = Vector3.Normalize(bodyNormal);
        var desiredUndersideCentroid = bodyMesh.Vertices[nearestId] + (bodyNormal * (float)options.ClearanceMm);
        var translation = desiredUndersideCentroid - underside.Centroid;
        for (var index = 0; index < aligned.Vertices.Count; index++)
        {
            aligned.Vertices[index] += translation;
        }

        if (aligned.HasNormals)
        {
            for (var index = 0; index < aligned.Normals!.Count; index++)
            {
                if (aligned.Normals[index].LengthSquared() > 1e-8f)
                {
                    aligned.Normals[index] = Vector3.Normalize(aligned.Normals[index]);
                }
            }
        }

        return aligned;
    }
}
