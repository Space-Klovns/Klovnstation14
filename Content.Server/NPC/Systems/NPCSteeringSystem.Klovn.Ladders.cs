// KS14: added in this fork
using System.Numerics;
using Content.Server._KS14.NPC.Ladders;
using Content.Server.NPC.Components;
using Content.Shared.Movement.Components;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Getting to a destination on another map, by ladder. See <see cref="KsNpcLadderSystem"/>.
/// </summary>
public sealed partial class NPCSteeringSystem
{
    [Dependency] private KsNpcLadderSystem _ksNpcLadderSystem = default!;

    /// <summary>
    ///     <see cref="Steer"/>, but for a destination on another map it steers for the ladder that leads towards that
    ///         map, and climbs it on getting there. Without a ladder that does, it is <see cref="Steer"/>, which has no
    ///         path to another map.
    /// </summary>
    private void KsSteerByLadder(
        EntityUid uid,
        NPCSteeringComponent steering,
        InputMoverComponent mover,
        TransformComponent xform,
        float frameTime,
        TimeSpan curTime)
    {
        // A destination on its own map, as nearly all are: this is all it costs then.
        if (!TryComp(steering.Coordinates.EntityId, out TransformComponent? destinationTransform) ||
            destinationTransform.MapUid is not { } destinationMapUid ||
            destinationMapUid == xform.MapUid ||
            !_ksNpcLadderSystem.TryGetLadder(uid, xform, destinationMapUid, out var ladder, out var isNewLadder))
        {
            Steer(uid, steering, mover, xform, frameTime, curTime);
            return;
        }

        // A climb that takes time: it stands still for it.
        if (_ksNpcLadderSystem.IsClimbing(uid))
        {
            SetDirection(uid, mover, steering, Vector2.Zero, clear: false);
            return;
        }

        // Whatever path it has is to somewhere else.
        if (isNewLadder)
            KsDropPath(steering);

        // The ladder is its destination for as long as this call and no longer: all of Steer goes by the destination,
        //      and whatever reads it between calls, its operator for one, gets the real one.
        var destination = steering.Coordinates;
        var range = steering.Range;
        var arriveOnLineOfSight = steering.ArriveOnLineOfSight;
        var arriveOnLineOfFire = steering.KsArriveOnLineOfFire;

        steering.Coordinates = Transform(ladder).Coordinates;
        steering.Range = KsNpcLadderSystem.ClimbRange;
        steering.ArriveOnLineOfSight = false;
        steering.KsArriveOnLineOfFire = false;

        Steer(uid, steering, mover, xform, frameTime, curTime);

        steering.Coordinates = destination;
        steering.Range = range;
        steering.ArriveOnLineOfSight = arriveOnLineOfSight;
        steering.KsArriveOnLineOfFire = arriveOnLineOfFire;

        if (steering.Status != SteeringStatus.InRange)
            return;

        // At the ladder, which is not at its destination: it is still on its way.
        steering.Status = SteeringStatus.Moving;
        KsDropPath(steering);
        _ksNpcLadderSystem.Climb(uid, ladder);
    }

    private static void KsDropPath(NPCSteeringComponent steering)
    {
        steering.PathfindToken?.Cancel();
        steering.PathfindToken = null;
        steering.CurrentPath.Clear();
    }
}
