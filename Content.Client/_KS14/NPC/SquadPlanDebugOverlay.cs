using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared._KS14.NPC;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Draws each squad plan the server sends (see <see cref="SquadPlanDebugSystem"/>):
///     <list type="bullet">
///         <item>each member's shot at the hostile, green if it gets there, red up to what stops it;</item>
///         <item>what it means to shoot out, as a pink ring;</item>
///         <item>the search for a way to it, as a line, and each way into its room, a ring where members wait joined to
///             the point inside they go to;</item>
///         <item>every member's order, coloured by kind;</item>
///         <item>next to the hostile: where the plan has got to and why, its steps and what they cost, and what it knows.</item>
///     </list>
/// </summary>
public sealed partial class SquadPlanDebugOverlay : Overlay
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private SquadPlanDebugSystem _squadPlanDebugSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    private static readonly Color ClearColor = Color.LimeGreen;
    private static readonly Color BlockedColor = Color.Red;
    private static readonly Color BarrierColor = Color.HotPink;
    private static readonly Color PathColor = Color.Cyan.WithAlpha(0.5f);
    private static readonly Color EntranceColor = Color.Orange;
    private static readonly Color TargetColor = Color.White;

    private const float MarkerSize = 0.3f;

    private Font? _font;

    private readonly StringBuilder _text = new();

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_squadPlanDebugSystem.Frames.Count == 0)
            return;

        if (args.Space == OverlaySpace.ScreenSpace)
        {
            DrawLabels(args);
            return;
        }

        var worldHandle = args.WorldHandle;

        foreach (var (frame, _) in _squadPlanDebugSystem.Frames.Values)
        {
            foreach (var shot in frame.Shots)
            {
                if (TryGetPosition(shot.From, args.MapId, out var fromPosition) && TryGetPosition(shot.To, args.MapId, out var toPosition))
                    worldHandle.DrawLine(fromPosition, toPosition, shot.Clear ? ClearColor : BlockedColor);
            }

            foreach (var barrier in frame.Barriers)
            {
                if (TryGetPosition(barrier, args.MapId, out var barrierPosition))
                    worldHandle.DrawCircle(barrierPosition, MarkerSize * 1.5f, BarrierColor, filled: false);
            }

            for (var i = 1; i < frame.Path.Count; i++)
            {
                if (TryGetPosition(frame.Path[i - 1], args.MapId, out var lastPosition) && TryGetPosition(frame.Path[i], args.MapId, out var position))
                    worldHandle.DrawLine(lastPosition, position, PathColor);
            }

            foreach (var entrance in frame.Entrances)
            {
                if (!TryGetPosition(entrance.Stage, args.MapId, out var stagePosition) || !TryGetPosition(entrance.Entry, args.MapId, out var entryPosition))
                    continue;

                worldHandle.DrawCircle(stagePosition, MarkerSize, EntranceColor, filled: false);
                worldHandle.DrawLine(stagePosition, entryPosition, EntranceColor);
            }

            foreach (var order in frame.Orders)
            {
                if (TryGetPosition(order.Member, args.MapId, out var memberPosition) && TryGetPosition(order.Target, args.MapId, out var targetPosition))
                    worldHandle.DrawLine(memberPosition, targetPosition, SquadDebugOverlay.GetOrderColor(order.Kind));
            }

            if (frame.ChargePhase != null &&
                TryGetPosition(frame.ChargeStand, args.MapId, out var chargeStand) &&
                TryGetPosition(frame.ChargeStandOff, args.MapId, out var chargeStandOff))
            {
                args.WorldHandle.DrawCircle(chargeStand, 0.4f, Color.Red, false);
                args.WorldHandle.DrawLine(chargeStand, chargeStandOff, Color.Red);
            }

            if (TryGetPosition(frame.Target, args.MapId, out var hostilePosition))
                worldHandle.DrawCircle(hostilePosition, MarkerSize, TargetColor, filled: false);
        }
    }

    private void DrawLabels(in OverlayDrawArgs args)
    {
        _font ??= new VectorFont(_resourceCache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);

        foreach (var (frame, _) in _squadPlanDebugSystem.Frames.Values)
        {
            if (!TryGetPosition(frame.Target, args.MapId, out var hostilePosition))
                continue;

            _text.Clear();
            _text.Append($"{frame.State} {frame.Elapsed:F1}");
            _text.Append(frame.Timeout > 0f ? $"/{frame.Timeout:F0}s" : "s");
            _text.Append($"\n{frame.Reason}");

            if (frame.Steps.Count > 0)
            {
                _text.Append($"\nplan ({frame.Cost:F1}):");

                for (var i = 0; i < frame.Steps.Count; i++)
                {
                    _text.Append(i == frame.CurrentStep ? $" [{frame.Steps[i]}]" : $" {frame.Steps[i]}");
                }
            }

            if (frame.ChargePhase is { } chargePhase)
                _text.Append($"\ncharge: {chargePhase}");

            if (frame.Facts != NpcSquadFact.None)
                _text.Append($"\nknows: {frame.Facts}");

            if (frame.Banned.Count > 0)
                _text.Append($"\nfailed: {string.Join(", ", frame.Banned.Select(action => action.ToString()))}");

            var screenPosition = _eyeManager.WorldToScreen(hostilePosition) + new Vector2(12f, -12f);
            args.ScreenHandle.DrawString(_font, screenPosition, _text.ToString(), GetStateColor(frame.State));
        }
    }

    private static Color GetStateColor(NpcSquadPlanState state)
    {
        return state switch
        {
            NpcSquadPlanState.Grace => Color.Yellow,
            NpcSquadPlanState.Probing => Color.Cyan,
            NpcSquadPlanState.Running => Color.Orange,
            _ => Color.Gray,
        };
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
