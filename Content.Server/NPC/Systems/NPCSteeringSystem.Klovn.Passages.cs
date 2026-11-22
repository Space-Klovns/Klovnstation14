// KS14: added in this fork
using System.Numerics;
using Content.Server._KS14.NPC.Passages;
using Content.Server.NPC.Components;
using Content.Shared.Movement.Components;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Getting to a destination on another map, by a passage: a ladder, a portal. See <see cref="KsNpcPassageSystem"/>.
/// </summary>
public sealed partial class NPCSteeringSystem
{
    [Dependency] private KsNpcPassageSystem _ksNpcPassageSystem = default!;

    /// <summary>
    ///     <see cref="Steer"/>, but for a destination on another map it steers for the passage that leads towards
    ///         that map, and takes it on getting there. Without one that does, it is <see cref="Steer"/>, which has no
    ///         path to another map.
    /// </summary>
    private void KsSteerByPassage(
        EntityUid uid,
        NPCSteeringComponent steering,
        InputMoverComponent mover,
        TransformComponent xform,
        float frameTime,
        TimeSpan curTime)
    {
        // A destination on its own map, as nearly all are.
        if (!TryComp(steering.Coordinates.EntityId, out TransformComponent? destinationTransform) ||
            destinationTransform.MapUid is not { } destinationMapUid ||
            destinationMapUid == xform.MapUid)
        {
            // Just come through the last passage: what is left of its path is on the map it left.
            if (_ksNpcPassageSystem.Clear(uid))
                KsDropPath(steering);

            Steer(uid, steering, mover, xform, frameTime, curTime);
            return;
        }

        if (!_ksNpcPassageSystem.TryGetPassage(uid, xform, destinationMapUid, out var passage, out var isNewPassage))
        {
            Steer(uid, steering, mover, xform, frameTime, curTime);
            return;
        }

        // Part way through taking it, a ladder that takes time to climb, say: it stands still for it.
        if (_ksNpcPassageSystem.IsWaiting(uid))
        {
            SetDirection(uid, mover, steering, Vector2.Zero, clear: false);
            return;
        }

        // Whatever path it has is to somewhere else.
        if (isNewPassage)
            KsDropPath(steering);

        // The passage is its destination for as long as this call and no longer: all of Steer goes by the
        //      destination, and whatever reads it between calls, its operator for one, gets the real one.
        var destination = steering.Coordinates;
        var range = steering.Range;
        var arriveOnLineOfSight = steering.ArriveOnLineOfSight;
        var arriveOnLineOfFire = steering.KsArriveOnLineOfFire;

        steering.Coordinates = Transform(passage).Coordinates;
        steering.Range = passage.Comp.Range;
        steering.ArriveOnLineOfSight = false;
        steering.KsArriveOnLineOfFire = false;

        Steer(uid, steering, mover, xform, frameTime, curTime);

        steering.Coordinates = destination;
        steering.Range = range;
        steering.ArriveOnLineOfSight = arriveOnLineOfSight;
        steering.KsArriveOnLineOfFire = arriveOnLineOfFire;

        if (steering.Status != SteeringStatus.InRange)
            return;

        // At the passage, which is not at its destination: it is still on its way.
        steering.Status = SteeringStatus.Moving;

        if (_ksNpcPassageSystem.Take(uid, passage))
        {
            KsDropPath(steering);
            return;
        }

        // Nothing took it up, so it is one that is walked into, and it is on the middle of it untaken: it walks on as
        //      it was going, out the other side, which is what crossing a portal's doorway is.
        SetDirection(uid, mover, steering, steering.LastSteerDirection, clear: false);
    }

    private static void KsDropPath(NPCSteeringComponent steering)
    {
        steering.PathfindToken?.Cancel();
        steering.PathfindToken = null;
        steering.CurrentPath.Clear();
    }
}
