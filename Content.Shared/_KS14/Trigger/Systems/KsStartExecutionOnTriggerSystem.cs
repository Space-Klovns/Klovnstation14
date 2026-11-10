using Content.Shared._KS14.Execution;
using Content.Shared._KS14.Trigger.Components;
using Content.Shared.Execution;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Trigger;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._KS14.Trigger.Systems;

public sealed partial class KsStartExecutionOnTriggerSystem : XOnTriggerSystem<KsStartExecutionOnTriggerComponent>
{
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedGunExecutionSystem _gunExecutionSystem = default!;
    [Dependency] private SharedExecutionSystem _executionSystem = default!;

    protected override void OnTrigger(Entity<KsStartExecutionOnTriggerComponent> entity, EntityUid targetUid, ref TriggerEvent args)
    {
        if (!TryComp<HandsComponent>(targetUid, out var handsComponent))
            return;

        foreach (var handId in _handsSystem.EnumerateHands((targetUid, handsComponent)))
        {
            if (!_handsSystem.TryGetHeldItem((targetUid, handsComponent), handId, out var activeWeaponUid) ||
                !TryComp<GunComponent>(activeWeaponUid, out var gunComponent) & !TryComp<ExecutionComponent>(activeWeaponUid, out var executionComponent))
                continue;

            _handsSystem.SetActiveHand((targetUid, handsComponent), handId);
            TryForceExecutionWith(entity, (activeWeaponUid.Value, gunComponent, executionComponent), targetUid);

            break;
        }
    }

    private bool TryForceExecutionWith(Entity<KsStartExecutionOnTriggerComponent> entity, Entity<GunComponent?, ExecutionComponent?> weaponEntity, EntityUid targetUid)
    {
        if (weaponEntity.Comp1 is { } gunComponent)
        {
            if (entity.Comp.AutoHandle)
                RackIfNecessary((weaponEntity, gunComponent), userUid: entity /* intended */);

            _gunExecutionSystem.TryStartGunExecutionDoafter(weaponEntity.Owner, targetUid, targetUid, (float)entity.Comp.Duration.TotalSeconds);
            return true;
        }
        else if (weaponEntity.Comp2 is { } executionComponent)
        {
            _executionSystem.TryStartExecutionDoAfter(weaponEntity.Owner, targetUid, targetUid, executionComponent);
            return true;
        }

        return false;
    }

    private void RackIfNecessary(Entity<GunComponent> entity, EntityUid? userUid = null)
    {
        if (!TryComp<ChamberMagazineAmmoProviderComponent>(entity, out var chamberMagazineAmmoProviderComponent) ||
            !chamberMagazineAmmoProviderComponent.CanRack)
            return;

        var chamberEntity = _gunSystem.GetChamberEntity(entity);
        if (chamberMagazineAmmoProviderComponent.BoltClosed != false &&
            chamberEntity is not null)
            return;

        _gunSystem.UseChambered(entity, component: chamberMagazineAmmoProviderComponent, user: userUid);
    }
}
