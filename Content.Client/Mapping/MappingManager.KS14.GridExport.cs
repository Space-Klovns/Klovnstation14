// ============================================================================
// KS14 DISCLAIMER: This vanilla-namespace partial contains the heavily adapted
// Eclipsion PNG grid export port (84b343cf, 6b7453d4). Keep this feature here
// until mapping is fully moved into a KS14 namespace.
// ============================================================================
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._KS14.Mapping;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client.Mapping;

public sealed partial class MappingManager
{
    private static readonly Vector2i MinGridScreenshotSize = new(1920, 1080);
    private const int MaxGridScreenshotDimension = 4096;
    private const int ScreenshotPvsBudget = 2048;
    private static readonly TimeSpan ScreenshotPvsPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ScreenshotPvsTimeout = TimeSpan.FromSeconds(30);
    private const int ScreenshotPvsStablePolls = 5;

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill _gridScreenshotSawmill = default!;

    private partial void InitializeGridScreenshotExport()
    {
        _gridScreenshotSawmill = _logManager.GetSawmill("mapping");
        _net.RegisterNetMessage<MappingScreenshotPvsMessage>();
    }

    public async Task ExportGridScreenshot(Entity<MapGridComponent> grid)
    {
        if (!_entityManager.TryGetComponent<TransformComponent>(grid.Owner, out var transformComponent) ||
            grid.Comp.LocalAABB.IsEmpty())
        {
            _gridScreenshotSawmill.Error("Unable to export the selected grid because it no longer exists or is empty.");
            return;
        }

        var transformSystem = _entityManager.System<SharedTransformSystem>();
        var bounds = transformSystem.GetWorldMatrix(transformComponent).TransformBox(grid.Comp.LocalAABB);
        bounds = bounds.Enlarged(MathF.Max(grid.Comp.TileSize, bounds.MaxDimension * 0.015f));

        var screenshotSize = new Vector2i(
            Math.Clamp((int) MathF.Ceiling(bounds.Width * EyeManager.PixelsPerMeter), MinGridScreenshotSize.X, MaxGridScreenshotDimension),
            Math.Clamp((int) MathF.Ceiling(bounds.Height * EyeManager.PixelsPerMeter), MinGridScreenshotSize.Y, MaxGridScreenshotDimension));
        var zoom = MathF.Max(
            MathF.Max(bounds.Width / (screenshotSize.X / (float) EyeManager.PixelsPerMeter),
                bounds.Height / (screenshotSize.Y / (float) EyeManager.PixelsPerMeter)),
            0.01f);

        var savePath = await _file.SaveFile(new FileDialogFilters(new FileDialogFilters.Group("png")));
        if (savePath is not { fileStream: var stream })
            return;

        await using (stream)
        {
            var netGrid = _entityManager.GetNetEntity(grid.Owner);
            var oldSpawnBudget = _configurationManager.GetCVar(CVars.NetPVSEntityBudget);
            var oldEnterBudget = _configurationManager.GetCVar(CVars.NetPVSEntityEnterBudget);

            try
            {
                _configurationManager.SetCVar(CVars.NetPVSEntityBudget, ScreenshotPvsBudget);
                _configurationManager.SetCVar(CVars.NetPVSEntityEnterBudget, ScreenshotPvsBudget);
                SetGridScreenshotPvs(netGrid, true);
                await WaitForGridEntities(grid.Owner);

                var eye = new FixedEye
                {
                    Position = new MapCoordinates(bounds.Center, transformComponent.MapID),
                    Zoom = new Vector2(zoom),
                    DrawFov = false,
                    DrawLight = true,
                };

                using var viewport = _clyde.CreateViewport(screenshotSize, "MappingGridScreenshot");
                viewport.Eye = eye;
                viewport.Render();

                var screenshotSource = new TaskCompletionSource<Image<Rgba32>>();
                viewport.RenderTarget.CopyPixelsToMemory<Rgba32>(image => screenshotSource.SetResult(image));
                using var screenshot = await screenshotSource.Task;
                await Task.Run(() => screenshot.SaveAsPng(stream));
                await stream.FlushAsync();
            }
            finally
            {
                SetGridScreenshotPvs(netGrid, false);
                _configurationManager.SetCVar(CVars.NetPVSEntityBudget, oldSpawnBudget);
                _configurationManager.SetCVar(CVars.NetPVSEntityEnterBudget, oldEnterBudget);
            }
        }
    }

    private void SetGridScreenshotPvs(NetEntity grid, bool enabled)
    {
        _net.ClientSendMessage(new MappingScreenshotPvsMessage { Grid = grid, Enabled = enabled });
    }

    private async Task WaitForGridEntities(EntityUid grid)
    {
        var polls = (int) (ScreenshotPvsTimeout / ScreenshotPvsPollInterval);
        var previous = -1;
        var stable = 0;

        for (var i = 0; i < polls; i++)
        {
            await Task.Delay(ScreenshotPvsPollInterval);
            var count = CountGridEntities(grid);
            if (count != previous)
            {
                previous = count;
                stable = 0;
                continue;
            }

            if (++stable >= ScreenshotPvsStablePolls)
                return;
        }

        _gridScreenshotSawmill.Warning("Timed out waiting for grid {0} to finish streaming in.", grid);
    }

    private int CountGridEntities(EntityUid grid)
    {
        var count = 0;
        var query = _entityManager.AllEntityQueryEnumerator<TransformComponent>();
        while (query.MoveNext(out var transformComponent))
        {
            if (transformComponent.GridUid == grid)
                count++;
        }

        return count;
    }
}