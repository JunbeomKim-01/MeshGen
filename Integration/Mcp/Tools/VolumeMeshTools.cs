using System.ComponentModel;
using MeshGenLite.Integration.Mcp.Bridge;
using MeshGenLite.Integration.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace MeshGenLite.Integration.Mcp.Tools;

[McpServerToolType]
public static class VolumeMeshTools
{
    [McpServerTool(Name = "generate_volume_mesh"), Description(
        "Generate a tetrahedral volume mesh from all active surface meshes (External, Organs, Electrodes). " +
        "Produces a PLC (Piecewise Linear Complex) and runs TetGen.")]
    public static async Task<string> GenerateVolumeMesh(
        IMeshGenCommandBridge bridge,
        [Description("Alpha radius (mm) for surface filtering. 0 = unfiltered. Default 0.0.")]
        double alphaMm = 0.0,
        [Description("Maximum input point count. Default 3000.")]
        int maxPoints = 3000,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.GenerateVolumeMeshAsync(new GenerateVolumeMeshRequest
        {
            AlphaMm = alphaMm,
            MaxPoints = maxPoints
        }, cancellationToken);
        return PipelineTools.Serialize(result);
    }
}
