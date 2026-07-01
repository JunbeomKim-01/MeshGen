using System.ComponentModel;
using MeshGenLite.Integration.Mcp.Bridge;
using ModelContextProtocol.Server;

namespace MeshGenLite.Integration.Mcp.Tools;

/// <summary>
/// Read-only status accessor. Modeled as a tool so it works with every MCP
/// client (some clients still don't fully support resource subscriptions).
/// Once Slice 2 lands we'll also expose this same payload as a true MCP
/// Resource at uri "meshgen://pipeline/status".
/// </summary>
[McpServerToolType]
public static class StatusTools
{
    [McpServerTool(Name = "get_pipeline_status"), Description(
        "Return a snapshot of MeshGen's current pipeline state: whether a study is loaded, " +
        "ROI count, external ROI, label map / surface mesh / attachment / volume mesh status.")]
    public static async Task<string> GetPipelineStatus(
        IMeshGenCommandBridge bridge,
        CancellationToken cancellationToken = default)
    {
        var status = await bridge.GetPipelineStatusAsync(cancellationToken);
        return PipelineTools.Serialize(status);
    }
}
