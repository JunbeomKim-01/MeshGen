using System.Numerics;
using MeshGenLite.Engine;
using MeshGenLite.Engine.Geometry;
using MeshGenLite.Models;
using Xunit;

namespace MeshGenLite.Tests;

public sealed class ElasticEngineTests
{
    [Fact]
    public void KdTree_FindsNearestAndRadiusMatches()
    {
        var points = new[]
        {
            new Vector3(0, 0, 0),
            new Vector3(5, 0, 0),
            new Vector3(1, 1, 0)
        };

        var tree = new KdTree(points);

        var nearest = tree.FindNearest(new Vector3(0.8f, 0.9f, 0));
        var within = tree.FindWithinRadius(new Vector3(0.8f, 0.9f, 0), 1.0);

        Assert.Equal(2, nearest);
        Assert.Contains(2, within);
    }

    [Fact]
    public void MeshTopology_ExpandRingsIncludesExpectedNeighbors()
    {
        var mesh = CreatePlaneMesh(4, 4, 1.0f);
        var topology = new MeshTopology(mesh);

        var expanded = topology.ExpandRings(new[] { 5 }, 1);

        Assert.Contains(5, expanded);
        Assert.Contains(1, expanded);
        Assert.Contains(4, expanded);
        Assert.Contains(6, expanded);
        Assert.Contains(9, expanded);
    }

    [Fact]
    public void ContactPatchExtractor_FindsPlaneVerticesBelowElectrode()
    {
        var body = CreatePlaneMesh(8, 8, 1.0f);
        var electrode = CreateCylinderElectrodeMesh(radius: 1.6f, height: 1.0f, segments: 24, center: new Vector3(3.5f, 3.5f, 0.8f));
        var extractor = new ContactPatchExtractor();

        var patch = extractor.Extract(body, electrode, new ContactPatchOptions
        {
            DistanceThresholdMm = 1.0,
            ExpansionRings = 1
        });

        Assert.NotEmpty(patch.VertexIds);
        Assert.NotEmpty(patch.BoundaryVertexIds);
    }

    [Fact]
    public void ConstraintBuilder_CreatesHardSoftAndAnchorConstraints()
    {
        var body = CreatePlaneMesh(7, 7, 1.0f);
        var electrode = CreateCylinderElectrodeMesh(radius: 1.5f, height: 1.0f, segments: 24, center: new Vector3(3.0f, 3.0f, 0.7f));
        var patch = new ContactPatch();
        patch.VertexIds.AddRange(new[] { 16, 17, 23, 24 });
        patch.BoundaryVertexIds.AddRange(new[] { 16, 17, 23, 24 });
        var anchors = new[] { 0, 6, 42, 48 };
        var builder = new ConstraintBuilder();

        var constraints = builder.Build(body, electrode, patch, anchors, new ElasticEngineConfig());

        Assert.Contains(constraints, constraint => constraint.IsHardConstraint && patch.VertexIds.Contains(constraint.VertexId));
        Assert.Contains(constraints, constraint => !constraint.IsHardConstraint);
        Assert.Contains(constraints, constraint => anchors.Contains(constraint.VertexId));
    }

    [Fact]
    public void LaplacianSurfaceDeformer_MovesConstrainedVertexTowardTarget()
    {
        var body = CreatePlaneMesh(5, 5, 1.0f);
        var deformer = new LaplacianSurfaceDeformer();
        var centerVertexId = 12;
        var constraints = new List<ConstraintPoint>
        {
            new()
            {
                VertexId = centerVertexId,
                TargetPosition = body.Vertices[centerVertexId] + new Vector3(0, 0, 1.0f),
                Weight = 50.0,
                IsHardConstraint = true
            },
            new()
            {
                VertexId = 0,
                TargetPosition = body.Vertices[0],
                Weight = 100.0,
                IsHardConstraint = true
            },
            new()
            {
                VertexId = 4,
                TargetPosition = body.Vertices[4],
                Weight = 100.0,
                IsHardConstraint = true
            },
            new()
            {
                VertexId = 20,
                TargetPosition = body.Vertices[20],
                Weight = 100.0,
                IsHardConstraint = true
            },
            new()
            {
                VertexId = 24,
                TargetPosition = body.Vertices[24],
                Weight = 100.0,
                IsHardConstraint = true
            }
        };

        var deformed = deformer.Deform(body, constraints, new ElasticEngineConfig());

        Assert.True(deformed.Vertices[centerVertexId].Z > 0.4f);
    }

    [Fact]
    public async Task BodyMeshElasticEngine_RunsEndToEndAndProducesQualityReport()
    {
        var body = CreatePlaneMesh(10, 10, 1.0f);
        var electrode = CreateCylinderElectrodeMesh(radius: 1.8f, height: 1.0f, segments: 32, center: new Vector3(4.5f, 4.5f, 0.7f));
        var engine = new BodyMeshElasticEngine(
            new BodyMeshPreprocessor(),
            new ElectrodeRigidAligner(),
            new ContactPatchExtractor(),
            new ConstraintBuilder(),
            new LaplacianSurfaceDeformer(),
            new MeshQualityValidator());
        engine.Configure(new ElasticEngineConfig
        {
            SmoothingIterations = 1,
            ContactDistanceThresholdMm = 1.0,
            ContactExpansionRing = 1,
            SoftConstraintRings = 1
        });
        engine.SetBodyMesh(body);
        engine.SetElectrodeMesh(electrode);

        await engine.PreprocessBodyMeshAsync();
        await engine.AlignElectrodeRigidAsync();
        await engine.ExtractContactPatchAsync();
        await engine.BuildConstraintsAsync();
        await engine.DeformSurfaceAsync();
        await engine.ValidateResultAsync();

        var session = engine.GetSession();
        Assert.NotNull(session.ContactPatch);
        Assert.NotEmpty(session.ContactPatch!.VertexIds);
        Assert.NotNull(session.DeformedBodyMesh);
        Assert.NotNull(session.QualityReport);
        Assert.True(session.QualityReport!.MaxDisplacement > 0.0);
    }

    [Fact]
    public async Task BodyMeshElasticEngine_PreservesBodyVertexCountWhenCleaningDisconnectedVertices()
    {
        var body = CreatePlaneMesh(10, 10, 1.0f);
        body.Vertices.Add(new Vector3(100, 100, 100));
        body.Vertices.Add(new Vector3(101, 100, 100));
        body.Vertices.Add(new Vector3(100, 101, 100));
        var isolatedStart = body.Vertices.Count - 3;
        body.Indices.Add(isolatedStart);
        body.Indices.Add(isolatedStart + 1);
        body.Indices.Add(isolatedStart + 2);
        body.Normals = Enumerable.Repeat(Vector3.UnitZ, body.Vertices.Count).ToList();

        var electrode = CreateCylinderElectrodeMesh(radius: 1.8f, height: 1.0f, segments: 32, center: new Vector3(4.5f, 4.5f, 0.7f));
        var engine = new BodyMeshElasticEngine(
            new BodyMeshPreprocessor(),
            new ElectrodeRigidAligner(),
            new ContactPatchExtractor(),
            new ConstraintBuilder(),
            new LaplacianSurfaceDeformer(),
            new MeshQualityValidator());
        engine.Configure(new ElasticEngineConfig
        {
            SmoothingIterations = 1,
            ContactDistanceThresholdMm = 1.0,
            ContactExpansionRing = 1,
            SoftConstraintRings = 1
        });
        engine.SetBodyMesh(body);
        engine.SetElectrodeMesh(electrode);

        await engine.PreprocessBodyMeshAsync();
        await engine.AlignElectrodeRigidAsync();
        await engine.ExtractContactPatchAsync();
        await engine.BuildConstraintsAsync();
        await engine.DeformSurfaceAsync();
        await engine.ValidateResultAsync();

        var session = engine.GetSession();
        Assert.Equal(body.Vertices.Count, session.PreprocessedBodyMesh!.Vertices.Count);
        Assert.Equal(body.Vertices.Count, session.DeformedBodyMesh!.Vertices.Count);
        Assert.NotNull(session.QualityReport);
    }

    [Fact]
    public void ElectrodeSurfaceConformer_PressesElectrodeToPlaneWithoutMovingBody()
    {
        var body = CreatePlaneMesh(8, 8, 1.0f);
        var bodyBefore = body.Vertices.ToArray();
        var electrode = CreateCylinderElectrodeMesh(
            radius: 1.4f,
            height: 1.0f,
            segments: 32,
            center: new Vector3(3.5f, 3.5f, 0.75f));

        var conformer = new ElectrodeSurfaceConformer();
        var result = conformer.Conform(body, electrode, new ElasticEngineConfig
        {
            ElectrodeAttachmentClearanceMm = 0.0
        });
        var originalThickness = electrode.Vertices.Max(vertex => vertex.Z) - electrode.Vertices.Min(vertex => vertex.Z);
        var attachedThickness = result.ElectrodeMesh.Vertices.Max(vertex => vertex.Z) - result.ElectrodeMesh.Vertices.Min(vertex => vertex.Z);

        Assert.Equal(bodyBefore, body.Vertices);
        Assert.True(electrode.Vertices.Min(vertex => vertex.Z) > 0.2f);
        Assert.True(result.ElectrodeMesh.Vertices.Min(vertex => vertex.Z) < 0.05f);
        Assert.True(result.ElectrodeMesh.Vertices.Max(vertex => vertex.Z) < 1.05f);
        Assert.True(Math.Abs(attachedThickness - originalThickness) < 0.05f);
        Assert.True(result.QualityReport.MaxDisplacement > 0.2);
        Assert.True(result.QualityReport.MeanResidualGap < 0.05);
    }

    [Fact]
    public void BodySurfaceIndentationDeformer_PressesPatchInward()
    {
        var body = CreatePlaneMesh(9, 9, 1.0f);
        var electrode = CreateCylinderElectrodeMesh(
            radius: 1.4f,
            height: 1.0f,
            segments: 32,
            center: new Vector3(4.0f, 4.0f, 0.5f));
        var patch = new ContactPatch();
        patch.VertexIds.AddRange(new[] { 30, 31, 32, 39, 40, 41, 48, 49, 50 });
        patch.BoundaryVertexIds.AddRange(new[] { 30, 31, 32, 39, 41, 48, 49, 50 });
        var deformer = new BodySurfaceIndentationDeformer();

        var result = deformer.Indent(body, electrode, patch, Vector3.UnitZ, new ElasticEngineConfig
        {
            ExternalIndentationDepthMm = 0.75,
            HardConstraintWeight = 80.0,
            SoftConstraintWeight = 5.0,
            AnchorConstraintWeight = 100.0,
            SoftConstraintRings = 1,
            AnchorMinRingDistance = 3,
            MaxAnchorCount = 32
        });

        Assert.True(result.DeformedBodyMesh.Vertices[40].Z < -0.35f);
        Assert.True(Math.Abs(result.DeformedBodyMesh.Vertices[0].Z) < 0.05f);
        Assert.True(result.QualityReport.MaxDisplacement > 0.3);
    }

    private static MeshData CreatePlaneMesh(int rows, int columns, float spacing)
    {
        var mesh = new MeshData();
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                mesh.Vertices.Add(new Vector3(column * spacing, row * spacing, 0.0f));
            }
        }

        for (var row = 0; row < rows - 1; row++)
        {
            for (var column = 0; column < columns - 1; column++)
            {
                var topLeft = (row * columns) + column;
                var topRight = topLeft + 1;
                var bottomLeft = topLeft + columns;
                var bottomRight = bottomLeft + 1;

                mesh.Indices.Add(topLeft);
                mesh.Indices.Add(bottomLeft);
                mesh.Indices.Add(topRight);

                mesh.Indices.Add(topRight);
                mesh.Indices.Add(bottomLeft);
                mesh.Indices.Add(bottomRight);
            }
        }

        mesh.Normals = Enumerable.Repeat(Vector3.UnitZ, mesh.Vertices.Count).ToList();
        return mesh;
    }

    private static MeshData CreateCylinderElectrodeMesh(float radius, float height, int segments, Vector3 center)
    {
        var mesh = new MeshData();
        var halfHeight = height * 0.5f;
        var bottomCenterIndex = 0;
        var topCenterIndex = 1;
        mesh.Vertices.Add(center + new Vector3(0, 0, -halfHeight));
        mesh.Vertices.Add(center + new Vector3(0, 0, halfHeight));

        for (var segment = 0; segment < segments; segment++)
        {
            var angle = MathF.PI * 2.0f * segment / segments;
            var x = MathF.Cos(angle) * radius;
            var y = MathF.Sin(angle) * radius;
            mesh.Vertices.Add(center + new Vector3(x, y, -halfHeight));
            mesh.Vertices.Add(center + new Vector3(x, y, halfHeight));
        }

        for (var segment = 0; segment < segments; segment++)
        {
            var next = (segment + 1) % segments;
            var bottomA = 2 + (segment * 2);
            var topA = bottomA + 1;
            var bottomB = 2 + (next * 2);
            var topB = bottomB + 1;

            mesh.Indices.Add(bottomCenterIndex);
            mesh.Indices.Add(bottomB);
            mesh.Indices.Add(bottomA);

            mesh.Indices.Add(topCenterIndex);
            mesh.Indices.Add(topA);
            mesh.Indices.Add(topB);

            mesh.Indices.Add(bottomA);
            mesh.Indices.Add(bottomB);
            mesh.Indices.Add(topA);

            mesh.Indices.Add(topA);
            mesh.Indices.Add(bottomB);
            mesh.Indices.Add(topB);
        }

        return mesh;
    }
}
