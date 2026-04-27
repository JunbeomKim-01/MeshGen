using System.Windows.Media;
using MeshGenLite.Infrastructure;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace MeshGenLite.Models;

public enum ElectrodeShape
{
    Circle = 0,
    Rectangle = 1,
    Triangle = 2
}

public sealed class ElectrodeLayer : ObservableObject
{
    private string name = "Layer";
    private double radius = 9.0;
    private double width = 18.0;
    private double depth = 18.0;
    private double thickness = 1.0;
    private double conductivity = 0.1;
    private double permittivity = 1.0;
    private WpfColor color = Colors.LightSkyBlue;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public double Radius
    {
        get => radius;
        set => SetProperty(ref radius, value);
    }

    public double Width
    {
        get => width;
        set => SetProperty(ref width, value);
    }

    public double Depth
    {
        get => depth;
        set => SetProperty(ref depth, value);
    }

    public double Thickness
    {
        get => thickness;
        set => SetProperty(ref thickness, value);
    }

    public double Conductivity
    {
        get => conductivity;
        set => SetProperty(ref conductivity, value);
    }

    public double Permittivity
    {
        get => permittivity;
        set => SetProperty(ref permittivity, value);
    }

    public WpfColor Color
    {
        get => color;
        set
        {
            if (SetProperty(ref color, value))
            {
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(ColorHex));
            }
        }
    }

    public SolidColorBrush ColorBrush => new(Color);

    public string ColorHex
    {
        get => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
        set
        {
            try
            {
                if (WpfColorConverter.ConvertFromString(value) is WpfColor parsed)
                {
                    Color = parsed;
                }
            }
            catch
            {
                // ignore parse errors; user is mid-edit
            }
        }
    }

    public ElectrodeLayer Clone() => new()
    {
        Name = Name,
        Radius = Radius,
        Width = Width,
        Depth = Depth,
        Thickness = Thickness,
        Conductivity = Conductivity,
        Permittivity = Permittivity,
        Color = Color
    };
}

public static class ElectrodeLayerPresets
{
    public static IReadOnlyList<ElectrodeLayer> CreateDefault()
    {
        return new[]
        {
            new ElectrodeLayer
            {
                Name = "Hydrogel",
                Radius = 10.0, Width = 20.0, Depth = 20.0, Thickness = 1.0,
                Conductivity = 0.1, Permittivity = 1e4,
                Color = ParseColor("#7AC1FF")
            },
            new ElectrodeLayer
            {
                Name = "Ceramic",
                Radius = 9.0, Width = 18.0, Depth = 18.0, Thickness = 1.0,
                Conductivity = 1e-12, Permittivity = 200.0,
                Color = ParseColor("#E0E0E0")
            },
            new ElectrodeLayer
            {
                Name = "Metal",
                Radius = 9.0, Width = 18.0, Depth = 18.0, Thickness = 0.5,
                Conductivity = 5.96e7, Permittivity = 1.0,
                Color = ParseColor("#FFC857")
            }
        };
    }

    public static ElectrodeLayer CreateBlankLayer(int existingCount)
    {
        return new ElectrodeLayer
        {
            Name = $"Layer {existingCount + 1}",
            Radius = 9.0, Width = 18.0, Depth = 18.0, Thickness = 1.0,
            Conductivity = 0.1, Permittivity = 1.0,
            Color = ParseColor("#A0A0A0")
        };
    }

    private static WpfColor ParseColor(string hex)
        => (WpfColor)WpfColorConverter.ConvertFromString(hex);
}
