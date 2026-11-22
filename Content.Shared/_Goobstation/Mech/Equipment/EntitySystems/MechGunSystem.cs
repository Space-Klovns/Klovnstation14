// KS14: moved from Content.Server, so the pilot's client predicts the top-up of the shot it predicts
using Content.Shared.Mech.EntitySystems; // KS14: Content.Server.Mech.Systems -> shared
using Content.Shared._Trauma.Mech;
using Content.Shared.Power.EntitySystems; // KS14: Content.Server.Power.EntitySystems -> shared
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Power.Components;

namespace Content.Shared.Mech.Equipment.EntitySystems; // KS14: Content.Server -> Content.Shared
public sealed partial class MechGunSystem : EntitySystem
{
    [Dependency] private SharedMechSystem _mech = default!; // KS14: MechSystem -> SharedMechSystem
    [Dependency] private SharedBatterySystem _battery = default!; // KS14: BatterySystem -> SharedBatterySystem

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MechEquipmentComponent, MechGunFiredEvent>(OnGunFired);
    }

    private void OnGunFired(EntityUid uid, MechEquipmentComponent component, ref MechGunFiredEvent args)
    {
        if (!component.EquipmentOwner.HasValue)
            return;

        if (!TryComp<MechComponent>(component.EquipmentOwner.Value, out var mech))
            return;

        if (TryComp<BatteryComponent>(uid, out var battery))
        {
            ChargeGunBattery(uid, battery);
            return;
        }
    }

    private void ChargeGunBattery(EntityUid uid, BatteryComponent component)
    {
        if (!TryComp<MechEquipmentComponent>(uid, out var mechEquipment) || !mechEquipment.EquipmentOwner.HasValue)
            return;

        if (!TryComp<MechComponent>(mechEquipment.EquipmentOwner.Value, out var mech))
            return;

        var maxCharge = component.MaxCharge;
        var currentCharge = component.LastCharge;

        var chargeDelta = maxCharge - currentCharge;

        // TODO: The battery charge of the mech would be spent directly when fired.
        if (chargeDelta <= 0 || mech.Energy - chargeDelta < 0)
            return;

        if (!_mech.TryChangeEnergy(mechEquipment.EquipmentOwner.Value, -chargeDelta, mech))
            return;

        _battery.SetCharge((uid, component), component.MaxCharge);
    }
}
