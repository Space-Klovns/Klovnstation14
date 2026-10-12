using System.Numerics;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Draws every NPC squad in its own colour: a double ring on the leader, single rings on members, lines
///         from the leader to each member, squares on the room thresholds the squad is covering, and a line
///         from each member to its assigned cover position. The squad's threat is a cross. What each member believes
///         about the hostiles it knows of is drawn too - see <see cref="DrawContact"/> - and so are its hunt for one
///         it has lost and each member's order: see <see cref="DrawHunt"/>. Every meter a member has - caution, say -
///         is a bar under it, labelled with its value: see <see cref="DrawMeterBars"/>.
/// </summary>
public sealed partial class SquadDebugOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private SquadDebugSystem _squadDebugSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    private const float MeterWidth = 0.9f;
    private const float MeterHeight = 0.08f;
    private const float MeterSpacing = 0.14f;
    private const float MeterOffset = 0.6f;

    private Font? _font;

    private static readonly Color KillZoneColor = Color.Crimson;

    private const float LeaderRadius = 0.55f;
    private const float MemberRadius = 0.4f;
    private const float ThresholdHalfSize = 0.3f;
    private const float CoverRadius = 0.15f;
    private const float ThreatHalfSize = 0.35f;
    private const float ContactRadius = 0.3f;

    private static readonly Color VisibleContactColor = Color.Red.WithAlpha(0.6f);
    private static readonly Color LostContactColor = Color.Orange;
    private static readonly Color ReportedContactColor = Color.Yellow;
    private static readonly Color HiddenContactColor = Color.Magenta;

    private const float SearchPointRadius = 0.2f;
    private const float EntranceSize = 0.3f;

    /// <summary>
    ///     Scratch corners for the shapes drawn by <see cref="DrawLoop"/>.
    /// </summary>
    private readonly Vector2[] _shape = new Vector2[4];

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_squadDebugSystem.Latest is not { } data)
            return;

        if (args.Space == OverlaySpace.ScreenSpace)
        {
            DrawMeterLabels(args, data);
            return;
        }

        DrawMeterBars(args.WorldHandle, data, args.MapId);
        DrawKillZones(args.WorldHandle, data, args.MapId);

        var worldHandle = args.WorldHandle;

        foreach (var squad in data.Squads)
        {
            var color = GetSquadColor(squad.Squad);
            Vector2? leaderPosition = null;

            if (squad.Leader is { } leader && TryGetPosition(leader, args.MapId, out var leaderMapPosition))
            {
                leaderPosition = leaderMapPosition;
                worldHandle.DrawCircle(leaderMapPosition, LeaderRadius, color, false);
                worldHandle.DrawCircle(leaderMapPosition, LeaderRadius - 0.08f, color, false);
            }

            foreach (var member in squad.Members)
            {
                if (!TryGetPosition(member, args.MapId, out var memberPosition))
                    continue;

                worldHandle.DrawCircle(memberPosition, MemberRadius, color, false);

                if (leaderPosition is { } fromPosition)
                    worldHandle.DrawLine(fromPosition, memberPosition, color.WithAlpha(0.5f));
            }

            foreach (var threshold in squad.Thresholds)
            {
                if (!TryGetPosition(threshold, args.MapId, out var thresholdPosition, out var thresholdRotation))
                    continue;

                worldHandle.SetTransform(GetMarkerTransform(thresholdPosition, thresholdRotation));
                worldHandle.DrawRect(Square(ThresholdHalfSize), color, filled: false);
                worldHandle.SetTransform(Matrix3x2.Identity);
            }

            if (squad.Threat is { } threat && TryGetPosition(threat, args.MapId, out var threatPosition, out var threatRotation))
            {
                var diagonal = new Vector2(ThreatHalfSize, ThreatHalfSize);
                var antiDiagonal = new Vector2(ThreatHalfSize, -ThreatHalfSize);
                worldHandle.SetTransform(GetMarkerTransform(threatPosition, threatRotation));
                worldHandle.DrawLine(-diagonal, diagonal, color);
                worldHandle.DrawLine(-antiDiagonal, antiDiagonal, color);
                worldHandle.SetTransform(Matrix3x2.Identity);
            }

            foreach (var assignment in squad.Assignments)
            {
                if (!TryGetPosition(assignment.Member, args.MapId, out var memberPosition) ||
                    !TryGetPosition(assignment.Cover, args.MapId, out var coverPosition))
                    continue;

                worldHandle.DrawLine(memberPosition, coverPosition, color.WithAlpha(0.3f));
                worldHandle.DrawCircle(coverPosition, CoverRadius, color.WithAlpha(0.8f));
            }

            foreach (var contact in squad.Contacts)
            {
                DrawContact(worldHandle, contact, args.MapId);
            }

            DrawHunt(worldHandle, squad, color, args.MapId);
        }
    }

    /// <summary>
    ///     A diamond where the hunted hostile should be, coloured by how far along the hunt is; a triangle outside each
    ///         way into its room, pointing in; a dot on each spot to search, hollow once searched, square for a locker;
    ///         and a line from each member to where its order sends it, coloured by the order.
    /// </summary>
    private void DrawHunt(DrawingHandleWorld worldHandle, SquadDebugSquad squad, Color squadColor, MapId mapId)
    {
        if (squad.HuntPhase is { } phase &&
            squad.HuntPredicted is { } predicted &&
            TryGetPosition(predicted, mapId, out var predictedPosition, out var predictedRotation))
        {
            var size = ThreatHalfSize;
            _shape[0] = new Vector2(0, size);
            _shape[1] = new Vector2(size, 0);
            _shape[2] = new Vector2(0, -size);
            _shape[3] = new Vector2(-size, 0);
            worldHandle.SetTransform(GetMarkerTransform(predictedPosition, predictedRotation));
            DrawLoop(worldHandle, 4, GetPhaseColor(phase));
            worldHandle.SetTransform(Matrix3x2.Identity);
        }

        foreach (var entrance in squad.HuntEntrances)
        {
            if (!TryGetPosition(entrance, mapId, out var entrancePosition, out var entranceRotation))
                continue;

            _shape[0] = new Vector2(0, EntranceSize);
            _shape[1] = new Vector2(EntranceSize, -EntranceSize);
            _shape[2] = new Vector2(-EntranceSize, -EntranceSize);
            worldHandle.SetTransform(GetMarkerTransform(entrancePosition, entranceRotation));
            DrawLoop(worldHandle, 3, squadColor);
            worldHandle.SetTransform(Matrix3x2.Identity);
        }

        foreach (var point in squad.SearchPoints)
        {
            if (!TryGetPosition(point.Coordinates, mapId, out var pointPosition, out var pointRotation))
                continue;

            var pointColor = point.Cleared ? squadColor.WithAlpha(0.4f) : squadColor;

            if (point.Locker)
            {
                worldHandle.SetTransform(GetMarkerTransform(pointPosition, pointRotation));
                worldHandle.DrawRect(Square(SearchPointRadius), pointColor, filled: !point.Cleared);
                worldHandle.SetTransform(Matrix3x2.Identity);
            }
            else
                worldHandle.DrawCircle(pointPosition, SearchPointRadius, pointColor, filled: !point.Cleared);
        }

        foreach (var order in squad.Orders)
        {
            if (!TryGetPosition(order.Member, mapId, out var memberPosition) ||
                !TryGetPosition(order.Target, mapId, out var targetPosition))
                continue;

            worldHandle.DrawLine(memberPosition, targetPosition, GetOrderColor(order.Kind));
        }
    }

    /// <summary>
    ///     Outlines the first <paramref name="count"/> points of <see cref="_shape"/>.
    /// </summary>
    private void DrawLoop(DrawingHandleWorld worldHandle, int count, Color color)
    {
        for (var i = 0; i < count; i++)
        {
            worldHandle.DrawLine(_shape[i], _shape[(i + 1) % count], color);
        }
    }

    internal static Color GetPhaseColor(NpcHuntPhase phase)
    {
        return phase switch
        {
            NpcHuntPhase.Watch => Color.Yellow,
            NpcHuntPhase.Stage => Color.Orange,
            NpcHuntPhase.Breach => Color.Magenta,
            NpcHuntPhase.Entry => Color.Red,
            NpcHuntPhase.Search => Color.Cyan,
            _ => Color.Gray,
        };
    }

    internal static Color GetOrderColor(NpcOrderKind kind)
    {
        return kind switch
        {
            NpcOrderKind.Investigate => Color.Yellow,
            NpcOrderKind.Watch => Color.LightYellow.WithAlpha(0.5f),
            NpcOrderKind.Stage => Color.Orange,
            NpcOrderKind.Breach => Color.Magenta,
            NpcOrderKind.Enter => Color.Red,
            NpcOrderKind.Search => Color.Cyan,
            NpcOrderKind.HoldArea => Color.Gray,
            NpcOrderKind.Regroup => Color.LimeGreen,
            _ => Color.White,
        };
    }

    /// <summary>
    ///     A bar under each member per meter it has a reading for - caution, say - filled to its share of the maximum,
    ///         stacked downwards in the order they arrive. "Under" is towards the grid's bottom, not the map's.
    /// </summary>
    private void DrawMeterBars(DrawingHandleWorld worldHandle, SquadDebugDataMessage data, MapId mapId)
    {
        foreach (var squad in data.Squads)
        {
            NetCoordinates? lastMember = null;
            var row = 0;

            foreach (var meter in squad.Meters)
            {
                row = meter.Member.Equals(lastMember) ? row + 1 : 0;
                lastMember = meter.Member;

                if (!TryGetPosition(meter.Member, mapId, out var memberPosition, out var gridRotation))
                    continue;

                var topLeft = new Vector2(-MeterWidth / 2f, -MeterOffset - row * MeterSpacing);
                var fraction = meter.Max > 0f ? Math.Clamp(meter.Value / meter.Max, 0f, 1f) : 0f;
                var color = GetMeterColor(meter.Meter);

                worldHandle.SetTransform(GetMarkerTransform(memberPosition, gridRotation));
                worldHandle.DrawRect(new Box2(topLeft - new Vector2(0f, MeterHeight), topLeft + new Vector2(MeterWidth, 0f)), color.WithAlpha(0.25f));
                worldHandle.DrawRect(new Box2(topLeft - new Vector2(0f, MeterHeight), topLeft + new Vector2(MeterWidth * fraction, 0f)), color);
                worldHandle.SetTransform(Matrix3x2.Identity);
            }
        }
    }

    /// <summary>
    ///     Each kill zone's tiles shaded red, deeper where it is more dangerous, on the grid as it is now, with a cross on
    ///         the tile where someone went down.
    /// </summary>
    private void DrawKillZones(DrawingHandleWorld worldHandle, SquadDebugDataMessage data, MapId mapId)
    {
        foreach (var zone in data.KillZones)
        {
            if (!_entityManager.TryGetEntity(zone.Grid, out var gridUid) ||
                !_entityManager.TryGetComponent(gridUid, out MapGridComponent? mapGridComponent) ||
                !_entityManager.TryGetComponent(gridUid, out TransformComponent? gridTransform) ||
                gridTransform.MapID != mapId)
                continue;

            var tileSize = mapGridComponent.TileSize;
            worldHandle.SetTransform(_transformSystem.GetWorldMatrix(gridUid.Value));

            for (var i = 0; i < zone.Tiles.Count; i++)
            {
                var tile = zone.Tiles[i];
                var box = new Box2(tile.X * tileSize, tile.Y * tileSize, (tile.X + 1) * tileSize, (tile.Y + 1) * tileSize);
                worldHandle.DrawRect(box, KillZoneColor.WithAlpha(0.1f + 0.35f * zone.Danger[i]));
            }

            var centerBox = new Box2(zone.Center.X * tileSize, zone.Center.Y * tileSize, (zone.Center.X + 1) * tileSize, (zone.Center.Y + 1) * tileSize);
            worldHandle.DrawLine(centerBox.BottomLeft, centerBox.TopRight, KillZoneColor);
            worldHandle.DrawLine(centerBox.TopLeft, centerBox.BottomRight, KillZoneColor);

            worldHandle.SetTransform(Matrix3x2.Identity);
        }
    }

    /// <summary>
    ///     Each meter's name and value, beside its bar.
    /// </summary>
    private void DrawMeterLabels(in OverlayDrawArgs args, SquadDebugDataMessage data)
    {
        _font ??= new VectorFont(_resourceCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 9);

        foreach (var zone in data.KillZones)
        {
            if (!_entityManager.TryGetEntity(zone.Grid, out var gridUid) ||
                !_entityManager.TryGetComponent(gridUid, out MapGridComponent? mapGridComponent))
                continue;

            var center = new EntityCoordinates(gridUid.Value, new Vector2(zone.Center.X + 0.5f, zone.Center.Y + 0.5f) * mapGridComponent.TileSize);
            var centerMap = _transformSystem.ToMapCoordinates(center);
            if (centerMap.MapId == args.MapId)
                args.ScreenHandle.DrawString(_font, _eyeManager.WorldToScreen(centerMap.Position) + new Vector2(6f, 6f), $"kill zone {zone.SecondsLeft:F0}s", KillZoneColor);
        }

        foreach (var squad in data.Squads)
        {
            NetCoordinates? lastMember = null;
            var row = 0;

            foreach (var meter in squad.Meters)
            {
                row = meter.Member.Equals(lastMember) ? row + 1 : 0;
                lastMember = meter.Member;

                if (!TryGetPosition(meter.Member, args.MapId, out var memberPosition, out var gridRotation))
                    continue;

                // The label is screen-space text, which no transform turns: only where it starts follows the bar.
                var barRight = Vector2.Transform(new Vector2(MeterWidth / 2f + 0.1f, -MeterOffset - row * MeterSpacing),
                    GetMarkerTransform(memberPosition, gridRotation));
                var screenPosition = _eyeManager.WorldToScreen(barRight) - new Vector2(0f, 7f);
                args.ScreenHandle.DrawString(_font, screenPosition, $"{meter.Meter} {meter.Value:F0}/{meter.Max:F0}", GetMeterColor(meter.Meter));
            }
        }
    }

    /// <summary>
    ///     A stable colour per meter, so caution looks the same on every member.
    /// </summary>
    private static Color GetMeterColor(string meter)
    {
        var hash = 0u;
        foreach (var character in meter)
        {
            hash = hash * 31 + character;
        }

        return Color.FromHsv(new Vector4(hash % 360 / 360f, 0.7f, 1f, 1f));
    }

    /// <summary>
    ///     A line to a hostile in sight; a ring where a lost or called-out one was, with a line on to where a lost
    ///         one is guessed to be; a square on the locker a hidden one is in, faint if only suspected.
    /// </summary>
    private void DrawContact(DrawingHandleWorld worldHandle, SquadDebugContact contact, MapId mapId)
    {
        if (!TryGetPosition(contact.Member, mapId, out var memberPosition) ||
            !TryGetPosition(contact.Believed, mapId, out var believedPosition))
            return;

        switch (contact.State)
        {
            case NpcContactState.Visible:
                worldHandle.DrawLine(memberPosition, believedPosition, VisibleContactColor);
                break;

            case NpcContactState.Lost:
            case NpcContactState.Reported:
                var ringColor = contact.State == NpcContactState.Lost ? LostContactColor : ReportedContactColor;
                worldHandle.DrawCircle(believedPosition, ContactRadius, ringColor, false);

                if (contact.Predicted is { } predicted && TryGetPosition(predicted, mapId, out var predictedPosition))
                {
                    worldHandle.DrawLine(believedPosition, predictedPosition, ringColor);
                    worldHandle.DrawCircle(predictedPosition, ContactRadius * 0.4f, ringColor);
                }

                break;

            case NpcContactState.Concealed:
            case NpcContactState.Suspected:
                var hiddenColor = contact.State == NpcContactState.Concealed ? HiddenContactColor : HiddenContactColor.WithAlpha(0.4f);
                var halfSize = new Vector2(ContactRadius, ContactRadius);
                worldHandle.DrawRect(new Box2(believedPosition - halfSize, believedPosition + halfSize), hiddenColor, false);
                worldHandle.DrawLine(memberPosition, believedPosition, hiddenColor.WithAlpha(0.3f));
                break;
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

    /// <summary>
    ///     A stable, well-spread hue per squad: the golden ratio walks the colour wheel without repeating soon.
    /// </summary>
    private static Color GetSquadColor(NetEntity squad)
    {
        var hue = (squad.Id * 0.61803398875f) % 1f;
        return Color.FromHsv(new Vector4(hue, 0.85f, 1f, 1f));
    }
}
