using System.ComponentModel;
using MeshGenLite.Integration.Mcp.Bridge;
using MeshGenLite.Integration.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace MeshGenLite.Integration.Mcp.Tools;

[McpServerToolType]
public static class AttachmentTools
{
    [McpServerTool(Name = "attach_electrodes_batch"), Description(
        "Run the elastic deformation engine to attach all current electrodes to the External ROI surface. " +
        "This deforms the electrode geometry to match the body curvature and ensures a clean contact patch.")]
    public static async Task<string> AttachElectrodesBatch(
        IMeshGenCommandBridge bridge,
        [Description("Maximum distance (mm) from electrode to surface to consider for attachment. Default 5.0.")]
        double searchRadiusMm = 5.0,
        [Description("Number of smoothing passes for the deformation field. Default 2.")]
        int smoothingIterations = 2,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.AttachElectrodesBatchAsync(new AttachElectrodesBatchRequest
        {
            Parameters = new AttachmentParametersDto
            {
                ContactThresholdMm = searchRadiusMm,
                SmoothingIterations = smoothingIterations
            }
        }, cancellationToken);
        return PipelineTools.Serialize(result);
    }
}
