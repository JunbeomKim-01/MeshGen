using System.ComponentModel;
using System.Text.Json;
using MeshGenLite.Integration.Mcp.Bridge;
using MeshGenLite.Integration.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace MeshGenLite.Integration.Mcp.Tools;

/// <summary>
/// MCP tools that wrap import / label-map / surface stages of the MeshGen pipeline.
/// All tools delegate to <see cref="IMeshGenCommandBridge"/> and serialize the
/// uniform <see cref="CommonToolResponse{T}"/> envelope as the response payload.
/// </summary>
[McpServerToolType]
public static class PipelineTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [McpServerTool(Name = "import_dicom_study"), Description(
        "Import a DICOM CT/MR series (folder of .dcm files) with optional RT structure set. " +
        "Replaces any previously loaded study.")]
    public static async Task<string> ImportDicomStudy(
        IMeshGenCommandBridge bridge,
        [Description("Absolute path to the folder containing DICOM (.dcm) files.")]
        string dicomDirectory,
        [Description("Optional absolute path to an RTSS .dcm file containing ROI contours.")]
        string? rtssPath = null,
        [Description("Target voxel spacing in millimeters for the rasterized label map. Default 2.0.")]
        double targetSpacingMm = 2.0,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ImportDicomStudyAsync(new ImportDicomStudyRequest
        {
            DicomDirectory = dicomDirectory,
            RtssPath = rtssPath,
            TargetSpacingMm = targetSpacingMm
        }, cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "import_nifti_segmentation"), Description(
        "Import one or more NIfTI label maps (.nii / .nii.gz). Multiple files are merged into " +
        "a single multi-label volume sharing the same geometry. Replaces any previously loaded study.")]
    public static async Task<string> ImportNiftiSegmentation(
        IMeshGenCommandBridge bridge,
        [Description("List of absolute paths to .nii or .nii.gz files. All must share identical origin/spacing/dimensions.")]
        string[] paths,
        [Description("Target voxel spacing in mm. Default 2.0.")]
        double targetSpacingMm = 2.0,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ImportNiftiSegmentationAsync(new ImportNiftiSegmentationRequest
        {
            Paths = paths,
            TargetSpacingMm = targetSpacingMm
        }, cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "build_label_map"), Description(
        "Rasterize the selected ROIs of the current study into a 3D label map. " +
        "If the study source is already a NIfTI label map this just clones the native one. " +
        "Required before build_surface_mesh.")]
    public static async Task<string> BuildLabelMap(
        IMeshGenCommandBridge bridge,
        [Description("Optional list of ROI IDs to include. When omitted, all ROIs with contours are used.")]
        int[]? roiIds = null,
        [Description("Target voxel spacing in mm. Default 2.0.")]
        double targetSpacingMm = 2.0,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.BuildLabelMapAsync(new BuildLabelMapRequest
        {
            RoiIds = roiIds ?? Array.Empty<int>(),
            TargetSpacingMm = targetSpacingMm
        }, cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "build_surface_mesh"), Description(
        "Run marching cubes + smoothing on the current label map to produce a surface mesh per " +
        "selected ROI. The ROI flagged as External receives stronger smoothing.")]
    public static async Task<string> BuildSurfaceMesh(
        IMeshGenCommandBridge bridge,
        [Description("Optional ROI ID list. When omitted, all ROIs in the study are processed.")]
        int[]? roiIds = null,
        [Description("Smoothing iterations applied specifically to the External ROI. Default 20.")]
        int externalSmoothingIterations = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.BuildSurfaceMeshAsync(new BuildSurfaceMeshRequest
        {
            RoiIds = roiIds,
            ExternalSmoothingIterations = externalSmoothingIterations
        }, cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}
