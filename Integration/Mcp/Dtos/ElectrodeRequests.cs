namespace MeshGenLite.Integration.Mcp.Dtos;

public sealed class ElectrodeLayerDto
{
    public string Name { get; init; } = "Layer";

    public double Radius { get; init; } = 9.0;

    public double Width { get; init; } = 18.0;

    public double Depth { get; init; } = 18.0;

    public double Thickness { get; init; } = 1.0;

    public double Conductivity { get; init; } = 0.1;

    public double Permittivity { get; init; } = 1.0;

    public string? ColorHex { get; init; }
}

public sealed class PlaceElectrodeAtPoseRequest
{
    public double[] Position { get; init; } = new double[3];

    public double[] Normal { get; init; } = new double[] { 0.0, 0.0, 1.0 };

    public double Clearance { get; init; } = 0.5;

    /// <summary>"Circle" | "Rectangle" | "Triangle". When null, uses the default spec.</summary>
    public string? Shape { get; init; }

    /// <summary>When null/empty, the default 3-layer Hydrogel/Ceramic/Metal preset is used.</summary>
    public IReadOnlyList<ElectrodeLayerDto>? Layers { get; init; }
}

public sealed class PlaceElectrodeAtPoseResult
{
    public int ElectrodeId { get; init; }

    public double[] WorldPosition { get; init; } = Array.Empty<double>();

    public double[] WorldNormal { get; init; } = Array.Empty<double>();

    public double TotalThicknessMm { get; init; }

    public double FootprintRadiusMm { get; init; }

    public int LayerCount { get; init; }
}

public sealed class ElectrodeSummaryDto
{
    public int Id { get; init; }

    public double[] WorldPosition { get; init; } = Array.Empty<double>();

    public double[] WorldNormal { get; init; } = Array.Empty<double>();

    public double LengthMm { get; init; }

    public double RadiusMm { get; init; }

    public double ClearanceMm { get; init; }
}

public sealed class ListElectrodesResult
{
    public IReadOnlyList<ElectrodeSummaryDto> Electrodes { get; init; } = Array.Empty<ElectrodeSummaryDto>();
}

public sealed class ClearElectrodesResult
{
    public int RemovedCount { get; init; }
}

public sealed class SetDefaultElectrodeSpecRequest
{
    public string? Shape { get; init; }

    public IReadOnlyList<ElectrodeLayerDto> Layers { get; init; } = Array.Empty<ElectrodeLayerDto>();
}

public sealed class SetDefaultElectrodeSpecResult
{
    public string Shape { get; init; } = string.Empty;

    public int LayerCount { get; init; }

    public double TotalThicknessMm { get; init; }
}
