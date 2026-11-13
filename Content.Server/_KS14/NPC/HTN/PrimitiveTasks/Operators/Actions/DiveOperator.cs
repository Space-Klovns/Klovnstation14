using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared.Physics;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Actions;

/// <summary>
///     Dives towards <see cref="DestinationKey"/> with the world-targeted jump action <see cref="KsBaseActionOperator.Id"/>:
///         as far as <see cref="MaxDistance"/>, stopping short of the first thing the owner would crash into. Fails to plan
///         if the owner has no such action, it is on cooldown, or there is no dive of at least <see cref="MinDistance"/> -
///         a dive into a wall is not getting away - so make it optional.
/// </summary>
/// <remarks>
///     <para>
///         The jump goes all the way to its target, however far that is, which is why the target is worked out here and
///             is never the destination itself.
///     </para>
///     <para>
///         Never across a friend's line of fire (<see cref="NpcGunfireSystem.CrossesFire"/>): a dive is far too fast for
///             the shooter to allow for, so the diver does. Nor across anyone else's if there is a way round. Straight at
///             the destination if that is clear; otherwise turned off it by each of <see cref="SideTurns"/> in turn, to
///             whichever side lands nearer the destination. With no way clear of hostile fire, the same again minding
///             only friendly fire. Asked when planning, and again as it goes: someone may have opened fire since.
///     </para>
/// </remarks>
public sealed partial class DiveOperator : KsBaseActionOperator
{
    [Dependency] private NpcGunfireSystem _npcGunfireSystem = default!;
    [Dependency] private NpcLineOfSightSystem _npcLineOfSightSystem = default!;
    [Dependency] private NpcSquadFireLaneSystem _npcSquadFireLaneSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [DataField] public string DestinationKey = "RetreatTargetCoordinates";

    /// <summary>
    ///     Where the dive lands, worked out while planning.
    /// </summary>
    [DataField] public string Key = "DiveCoordinates";

    [DataField] public float MaxDistance = 3.5f;

    [DataField] public float MinDistance = 1.5f;

    /// <summary>
    ///     How far short of an obstacle the dive stops.
    /// </summary>
    [DataField] public float ObstacleMargin = 0.6f;

    /// <summary>
    ///     How far off straight at the destination a dive may be turned, either way, to keep out of a line of fire: tried in
    ///         order. In degrees in YAML. Kept under a right angle, so a turned dive still gets it some way towards the
    ///         destination: at 60 degrees, half its length.
    /// </summary>
    [DataField] public List<Angle> SideTurns = new() { Angle.FromDegrees(30), Angle.FromDegrees(60) };

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!TryGetValidAction(blackboard, out var ownerUid, out _) ||
            !blackboard.TryGetCoordinates(DestinationKey, EntityManager, out var destinationCoordinates) ||
            !TryGetDivePoint(ownerUid, destinationCoordinates, out var diveCoordinates))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { Key, diveCoordinates },
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetCoordinates(Key, EntityManager, out var diveCoordinates))
            return HTNOperatorStatus.Failed;

        // Someone has opened fire across it since it was planned: another way, if there is one.
        var ownerUid = blackboard.GetOwner();
        if (CrossesFire(ownerUid,
                _transformSystem.GetMapCoordinates(ownerUid),
                _transformSystem.ToMapCoordinates(diveCoordinates),
                NpcFireSide.Friendly) &&
            !(blackboard.TryGetCoordinates(DestinationKey, EntityManager, out var destinationCoordinates) &&
              TryGetDivePoint(ownerUid, destinationCoordinates, out diveCoordinates)))
            return HTNOperatorStatus.Failed;

        if (!TryPerformWorldAction(blackboard, diveCoordinates, throwOnWrongEvent: false))
            return HTNOperatorStatus.Failed;

        return HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.Remove<EntityCoordinates>(Key);
    }

    private bool TryGetDivePoint(EntityUid ownerUid, EntityCoordinates destinationCoordinates, out EntityCoordinates diveCoordinates)
    {
        diveCoordinates = default;

        var ownerMapCoordinates = _transformSystem.GetMapCoordinates(ownerUid);
        var destinationMapCoordinates = _transformSystem.ToMapCoordinates(destinationCoordinates);

        if (ownerMapCoordinates.MapId != destinationMapCoordinates.MapId)
            return false;

        var offset = destinationMapCoordinates.Position - ownerMapCoordinates.Position;
        var length = offset.Length();
        var distance = MathF.Min(length, MaxDistance);
        if (distance < MinDistance)
            return false;

        var straight = offset / length;

        // Turning off is for keeping out of fire, not for getting round walls: with no room straight ahead, no dive.
        if (!TryDiveAlong(ownerUid, ownerMapCoordinates, straight, distance, NpcFireSide.None, out _))
            return false;

        // Clear of all fire if it can be; failing that, clear of its friends' at least.
        if (!TryFindDive(ownerUid, ownerMapCoordinates, destinationMapCoordinates.Position, straight, distance, NpcFireSide.Any, out var landing) &&
            !TryFindDive(ownerUid, ownerMapCoordinates, destinationMapCoordinates.Position, straight, distance, NpcFireSide.Friendly, out landing))
            return false;

        diveCoordinates = _transformSystem.ToCoordinates(landing);
        return true;
    }

    /// <summary>
    ///     A dive clear of <paramref name="avoided"/>'s fire: straight along <paramref name="straight"/> if that is, else the
    ///         least turn of <see cref="SideTurns"/> that is, to the side landing nearer <paramref name="destination"/>.
    /// </summary>
    private bool TryFindDive(EntityUid ownerUid,
        MapCoordinates from,
        Vector2 destination,
        Vector2 straight,
        float distance,
        NpcFireSide avoided,
        out MapCoordinates landing)
    {
        if (TryDiveAlong(ownerUid, from, straight, distance, avoided, out landing))
            return true;

        foreach (var turn in SideTurns)
        {
            var leftClear = TryDiveAlong(ownerUid, from, turn.RotateVec(straight), distance, avoided, out var left);
            var rightClear = TryDiveAlong(ownerUid, from, (-turn).RotateVec(straight), distance, avoided, out var right);

            if (!leftClear && !rightClear)
                continue;

            landing = leftClear && (!rightClear ||
                (left.Position - destination).LengthSquared() <= (right.Position - destination).LengthSquared())
                ? left
                : right;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Where a dive from <paramref name="from"/> along <paramref name="direction"/>, up to <paramref name="distance"/>,
    ///         lands: short of the first thing in the way, at least <see cref="MinDistance"/> off, and clear of
    ///         <paramref name="avoided"/>'s fire.
    /// </summary>
    private bool TryDiveAlong(EntityUid ownerUid,
        MapCoordinates from,
        Vector2 direction,
        float distance,
        NpcFireSide avoided,
        out MapCoordinates landing)
    {
        landing = default;

        var clearDistance = _npcLineOfSightSystem.GetClearDistance(from,
            direction,
            distance + ObstacleMargin,
            (int) CollisionGroup.MobMask);

        distance = MathF.Min(distance, clearDistance - ObstacleMargin);
        if (distance < MinDistance)
            return false;

        landing = from.Offset(direction * distance);
        return !CrossesFire(ownerUid, from, landing, avoided);
    }

    private bool CrossesFire(EntityUid ownerUid, MapCoordinates from, MapCoordinates to, NpcFireSide sides)
    {
        var clearance = _npcSquadFireLaneSystem.GetBodyRadius(ownerUid) + NpcGunfireSystem.DefaultFireMargin;
        return _npcGunfireSystem.CrossesFire(ownerUid, from, to, sides, clearance);
    }
}
