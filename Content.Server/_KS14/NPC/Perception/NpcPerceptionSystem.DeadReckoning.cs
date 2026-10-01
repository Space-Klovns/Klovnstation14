using Content.Shared._KS14.NPC;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Server._KS14.NPC.Perception;

public sealed partial class NpcPerceptionSystem
{
    /// <summary>
    ///     How far short of a wall a guessed position stops, so it is never inside one.
    /// </summary>
    private const float DeadReckoningWallMargin = 0.5f;

    /// <summary>
    ///     Where <paramref name="observer"/> guesses a hostile it has lost has got to by now: where it was last seen,
    ///         carried on along its last velocity for up to <see cref="NpcPerceptionComponent.DeadReckoningTime"/>,
    ///         stopping short of the first wall in the way.
    /// </summary>
    /// <remarks>
    ///     Worked out when asked, not every update: only the HTN branch that acts on it asks.
    /// </remarks>
    public bool TryGetPredictedCoordinates(Entity<NpcPerceptionComponent?> observer, EntityUid targetUid, out EntityCoordinates predictedCoordinates)
    {
        predictedCoordinates = default;

        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false) ||
            !observer.Comp.Contacts.TryGetValue(targetUid, out var contact) ||
            TerminatingOrDeleted(contact.LastKnownCoordinates.EntityId))
            return false;

        predictedCoordinates = contact.LastKnownCoordinates;

        var age = _gameTiming.CurTime - contact.LastSeen;
        if (age > observer.Comp.DeadReckoningTime)
            age = observer.Comp.DeadReckoningTime;

        var offset = contact.LastKnownVelocity * (float)age.TotalSeconds;
        var distance = offset.Length();
        if (distance < 0.05f)
            return true;

        var lastKnownMapCoordinates = _transformSystem.ToMapCoordinates(contact.LastKnownCoordinates);
        var direction = offset / distance;
        var ray = new CollisionRay(lastKnownMapCoordinates.Position, direction, (int)CollisionGroup.Impassable);

        // Every hit, not the first: the first is the first the broadphase came across, not the nearest.
        var nearestHit = distance + DeadReckoningWallMargin;
        foreach (var hit in _physicsSystem.IntersectRay(lastKnownMapCoordinates.MapId, ray, distance, returnOnFirstHit: false))
        {
            nearestHit = MathF.Min(nearestHit, hit.Distance);
        }

        distance = MathF.Max(0f, MathF.Min(distance, nearestHit - DeadReckoningWallMargin));

        var predictedMapCoordinates = lastKnownMapCoordinates.Offset(direction * distance);
        predictedCoordinates = _transformSystem.ToCoordinates(contact.LastKnownCoordinates.EntityId, predictedMapCoordinates);
        return true;
    }
}
