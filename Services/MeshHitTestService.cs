using MeshGenLite.Models;

namespace MeshGenLite.Services;

public sealed class MeshHitTestService
{
    private readonly VtkViewportService viewportService;

    public MeshHitTestService(VtkViewportService viewportService)
    {
        this.viewportService = viewportService;
    }

    public bool TryHitExternalSurface(int screenX, int screenY, out MeshHitResult result)
        => viewportService.TryPickExternalHit(screenX, screenY, out result);
}
