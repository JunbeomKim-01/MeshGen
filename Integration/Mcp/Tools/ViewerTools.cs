using System.ComponentModel;
using MeshGenLite.Integration.Mcp.Bridge;
using MeshGenLite.Integration.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace MeshGenLite.Integration.Mcp.Tools;

[McpServerToolType]
public static class ViewerTools
{
    [McpServerTool(Name = "show_mesh_window"), Description(
        "Open a native VTK 3D window to preview the current mesh scene. " +
        "This window pops up on the server machine's display.")]
    public static async Task<string> ShowMeshWindow(
        IMeshGenCommandBridge bridge,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ShowMeshWindowAsync(cancellationToken);
        return PipelineTools.Serialize(result);
    }
}
