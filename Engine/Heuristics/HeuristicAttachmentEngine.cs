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
        var session = new ElasticEngineSession
        {
            OriginalBodyMesh = bodyMesh.DeepCopy(),
            PreprocessedBodyMesh = bodyMesh.DeepCopy(),
            ElectrodeMesh = electrodeMesh.DeepCopy()
        };

        // Determine Approach Direction (points from electrode towards the body)
        var undersideCap = ElectrodeSampling.GetUndersideCap(electrodeMesh, bodyMesh);
        var electrodeApproachDirection = undersideCap.CapNormal;
        var outwardDirection = -electrodeApproachDirection;

        // --- Step: Initial Safe Placement ---
        // Push the electrode completely outside the body before searching for correspondences.
        // We find the maximum penetration depth against the body surface using the outward direction.
        var maxPenetration = 0.0f;
        var bodyTree = new KdTree(bodyMesh.Vertices);
        foreach (var v in electrodeMesh.Vertices)
        {
            var nearestId = bodyTree.FindNearest(v);
            if (nearestId >= 0)
            {
                var bodyVertex = bodyMesh.Vertices[nearestId];
                var bodyNormal = bodyMesh.HasNormals ? bodyMesh.Normals![nearestId] : Vector3.UnitZ;
                bodyNormal = bodyNormal.LengthSquared() <= 1e-8f ? Vector3.UnitZ : Vector3.Normalize(bodyNormal);
                
                // Signed distance > 0 means the point is inside the body (penetrating).
                // We assume bodyNormal points OUTWARD into the air.
                var surfaceToPoint = v - bodyVertex;
                var signedDistance = Vector3.Dot(surfaceToPoint, bodyNormal);
                
                // If signedDistance is negative, the point is inside (penetrating).
                if (signedDistance < 0)
                {
                    maxPenetration = Math.Max(maxPenetration, -signedDistance);
                }
            }
        }

        // Apply outward translation if penetrated
        if (maxPenetration > 0)
        {
            var pushVector = outwardDirection * (maxPenetration + parameters.MinimumClearanceMm);
            for (var i = 0; i < electrodeMesh.Vertices.Count; i++)
            {
                electrodeMesh.Vertices[i] += pushVector;
            }
        }

        // Now sample the bottom after it has been safely placed outside
        var samples = sampler.Sample(electrodeMesh, bodyMesh);

        var hits = correspondenceService.FindCorrespondences(samples, bodyMesh, electrodeApproachDirection);

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
        var patch = patchExtractor.Extract(bodyMesh, alignedElectrode, patchOptions);

        var config = new ElasticEngineConfig
        {
            UseLaplacian = true,
            HardConstraintWeight = parameters.HardWeight,
            SoftConstraintWeight = parameters.SoftWeight,
            SoftConstraintRings = parameters.ExpansionRings
        };

        var constraints = constraintBuilder.Build(bodyMesh, alignedElectrode, patch, new List<int>(), config);
        
        var deformedBody = surfaceDeformer.Deform(bodyMesh, constraints, config);

        var finalReport = qualityEvaluator.Evaluate(alignedElectrode, deformedBody, transformedSamples);

        session.AlignedElectrodeMesh = alignedElectrode;
        session.DeformedBodyMesh = deformedBody;
        session.ContactPatch = patch;
        // Since we don't return constraints directly from the pipeline yet, we can populate them from what we built
        session.Constraints.AddRange(constraints);
        // Map the new AttachmentQualityReport to the old DeformationQualityReport for compatibility
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
