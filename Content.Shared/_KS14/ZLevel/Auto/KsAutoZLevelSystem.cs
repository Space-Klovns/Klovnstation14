using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.ZLevel.Auto;

public sealed class KsAutoZLevelSystem : EntitySystem
{
    [Dependency] private readonly KsZLevelSystem _zLevelSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsAutoZLevelComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KsAutoZLevelComponent, EntityUnpausedEvent>(OnUnpaused);
    }

    private void OnStartup(Entity<KsAutoZLevelComponent> entity, ref ComponentStartup args)
    {
        if (Paused(entity.Owner))
            return;

        TryLink(entity);
    }

    private void OnUnpaused(Entity<KsAutoZLevelComponent> entity, ref EntityUnpausedEvent args)
    {
        TryLink(entity);
    }

    public void TryLink(Entity<KsAutoZLevelComponent> entity)
    {
        DebugTools.Assert(HasComp<MapComponent>(entity.Owner), "Auto z-level has no MapComponent");
        if (!HasComp<MapComponent>(entity.Owner))
        {
            Log.Error($"Auto z-level {ToPrettyString(entity.Owner)} has no MapComponent!");
            return;
        }

        var eqe = EntityQueryEnumerator<KsAutoZLevelComponent, KsZLevelComponent>();
        while (eqe.MoveNext(out var uid, out var component, out var zLevelComponent))
        {
            if (component.Id != entity.Comp.Id)
                continue;

            if (component.Location == entity.Comp.Location)
                Log.Warning($"KsAutoZLevelType of auto z-levels {ToPrettyString(entity.Owner)} and {uid} is the same! The location of the z-levels relative to each other will be determined by update order.");

            if (entity.Comp.Location == KsAutoZLevelType.Above)
                _zLevelSystem.AddZLevelDirectlyAbove((uid, zLevelComponent), entity.Owner);
            else
                _zLevelSystem.AddZLevelDirectlyUnder((uid, zLevelComponent), entity.Owner);

            RemComp(entity.Owner, entity.Comp);
            RemComp(uid, component);
            break;
        }
    }
}
