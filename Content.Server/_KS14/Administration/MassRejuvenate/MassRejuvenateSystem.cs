using Content.Shared.Administration.Systems;
using Robust.Shared.Player;

namespace Content.Server._KS14.Administration.MassRejuvenate;

/// <summary>
///     Runs <see cref="MassRejuvenateMarkerComponent"/> markers: heal everything in range once, then
///         clean the marker up.
/// </summary>
public sealed partial class MassRejuvenateSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private RejuvenateSystem _rejuvenateSystem = default!;
    [Dependency] private EntityQuery<ActorComponent> _actorQuery = default!;

    /// <summary>
    ///     Fires the marker, then deletes it.
    /// </summary>
    /// <remarks>
    ///     Map init rather than component init: on a loading map, component init runs before the
    ///         entities around the marker are initialized and before any player is attached, which makes
    ///         <see cref="MassRejuvenateMarkerComponent.PlayerControlledOnly"/> silently heal nobody.
    ///     Deleting the marker from inside its own map-init handler is deliberate. <see cref="QueueDel"/>
    ///         defers the removal to the end of the tick, so the entity survives the rest of its own
    ///         initialization.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnMapInit(Entity<MassRejuvenateMarkerComponent> entity, ref MapInitEvent args)
    {
        RejuvenateNearbyEntities(entity);
        QueueDel(entity);
    }

    /// <summary>
    ///     Heals everything within <see cref="MassRejuvenateMarkerComponent.Radius"/> of the marker that
    ///         the marker is configured to care about.
    /// </summary>
    private void RejuvenateNearbyEntities(Entity<MassRejuvenateMarkerComponent> markerEntity)
    {
        // Default lookup flags, so entities inside containers are healed too - a marker over a locker
        // room is expected to reach whoever is in the lockers.
        foreach (var targetUid in _entityLookupSystem.GetEntitiesInRange(Transform(markerEntity).Coordinates, markerEntity.Comp.Radius))
        {
            if (targetUid == markerEntity.Owner ||
                markerEntity.Comp.PlayerControlledOnly && !_actorQuery.HasComp(targetUid))
                continue;

            _rejuvenateSystem.PerformRejuvenate(targetUid);
        }
    }
}
