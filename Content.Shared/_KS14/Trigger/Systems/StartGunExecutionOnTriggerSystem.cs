using Content.Shared._KS14.Execution;
using Content.Shared._KS14.Trigger.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Trigger;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._KS14.Trigger.Systems;

public sealed partial class StartGunExecutionOnTriggerSystem : XOnTriggerSystem<StartGunExecutionOnTriggerComponent>
{
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedGunExecutionSystem _gunExecutionSystem = default!;

    protected override void OnTrigger(Entity<StartGunExecutionOnTriggerComponent> entity, EntityUid targetUid, ref TriggerEvent args)
    {
        if (!_handsSystem.TryGetActiveItem(targetUid, out var weaponUid) ||
            !TryComp<GunComponent>(weaponUid, out var gunComponent))
            return;

        if (entity.Comp.AutoHandle)
            RackIfNecessary((weaponUid.Value, gunComponent), userUid: entity /* intended */);

        _gunExecutionSystem.TryStartGunExecutionDoafter(weaponUid.Value, targetUid, targetUid, (float)entity.Comp.Duration.TotalSeconds);
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
