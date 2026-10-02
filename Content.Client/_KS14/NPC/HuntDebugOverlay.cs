using System.Numerics;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Draws each hunt the server sends (see <see cref="HuntDebugSystem"/>):
///     <list type="bullet">
///         <item>the room: floor still to be seen shaded red, floor already seen faintly green, doorways outlined;</item>
///         <item>each way in: a triangle where members wait, joined to the point inside they go to;</item>
///         <item>each staging member's way round the room, turn by turn;</item>
///         <item>lockers (squares) and spots (dots) to check, hollow once checked, joined to whoever is checking;</item>
///         <item>each sweeping member's line to the unseen tile it is heading for;</item>
///         <item>every member's order, coloured by kind;</item>
///         <item>where the hostile was last seen (a ring) and should be now (a diamond), labelled with the phase, its
///             timers, and how much of the room has been seen.</item>
///     </list>
/// </summary>
public sealed partial class HuntDebugOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private HuntDebugSystem _huntDebugSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    private static readonly Color UnseenColor = Color.Red.WithAlpha(0.25f);
    private static readonly Color SeenColor = Color.LimeGreen.WithAlpha(0.08f);
    private static readonly Color ThresholdColor = Color.Yellow.WithAlpha(0.8f);
    private static readonly Color EntranceColor = Color.Orange;
    private static readonly Color RouteColor = Color.Orange.WithAlpha(0.7f);
    private static readonly Color PointColor = Color.Cyan;
    private static readonly Color SweepColor = Color.Cyan.WithAlpha(0.4f);
    private static readonly Color LastKnownColor = Color.White;

    private const float MarkerSize = 0.3f;

    private Font? _font;

    private readonly Vector2[] _shape = new Vector2[4];

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_huntDebugSystem.Frames.Count == 0)
            return;

        if (args.Space == OverlaySpace.ScreenSpace)
        {
            DrawLabels(args);
            return;
        }

        var worldHandle = args.WorldHandle;

        foreach (var (frame, _) in _huntDebugSystem.Frames.Values)
        {
            DrawRoom(worldHandle, frame, args.MapId);
            DrawEntrances(worldHandle, frame, args.MapId);
            DrawRoutes(worldHandle, frame, args.MapId);
            DrawSearch(worldHandle, frame, args.MapId);

            foreach (var order in frame.Orders)
            {
                if (TryGetPosition(order.Member, args.MapId, out var memberPosition) &&
                    TryGetPosition(order.Target, args.MapId, out var targetPosition))
                    worldHandle.DrawLine(memberPosition, targetPosition, SquadDebugOverlay.GetOrderColor(order.Kind));
            }

            if (TryGetPosition(frame.LastKnown, args.MapId, out var lastKnownPosition))
                worldHandle.DrawCircle(lastKnownPosition, MarkerSize, LastKnownColor, filled: false);

            if (TryGetPosition(frame.Predicted, args.MapId, out var predictedPosition, out var predictedRotation))
            {
                _shape[0] = new Vector2(0, MarkerSize);
                _shape[1] = new Vector2(MarkerSize, 0);
                _shape[2] = new Vector2(0, -MarkerSize);
                _shape[3] = new Vector2(-MarkerSize, 0);
                worldHandle.SetTransform(GetMarkerTransform(predictedPosition, predictedRotation));
                DrawLoop(worldHandle, 4, SquadDebugOverlay.GetPhaseColor(frame.Phase));
                worldHandle.SetTransform(Matrix3x2.Identity);
            }
        }
    }

    /// <summary>
    ///     Tiles are drawn in the grid's own frame, under its current world transform, so a turned or moving grid is
    ///         drawn as it is.
    /// </summary>
    private void DrawRoom(DrawingHandleWorld worldHandle, HuntDebugDataMessage frame, MapId mapId)
    {
        if (frame.Grid is not { } netGrid ||
            !_entityManager.TryGetEntity(netGrid, out var gridUid) ||
            !_entityManager.TryGetComponent(gridUid, out MapGridComponent? mapGridComponent) ||
            !_entityManager.TryGetComponent(gridUid, out TransformComponent? gridTransform) ||
            gridTransform.MapID != mapId)
            return;

        var tileSize = mapGridComponent.TileSize;
        worldHandle.SetTransform(_transformSystem.GetWorldMatrix(gridUid.Value));

        foreach (var tile in frame.RoomTiles)
        {
            worldHandle.DrawRect(TileBox(tile, tileSize), SeenColor);
        }

        foreach (var tile in frame.UnseenTiles)
        {
            worldHandle.DrawRect(TileBox(tile, tileSize), UnseenColor);
        }

        foreach (var tile in frame.ThresholdTiles)
        {
            worldHandle.DrawRect(TileBox(tile, tileSize), ThresholdColor, filled: false);
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private static Box2 TileBox(Vector2i tile, ushort tileSize)
    {
        return new Box2(tile.X * tileSize, tile.Y * tileSize, (tile.X + 1) * tileSize, (tile.Y + 1) * tileSize);
    }

    private void DrawEntrances(DrawingHandleWorld worldHandle, HuntDebugDataMessage frame, MapId mapId)
    {
        foreach (var entrance in frame.Entrances)
        {
            if (!TryGetPosition(entrance.Stage, mapId, out var stagePosition, out var stageRotation) ||
                !TryGetPosition(entrance.Breach, mapId, out var breachPosition))
                continue;

            _shape[0] = new Vector2(0, MarkerSize);
            _shape[1] = new Vector2(MarkerSize, -MarkerSize);
            _shape[2] = new Vector2(-MarkerSize, -MarkerSize);
            worldHandle.SetTransform(GetMarkerTransform(stagePosition, stageRotation));
            DrawLoop(worldHandle, 3, EntranceColor);
            worldHandle.SetTransform(Matrix3x2.Identity);

            worldHandle.DrawLine(stagePosition, breachPosition, EntranceColor.WithAlpha(0.5f));
            worldHandle.DrawCircle(breachPosition, MarkerSize * 0.5f, EntranceColor, filled: false);
        }
    }

    private void DrawRoutes(DrawingHandleWorld worldHandle, HuntDebugDataMessage frame, MapId mapId)
    {
        foreach (var route in frame.Routes)
        {
            Vector2? previous = null;

            foreach (var point in route.Points)
            {
                if (!TryGetPosition(point, mapId, out var position))
                    continue;

                if (previous is { } from)
                {
                    worldHandle.DrawLine(from, position, RouteColor);
                    worldHandle.DrawCircle(position, MarkerSize * 0.3f, RouteColor);
                }

                previous = position;
            }
        }
    }

    private void DrawSearch(DrawingHandleWorld worldHandle, HuntDebugDataMessage frame, MapId mapId)
    {
        foreach (var point in frame.SearchPoints)
        {
            if (!TryGetPosition(point.Coordinates, mapId, out var position, out var pointRotation))
                continue;

            var color = point.Cleared ? PointColor.WithAlpha(0.4f) : PointColor;

            if (point.Locker)
            {
                worldHandle.SetTransform(GetMarkerTransform(position, pointRotation));
                worldHandle.DrawRect(Square(MarkerSize * 0.7f), color, filled: !point.Cleared);
                worldHandle.SetTransform(Matrix3x2.Identity);
            }
            else
                worldHandle.DrawCircle(position, MarkerSize * 0.6f, color, filled: !point.Cleared);

            if (point.Assignee is { } assignee && TryGetPosition(assignee, mapId, out var assigneePosition))
                worldHandle.DrawLine(assigneePosition, position, color.WithAlpha(0.6f));
        }

        foreach (var sweep in frame.Sweeps)
        {
            if (!TryGetPosition(sweep.From, mapId, out var fromPosition) ||
                !TryGetPosition(sweep.To, mapId, out var toPosition))
                continue;

            worldHandle.DrawLine(fromPosition, toPosition, SweepColor);
            worldHandle.DrawCircle(toPosition, MarkerSize * 0.4f, SweepColor, filled: false);
        }
    }

    /// <summary>
    ///     Next to where the hostile should be: the phase and how long it has run (and may run), the whole hunt's
    ///         time, and how much of the room has been seen against how much has to be.
    /// </summary>
    private void DrawLabels(in OverlayDrawArgs args)
    {
        _font ??= new VectorFont(_resourceCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);

        foreach (var (frame, _) in _huntDebugSystem.Frames.Values)
        {
            if (!TryGetPosition(frame.Predicted, args.MapId, out var predictedPosition))
                continue;

            var phaseTime = frame.PhaseLimit > 0f
                ? $"{frame.PhaseElapsed:F1}/{frame.PhaseLimit:F0}s"
                : $"{frame.PhaseElapsed:F1}s";

            var text = $"{frame.Phase} {phaseTime}\nhunt {frame.HuntElapsed:F1}/{frame.HuntTimeout:F0}s\nseen {frame.Coverage:P0} of {frame.RequiredCoverage:P0}";
            var screenPosition = _eyeManager.WorldToScreen(predictedPosition) + new Vector2(12f, -12f);

            args.ScreenHandle.DrawString(_font, screenPosition, text, SquadDebugOverlay.GetPhaseColor(frame.Phase));
        }
    }

    private void DrawLoop(DrawingHandleWorld worldHandle, int count, Color color)
    {
        for (var i = 0; i < count; i++)
        {
            worldHandle.DrawLine(_shape[i], _shape[(i + 1) % count], color);
        }
    }

    private bool TryGetPosition(NetCoordinates netCoordinates, MapId mapId, out Vector2 position)
    {
        return TryGetPosition(netCoordinates, mapId, out position, out _);
    }

    /// <summary>
    ///     Where <paramref name="netCoordinates"/> are on the map, and the world rotation of the grid they are on (none
    ///         off a grid). Markers with corners are drawn in a frame turned by that rotation (see
    ///         <see cref="GetMarkerTransform"/>), so they sit square to the grid they mark rather than to the map.
    /// </summary>
    private bool TryGetPosition(NetCoordinates netCoordinates, MapId mapId, out Vector2 position, out Angle gridRotation)
    {
        position = default;
        gridRotation = Angle.Zero;

        if (!_entityManager.TryGetEntity(netCoordinates.NetEntity, out _))
            return false;

        var coordinates = _entityManager.GetCoordinates(netCoordinates);
        var mapCoordinates = _transformSystem.ToMapCoordinates(coordinates);
        if (mapCoordinates.MapId != mapId)
            return false;

        position = mapCoordinates.Position;
        if (_transformSystem.GetGrid(coordinates) is { } gridUid)
            gridRotation = _transformSystem.GetWorldRotation(gridUid);

        return true;
    }

    /// <summary>
    ///     A marker's own frame: its origin at <paramref name="position"/>, its axes along its grid's. Set it on the
    ///         handle with <c>SetTransform</c>, draw the marker around <see cref="Vector2.Zero"/>, and set
    ///         <see cref="Matrix3x2.Identity"/> back after. Clyde applies the model transform to vertices as they are
    ///         queued, so changing it per marker is cheap.
    /// </summary>
    private static Matrix3x2 GetMarkerTransform(Vector2 position, Angle gridRotation)
    {
        return Matrix3Helpers.CreateTransform(position, gridRotation);
    }

    /// <summary>
    ///     A square of <paramref name="halfSize"/> around a marker's origin.
    /// </summary>
    private static Box2 Square(float halfSize)
    {
        return Box2.CenteredAround(Vector2.Zero, new Vector2(halfSize * 2f, halfSize * 2f));
    }
}
