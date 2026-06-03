using Content.Shared._KS14.ZLevel;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._KS14.ZLevel.Auto;

public sealed class KsAutoZLevelSystem : EntitySystem
{
    [Dependency] private readonly KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoaderSystem = default!;

    private static readonly DeserializationOptions DeserializationOptions = DeserializationOptions.Default with
    {
        InitializeMaps = true,
        PauseMaps = false
    };

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

        if (entity.Comp.MapPath is { } mapPath)
        {
            if (!_mapLoaderSystem.TryLoadMap(mapPath, out var mapEntity, out _, options: DeserializationOptions))
            {
                Log.Error($"Failed to load map at path '{mapPath}' for auto-zlevel {ToPrettyString(entity.Owner)}");
                DebugTools.Assert($"Failed to load map at path '{mapPath}' for auto-zlevel {ToPrettyString(entity.Owner)}");

                RemComp(entity, entity.Comp);
                return;
            }

            LinkWith(entity, mapEntity.Value.Owner);
        }
        else
        {
            var eqe = EntityQueryEnumerator<KsAutoZLevelComponent, MapComponent>();
            while (eqe.MoveNext(out var uid, out var component, out _))
            {
                if (component.Id != entity.Comp.Id ||
                    uid == entity.Owner)
                    continue;

                LinkWith(entity, uid);
                break;
            }
        }
    }

    private void LinkWith(Entity<KsAutoZLevelComponent> entity, Entity<KsAutoZLevelComponent?> otherEntity)
    {
        if (otherEntity.Comp?.Location == entity.Comp.Location)
            Log.Warning($"KsAutoZLevelType of auto z-levels {ToPrettyString(entity.Owner)} and {ToPrettyString(otherEntity.Owner)} is the same! The location of the z-levels relative to each other will be determined by update order.");

        if (entity.Comp.Location == KsAutoZLevelType.Above)
            _zLevelSystem.AddZLevelDirectlyAbove(
                (otherEntity.Owner, EnsureComp<KsZLevelComponent>(otherEntity.Owner)),
                (entity.Owner, EnsureComp<KsZLevelComponent>(entity.Owner))
            );
        else
            _zLevelSystem.AddZLevelDirectlyUnder(
                (otherEntity.Owner, EnsureComp<KsZLevelComponent>(otherEntity.Owner)),
                (entity.Owner, EnsureComp<KsZLevelComponent>(entity.Owner))
            );

        RemComp(entity.Owner, entity.Comp);

        if (Resolve(otherEntity, ref otherEntity.Comp, logMissing: false))
            RemComp(otherEntity.Owner, otherEntity.Comp);
    }
}
