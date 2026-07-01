using System.ComponentModel;
using MeshGenLite.Integration.Mcp.Bridge;
using MeshGenLite.Integration.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace MeshGenLite.Integration.Mcp.Tools;

[McpServerToolType]
public static class RoiTools
{
    [McpServerTool(Name = "list_rois"), Description(
        "Return all ROIs in the currently loaded study, including each ROI's role (External/Tissue), " +
        "selection state, and whether it has contours/voxels.")]
    public static async Task<string> ListRois(
        IMeshGenCommandBridge bridge,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ListRoisAsync(cancellationToken);
        return PipelineTools.Serialize(result);
    }

    [McpServerTool(Name = "set_external_roi"), Description(
        "Mark the given ROI as the External (body skin) ROI. This determines the body surface " +
        "used for electrode placement and attachment. The ROI must have contours/voxels.")]
    public static async Task<string> SetExternalRoi(
        IMeshGenCommandBridge bridge,
        [Description("Numeric ID of the ROI (as returned by list_rois).")]
        int roiId,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.SetExternalRoiAsync(new SetExternalRoiRequest { RoiId = roiId }, cancellationToken);
        return PipelineTools.Serialize(result);
    }
}
