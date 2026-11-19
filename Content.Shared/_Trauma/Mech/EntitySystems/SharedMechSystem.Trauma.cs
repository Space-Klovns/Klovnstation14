using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared._Trauma.Mech;

namespace Content.Shared.Mech.EntitySystems;

public abstract partial class SharedMechSystem
{
    private void InitializeTrauma()
    {
        SubscribeLocalEvent<MechEquipmentComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    // TODO: this has no reason to be here
    private void OnShotAttempted(EntityUid uid, MechEquipmentComponent component, ref ShotAttemptedEvent args)
    {
        if (component.EquipmentOwner is not {} mech ||
            !HasComp<MechComponent>(mech))
        {
            args.Cancel();
            return;
        }

        // TODO: this should not be in an attempt event
        var ev = new MechGunFiredEvent();
        RaiseLocalEvent(uid, ref ev);
    }
}
