using Content.Server.Atmos.Portable;
using Content.Shared._KS14.Atmos.Components;
using Content.Server.Body.Systems;
using Content.Shared.Atmos;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Atmos.EntitySystems;

public sealed class EvaporinGasSystem : EntitySystem
{
    [Dependency] private readonly ThirstSystem _thirstSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EvaporinMetabolizerComponent, InhaledGasEvent>(OnGasInhaled);
    }

    private void OnGasInhaled(Entity<EvaporinMetabolizerComponent> ent, ref InhaledGasEvent args)
    {
        var moles = args.Gas.GetMoles(Gas.Evaporin);
        if (moles <= Atmospherics.GasMinMoles)
            return;

        if (!TryComp<ThirstComponent>(ent, out var thirst))
            return;

        var bodyEntity = new Entity<ThirstComponent>(ent, thirst);

        _thirstSystem.ModifyThirst(bodyEntity.Owner, bodyEntity.Comp, -moles * ent.Comp.ThirstMultiplier);

        if (bodyEntity.Comp.CurrentThirstThreshold == ThirstThreshold.Dead)
        {
            // Scale damage by moles inhaled to make dense clouds more dangerous
            _damageableSystem.TryChangeDamage(bodyEntity.Owner, ent.Comp.Damage * moles, true, false);
        }
    }
}
