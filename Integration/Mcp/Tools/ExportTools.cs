using System.ComponentModel;
using MeshGenLite.Integration.Mcp.Bridge;
using MeshGenLite.Integration.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace MeshGenLite.Integration.Mcp.Tools;

[McpServerToolType]
public static class ExportTools
{
    [McpServerTool(Name = "export_surface_stl"), Description(
        "Export all current surface meshes (body and internal organs) as separate STL files to the target directory.")]
    public static async Task<string> ExportSurfaceStl(
        IMeshGenCommandBridge bridge,
        [Description("Absolute path to the output .stl file.")]
        string outputPath,
        [Description("Overwrite if file exists.")]
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ExportSurfaceStlAsync(new ExportSurfaceRequest
        {
            OutputPath = outputPath,
            Overwrite = overwrite
        }, cancellationToken);
        return PipelineTools.Serialize(result);
    }

    [McpServerTool(Name = "export_surface_poly"), Description(
        "Export the entire scene as a TetGen-compatible .poly file (PLC) to the target directory.")]
    public static async Task<string> ExportSurfacePoly(
        IMeshGenCommandBridge bridge,
        [Description("Absolute path to the output .poly file.")]
        string outputPath,
        [Description("Overwrite if file exists.")]
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ExportSurfacePolyAsync(new ExportSurfaceRequest
        {
            OutputPath = outputPath,
            Overwrite = overwrite
        }, cancellationToken);
        return PipelineTools.Serialize(result);
    }

    [McpServerTool(Name = "export_fem_package"), Description(
        "Export a FEM-ready file package: VTU geometry mesh, element region labels, boundary face markers, " +
        "and a region material table for electric field and thermal FEM import.")]
    public static async Task<string> ExportFemPackage(
        IMeshGenCommandBridge bridge,
        [Description("Absolute path to the output folder.")]
        string outputDirectory,
        [Description("Base file name without extension. Default meshgen_fem.")]
        string baseName = "meshgen_fem",
        [Description("Alpha radius (mm) for volume mesh generation if needed. Default 0.0.")]
        double alphaMm = 0.0,
        [Description("Maximum input point count if volume mesh generation is needed. Default 3000.")]
        int maxPoints = 3000,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ExportFemPackageAsync(new ExportFemPackageRequest
        {
            OutputDirectory = outputDirectory,
            BaseName = baseName,
            AlphaMm = alphaMm,
            MaxPoints = maxPoints
        }, cancellationToken);
        return PipelineTools.Serialize(result);
    }
}
