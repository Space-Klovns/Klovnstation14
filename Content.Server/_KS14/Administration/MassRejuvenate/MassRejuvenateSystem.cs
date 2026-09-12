using Content.Shared.Administration.Systems;
using Robust.Shared.Player;

namespace Content.Server._KS14.Administration.MassRejuvenate;

public sealed partial class MassRejuvenateSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private RejuvenateSystem _rejuvenateSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MassRejuvenateMarkerComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<MassRejuvenateMarkerComponent> entity, ref MapInitEvent args)
    {
        RejuvenateNearbyEntities(entity);
    }

    private void RejuvenateNearbyEntities(Entity<MassRejuvenateMarkerComponent> markerEntity)
    {
        foreach (var targetUid in _entityLookupSystem.GetEntitiesInRange(Transform(markerEntity).Coordinates, markerEntity.Comp.Radius))
        {
            if (targetUid == markerEntity.Owner ||
                markerEntity.Comp.PlayerControlledOnly && !HasComp<ActorComponent>(targetUid))
                continue;

            _rejuvenateSystem.PerformRejuvenate(targetUid);
        }

        QueueDel(markerEntity);
    }
}