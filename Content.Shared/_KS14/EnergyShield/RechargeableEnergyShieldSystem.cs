using Content.Shared.Damage.Systems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;

namespace Content.Shared._KS14.EnergyShield;

/// <summary>
///     Drains a <see cref="RechargeableEnergyShieldComponent"/>'s battery as it soaks damage, and keeps
///         the shield switched off while that battery is flat.
/// </summary>
/// <remarks>
///     Deactivation hangs off <see cref="ChargeChangedEvent"/> alone. An earlier version also re-checked
///         the same condition every tick and, when the shield was already off, hand-raised a synthetic
///         <see cref="ItemToggledEvent"/> - which fires every unrelated subscriber (visuals, sounds,
///         melee stats) for a state change that never happened.
/// </remarks>
public sealed partial class RechargeableEnergyShieldSystem : EntitySystem
{
    [Dependency] private SharedBatterySystem _batterySystem = default!;
    [Dependency] private ItemToggleSystem _itemToggleSystem = default!;
    [Dependency] private EntityQuery<BatteryComponent> _batteryQuery = default!;

    /// <summary>
    ///     Spends charge for the damage the shield just took.
    /// </summary>
    /// <remarks>
    ///     Healing is ignored rather than credited back as charge - putting the shield back together is
    ///         what the self-recharger is for.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnDamageDealt(Entity<RechargeableEnergyShieldComponent> entity, ref DamageDealtEvent args)
    {
        if (!_batteryQuery.TryComp(entity, out var batteryComponent))
            return;

        var chargeDamage = args.Damage.GetTotal().Float() * entity.Comp.DamageToChargeRatio;
        if (chargeDamage > 0f)
            _batterySystem.UseCharge((entity, batteryComponent), chargeDamage);
    }

    /// <summary>
    ///     Switches the shield off the moment its battery runs dry.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnChargeChanged(Entity<RechargeableEnergyShieldComponent> entity, ref ChargeChangedEvent args)
    {
        if (args.CurrentCharge > 0f)
            return;

        _itemToggleSystem.TryDeactivate(entity.Owner);
    }

    /// <summary>
    ///     Refuses to switch the shield on while its battery is flat.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnToggleAttempt(Entity<RechargeableEnergyShieldComponent> entity, ref ItemToggleActivateAttemptEvent args)
    {
        if (_batteryQuery.TryComp(entity, out var batteryComponent) &&
            _batterySystem.GetCharge((entity, batteryComponent)) > 0f)
            return;

        args.Cancelled = true;
        args.Popup = Loc.GetString("rechargeable-energy-shield-insufficient-charge");
    }
}
