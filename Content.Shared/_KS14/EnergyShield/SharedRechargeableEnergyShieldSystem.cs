using Content.Shared.Damage;
using Content.Shared.Damage.Systems; // KS14
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Power; // KS14
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.EnergyShield;

public sealed partial class SharedRechargeableEnergyShieldSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedBatterySystem _batterySystem = default!;
    [Dependency] private ItemToggleSystem _itemToggleSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RechargeableEnergyShieldComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<RechargeableEnergyShieldComponent, ChargeChangedEvent>(OnChargeChanged);
        SubscribeLocalEvent<RechargeableEnergyShieldComponent, ItemToggleActivateAttemptEvent>(OnToggleAttempt);
    }

    public override void Update(float frameTime)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<RechargeableEnergyShieldComponent, BatteryComponent>();
        while (query.MoveNext(out var uid, out var rechargeableComponent, out var batteryComponent))
        {
            var chargeDepleted = _batterySystem.GetCharge((uid, batteryComponent)) <= 0f;
            if (rechargeableComponent.ChargeDepleted != chargeDepleted)
            {
                rechargeableComponent.ChargeDepleted = chargeDepleted;
                Dirty(uid, rechargeableComponent);
            }

            if (!chargeDepleted)
            {
                rechargeableComponent.ShutdownHandled = false;
                continue;
            }

            if (_itemToggleSystem.IsActivated(uid))
            {
                _itemToggleSystem.TryDeactivate(uid);
            }
            else if (!rechargeableComponent.ShutdownHandled)
            {
                var toggleEvent = new ItemToggledEvent(Predicted: true, Activated: false, User: null);
                RaiseLocalEvent(uid, ref toggleEvent);
            }

            rechargeableComponent.ShutdownHandled = true;
        }
    }

    private void OnDamageChanged(Entity<RechargeableEnergyShieldComponent> entity, ref DamageChangedEvent args)
    {
        if (args.DamageDelta == null ||
            !TryComp<BatteryComponent>(entity, out var batteryComponent))
            return;

        var chargeDamage = args.DamageDelta.GetTotal().Float();
        if (chargeDamage > 0f)
            _batterySystem.UseCharge((entity, batteryComponent), chargeDamage);
    }

    private void OnChargeChanged(Entity<RechargeableEnergyShieldComponent> entity, ref ChargeChangedEvent args)
    {
        if (args.CurrentCharge > 0f)
            return;

        entity.Comp.ChargeDepleted = true;
        Dirty(entity);
        _itemToggleSystem.TryDeactivate(entity.Owner);
    }
    private void OnToggleAttempt(Entity<RechargeableEnergyShieldComponent> entity, ref ItemToggleActivateAttemptEvent args)
    {
        if (!TryComp<BatteryComponent>(entity, out var batteryComponent) ||
            _batterySystem.GetCharge((entity, batteryComponent)) <= 0f)
        {
            args.Cancelled = true;
            args.Popup = Loc.GetString("stunbaton-component-low-charge");
        }
    }
}
