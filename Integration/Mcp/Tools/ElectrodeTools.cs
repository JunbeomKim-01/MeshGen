using System.ComponentModel;
using MeshGenLite.Integration.Mcp.Bridge;
using MeshGenLite.Integration.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace MeshGenLite.Integration.Mcp.Tools;

[McpServerToolType]
public static class ElectrodeTools
{
    [McpServerTool(Name = "place_electrode_at_pose"), Description(
        "Place a single electrode at a specific world position and orientation. " +
        "Position is (x,y,z) in millimeters. Normal is the outward-facing vector (unit vector).")]
    public static async Task<string> PlaceElectrodeAtPose(
        IMeshGenCommandBridge bridge,
        [Description("World X coordinate in mm.")] double x,
        [Description("World Y coordinate in mm.")] double y,
        [Description("World Z coordinate in mm.")] double z,
        [Description("Normal X component.")] double nx,
        [Description("Normal Y component.")] double ny,
        [Description("Normal Z component.")] double nz,
        [Description("Air gap or standoff distance in mm between electrode and skin. Default 0.5.")] double clearance = 0.5,
        [Description("Electrode shape: Circle, Rectangle, or Triangle. Default is Circle.")] string? shape = "Circle",
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.PlaceElectrodeAtPoseAsync(new PlaceElectrodeAtPoseRequest
        {
            Position = new[] { x, y, z },
            Normal = new[] { nx, ny, nz },
            Clearance = clearance,
            Shape = shape
        }, cancellationToken);
        return PipelineTools.Serialize(result);
    }

    [McpServerTool(Name = "list_electrodes"), Description(
        "Return all electrodes currently in the scene, including their IDs, labels, " +
        "poses, and current attachment status.")]
    public static async Task<string> ListElectrodes(
        IMeshGenCommandBridge bridge,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ListElectrodesAsync(cancellationToken);
        return PipelineTools.Serialize(result);
    }

    [McpServerTool(Name = "clear_electrodes"), Description(
        "Remove all electrodes from the scene.")]
    public static async Task<string> ClearElectrodes(
        IMeshGenCommandBridge bridge,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.ClearElectrodesAsync(cancellationToken);
        return PipelineTools.Serialize(result);
    }

    [McpServerTool(Name = "set_default_electrode_spec"), Description(
        "Set the default dimensions (width, height, thickness) and material properties " +
        "for subsequent electrode placement.")]
    public static async Task<string> SetDefaultElectrodeSpec(
        IMeshGenCommandBridge bridge,
        [Description("Electrode shape: Circle, Rectangle, or Triangle.")] string shape,
        [Description("Radius for Circle/Triangle or half-width for Rectangle (mm).")] double radius = 9.0,
        [Description("Thickness of the conductive layer (mm).")] double thickness = 1.0,
        CancellationToken cancellationToken = default)
    {
        var result = await bridge.SetDefaultElectrodeSpecAsync(new SetDefaultElectrodeSpecRequest
        {
            Shape = shape,
            Layers = new[]
            {
                new ElectrodeLayerDto { Name = "Conductive", Radius = radius, Width = radius * 2, Depth = radius * 2, Thickness = thickness }
            }
        }, cancellationToken);
        return PipelineTools.Serialize(result);
    }
}
