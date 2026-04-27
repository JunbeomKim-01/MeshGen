using System.Numerics;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;

namespace MeshGenLite.Engine.Heuristics;

public sealed class HeuristicAttachmentEngine
{
    private readonly ElectrodeBottomSampler sampler = new();
    private readonly SurfaceCorrespondenceService correspondenceService = new();
    private readonly ElectrodePoseOptimizer poseOptimizer = new();
    private readonly AttachmentQualityEvaluator qualityEvaluator = new();
    private readonly LaplacianSurfaceDeformer surfaceDeformer = new();
    private readonly ConstraintBuilder constraintBuilder = new();

    public ElasticEngineSession Attach(
        MeshData bodyMesh, 
        MeshData electrodeMesh, 
        AttachmentParameters parameters)
    {
        var bodyTree = new KdTree(bodyMesh.Vertices);
        var bodyTopology = new MeshTopology(bodyMesh);

        var session = ComputeConstraints(bodyMesh, bodyTree, bodyTopology, electrodeMesh, parameters);
        
        var config = new ElasticEngineConfig
        {
            UseLaplacian = true,
            HardConstraintWeight = parameters.HardWeight,
            SoftConstraintWeight = parameters.SoftWeight,
            SoftConstraintRings = parameters.ExpansionRings
        };

        session.DeformedBodyMesh = surfaceDeformer.Deform(bodyMesh, bodyTopology, session.Constraints, config);
        
        return FinalizeAttachment(session, session.DeformedBodyMesh, parameters);
    }

    public ElasticEngineSession ComputeConstraints(
        MeshData bodyMesh,
        KdTree bodyTree,
        MeshTopology bodyTopology,
        MeshData electrodeMesh,
        AttachmentParameters parameters)
    {
        var session = new ElasticEngineSession
        {
            OriginalBodyMesh = bodyMesh,
            PreprocessedBodyMesh = bodyMesh,
            ElectrodeMesh = electrodeMesh.DeepCopy()
        };

        // Determine Approach Direction (points from electrode towards the body)
        var undersideCap = ElectrodeSampling.GetUndersideCap(electrodeMesh, bodyMesh, bodyTree);
        var electrodeApproachDirection = undersideCap.CapNormal;
        var outwardDirection = -electrodeApproachDirection;

        // --- Step: Initial Safe Placement ---
        var maxPenetration = 0.0f;
        foreach (var v in electrodeMesh.Vertices)
        {
            var nearestId = bodyTree.FindNearest(v);
            if (nearestId >= 0)
            {
                var bodyVertex = bodyMesh.Vertices[nearestId];
                var bodyNormal = bodyMesh.HasNormals ? bodyMesh.Normals![nearestId] : Vector3.UnitZ;
                bodyNormal = bodyNormal.LengthSquared() <= 1e-8f ? Vector3.UnitZ : Vector3.Normalize(bodyNormal);
                
                var surfaceToPoint = v - bodyVertex;
                var signedDistance = Vector3.Dot(surfaceToPoint, bodyNormal);
                if (signedDistance < 0)
                {
                    maxPenetration = Math.Max(maxPenetration, -signedDistance);
                }
            }
        }

        if (maxPenetration > 0)
        {
            var pushVector = outwardDirection * (maxPenetration + parameters.MinimumClearanceMm);
            for (var i = 0; i < electrodeMesh.Vertices.Count; i++)
            {
                electrodeMesh.Vertices[i] += pushVector;
            }
        }

        var samples = sampler.Sample(electrodeMesh, bodyMesh, bodyTree);
        var hits = correspondenceService.FindCorrespondences(samples, bodyMesh, bodyTree, electrodeApproachDirection);
        var transform = poseOptimizer.OptimizePose(samples, hits);

        var alignedElectrode = TransformMesh(electrodeMesh, transform);
        var transformedSamples = new List<ElectrodeSamplePoint>();
        foreach (var sample in samples)
        {
            transformedSamples.Add(new ElectrodeSamplePoint
            {
                SampleId = sample.SampleId,
                LocalPosition = sample.LocalPosition,
                WorldPosition = Vector3.Transform(sample.LocalPosition, transform),
                IsBoundary = sample.IsBoundary
            });
        }

        var patchExtractor = new ContactPatchExtractor();
        var patchOptions = new ContactPatchOptions 
        { 
            DistanceThresholdMm = parameters.ContactThresholdMm, 
            ExpansionRings = parameters.ExpansionRings 
        };
        var patch = patchExtractor.Extract(bodyMesh, bodyTree, bodyTopology, alignedElectrode, patchOptions);

        var config = new ElasticEngineConfig
        {
            UseLaplacian = true,
            HardConstraintWeight = parameters.HardWeight,
            SoftConstraintWeight = parameters.SoftWeight,
            SoftConstraintRings = parameters.ExpansionRings
        };

        var constraints = constraintBuilder.Build(bodyMesh, bodyTopology, alignedElectrode, patch, new List<int>(), config);

        session.AlignedElectrodeMesh = alignedElectrode;
        session.ContactPatch = patch;
        session.Constraints.AddRange(constraints);
        session.InternalData["TransformedSamples"] = transformedSamples;
        session.InternalData["OutwardDirection"] = outwardDirection;

        return session;
    }

    public ElasticEngineSession FinalizeAttachment(
        ElasticEngineSession session,
        MeshData deformedBody,
        AttachmentParameters parameters)
    {
        var alignedElectrode = session.AlignedElectrodeMesh!;
        var outwardDirection = (Vector3)session.InternalData["OutwardDirection"];
        var transformedSamples = (List<ElectrodeSamplePoint>)session.InternalData["TransformedSamples"];

        // --- Step: Final Penetration Resolution ---
        var finalMaxPenetration = 0.0f;
        var deformedBodyTree = new KdTree(deformedBody.Vertices);
        
        foreach (var v in alignedElectrode.Vertices)
        {
            var nearestId = deformedBodyTree.FindNearest(v);
            if (nearestId >= 0)
            {
                var bodyVertex = deformedBody.Vertices[nearestId];
                var bodyNormal = deformedBody.HasNormals ? deformedBody.Normals![nearestId] : Vector3.UnitZ;
                bodyNormal = bodyNormal.LengthSquared() <= 1e-8f ? Vector3.UnitZ : Vector3.Normalize(bodyNormal);
                
                var signedDistance = Vector3.Dot(v - bodyVertex, bodyNormal);
                if (signedDistance < 0)
                {
                    finalMaxPenetration = Math.Max(finalMaxPenetration, -signedDistance);
                }
            }
        }

        if (finalMaxPenetration > 0)
        {
            var finalPush = outwardDirection * (finalMaxPenetration + parameters.MinimumClearanceMm);
            for (var i = 0; i < alignedElectrode.Vertices.Count; i++)
            {
                alignedElectrode.Vertices[i] += finalPush;
            }
        }

        var finalReport = qualityEvaluator.Evaluate(alignedElectrode, deformedBody, transformedSamples);

        session.DeformedBodyMesh = deformedBody;
        session.QualityReport = new DeformationQualityReport
        {
            MaxPenetrationDepth = finalReport.MaxPenetrationMm,
            MeanResidualGap = finalReport.MeanGapMm,
            HasPenetration = !finalReport.Passed
        };

        return session;
    }

    private MeshData TransformMesh(MeshData mesh, Matrix4x4 transform)
    {
        var result = mesh.DeepCopy();
        for (var i = 0; i < result.Vertices.Count; i++)
        {
            result.Vertices[i] = Vector3.Transform(mesh.Vertices[i], transform);
        }
        
        if (result.HasNormals)
        {
            Matrix4x4.Invert(transform, out var inverted);
            var normalTransform = Matrix4x4.Transpose(inverted);
            for (var i = 0; i < result.Normals!.Count; i++)
            {
                var n = Vector3.TransformNormal(result.Normals[i], normalTransform);
                result.Normals[i] = n.LengthSquared() > 1e-8f ? Vector3.Normalize(n) : Vector3.UnitZ;
            }
        }
        return result;
    }
}
