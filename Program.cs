using System.Windows;
using MeshGenLite.Integration.Mcp;
using MeshGenLite.Integration.Mcp.Bridge;
using MeshGenLite.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MeshGenLite;

/// <summary>
/// Custom entry point. By default launches the WPF application as before.
/// When invoked with <c>--mcp</c> (typically from Claude Desktop or another
/// MCP client) the process runs headless and serves the MeshGen MCP server
/// over stdio instead.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--mcp", StringComparison.OrdinalIgnoreCase)))
        {
            return RunMcpServer(args);
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }

    private static int RunMcpServer(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Important: MCP stdio uses stdout for JSON-RPC frames, so logs MUST go to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton<IPipelineService, PipelineService>();
        builder.Services.AddSingleton<SceneStateService>();
        builder.Services.AddSingleton<ElectrodeFactory>();
        builder.Services.AddSingleton<ElectrodeTransformBuilder>();
        builder.Services.AddSingleton<IElasticDeformationService, ElasticDeformationService>();
        builder.Services.AddSingleton<IVolumeMeshService, VolumeMeshService>();
        builder.Services.AddSingleton<IMeshExportService, MeshExportService>();
        builder.Services.AddSingleton<IFemExportService, FemExportService>();
        builder.Services.AddSingleton<IMeshGenCommandBridge, InProcessMeshGenCommandBridge>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var host = builder.Build();
        host.Run();
        return 0;
    }
}
