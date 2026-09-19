using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Trigger;

namespace Content.Shared._KS14.Trigger.Components.Effects;

public sealed partial class EntityStorageOnTriggerSystem : XOnTriggerSystem<EntityStorageOnTriggerComponent>
{
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;

    protected override void OnTrigger(Entity<EntityStorageOnTriggerComponent> entity, EntityUid targetUid, ref TriggerEvent args)
    {
        if (!TryComp<EntityStorageComponent>(entity, out var entityStorageComponent))
            return;

        var mode = entity.Comp.Mode;
        if (entity.Comp.Mode == StorageAction.Toggle)
            mode = entityStorageComponent.Open ? StorageAction.Close : StorageAction.Open;

        var userUid = args.User ?? entity;
        switch (mode)
        {
            case StorageAction.Open:
                if (!entity.Comp.Force &&
                    !_entityStorageSystem.CanOpen(userUid, targetUid, silent: true /* intentional */, component: entityStorageComponent))
                    return;

                _entityStorageSystem.OpenStorage(targetUid, component: entityStorageComponent);
                args.Handled = true;
                break;
            case StorageAction.Close:
                if (!entity.Comp.Force &&
                    !_entityStorageSystem.CanClose(targetUid, userUid, silent: true /* intentional */))
                    return;

                _entityStorageSystem.CloseStorage(targetUid, component: entityStorageComponent);
                args.Handled = true;
                break;
        }
    }
}
