using System.Numerics;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Draws the shots the server sends (see <see cref="LineOfFireDebugSystem"/>): a line from each NPC, green if its shot
///         gets to its target and red up to what stops it, labelled with what the shot collides with and what stopped it.
/// </summary>
public sealed partial class LineOfFireDebugOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private LineOfFireDebugSystem _lineOfFireDebugSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    private static readonly Color ClearColor = Color.LimeGreen;
    private static readonly Color BlockedColor = Color.Red;

    private Font? _font;

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_lineOfFireDebugSystem.Latest is not { } latest)
            return;

        if (args.Space == OverlaySpace.ScreenSpace)
        {
            _font ??= new VectorFont(_resourceCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);

            foreach (var shot in latest.Shots)
            {
                if (!TryGetPosition(shot.From, args.MapId, out var fromPosition))
                    continue;

                var text = shot.Blocker is { } blocker ? $"{shot.Mask}\nstopped by {blocker}" : shot.Mask;
                args.ScreenHandle.DrawString(_font, _eyeManager.WorldToScreen(fromPosition) + new Vector2(12f, -12f), text, shot.Clear ? ClearColor : BlockedColor);
            }

            return;
        }

        foreach (var shot in latest.Shots)
        {
            if (TryGetPosition(shot.From, args.MapId, out var fromPosition) && TryGetPosition(shot.To, args.MapId, out var toPosition))
                args.WorldHandle.DrawLine(fromPosition, toPosition, shot.Clear ? ClearColor : BlockedColor);
        }
    }

    private bool TryGetPosition(NetCoordinates netCoordinates, MapId mapId, out Vector2 position)
    {
        position = default;

        if (!_entityManager.TryGetEntity(netCoordinates.NetEntity, out _))
            return false;

        var mapCoordinates = _transformSystem.ToMapCoordinates(_entityManager.GetCoordinates(netCoordinates));
        if (mapCoordinates.MapId != mapId)
            return false;

        position = mapCoordinates.Position;
        return true;
    }
}
