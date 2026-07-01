using MeshGenLite.Integration.Mcp.Dtos;

namespace MeshGenLite.Integration.Mcp.Bridge;

/// <summary>
/// Single seam between MCP tools (or any future external client) and MeshGen
/// core services. Tool classes only depend on this interface, never on the
/// concrete Pipeline/Repo/Service types — keeps tools testable and lets us
/// later swap in an out-of-process IPC implementation without touching tools.
/// </summary>
public interface IMeshGenCommandBridge
{
    Task<CommonToolResponse<ImportDicomStudyResult>> ImportDicomStudyAsync(
        ImportDicomStudyRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<ImportDicomStudyResult>> ImportNiftiSegmentationAsync(
        ImportNiftiSegmentationRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<ListRoisResult>> ListRoisAsync(CancellationToken cancellationToken);

    Task<CommonToolResponse<SetExternalRoiResult>> SetExternalRoiAsync(
        SetExternalRoiRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<BuildLabelMapResult>> BuildLabelMapAsync(
        BuildLabelMapRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<BuildSurfaceMeshResult>> BuildSurfaceMeshAsync(
        BuildSurfaceMeshRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<PlaceElectrodeAtPoseResult>> PlaceElectrodeAtPoseAsync(
        PlaceElectrodeAtPoseRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<ListElectrodesResult>> ListElectrodesAsync(CancellationToken cancellationToken);

    Task<CommonToolResponse<ClearElectrodesResult>> ClearElectrodesAsync(CancellationToken cancellationToken);

    Task<CommonToolResponse<SetDefaultElectrodeSpecResult>> SetDefaultElectrodeSpecAsync(
        SetDefaultElectrodeSpecRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<AttachElectrodesBatchResult>> AttachElectrodesBatchAsync(
        AttachElectrodesBatchRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<GenerateVolumeMeshResult>> GenerateVolumeMeshAsync(
        GenerateVolumeMeshRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<ExportResult>> ExportSurfaceStlAsync(
        ExportSurfaceRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<ExportResult>> ExportSurfacePolyAsync(
        ExportSurfaceRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<ExportFemPackageResult>> ExportFemPackageAsync(
        ExportFemPackageRequest request,
        CancellationToken cancellationToken);

    Task<CommonToolResponse<bool>> ShowMeshWindowAsync(CancellationToken cancellationToken);

    Task<PipelineStatusDto> GetPipelineStatusAsync(CancellationToken cancellationToken);
}
