using Content.Shared._KS14.Weapons.Melee;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Random;

namespace Content.Shared._KS14.Weapons.Melee;

/// <summary>
/// Put the component on a weapon and itll be forcibly unwielded when hit on melee.
/// Target audience: cumbersome weapons like rifles
/// </summary>
public sealed class MeleeForceUnwieldWeaponSystem : EntitySystem
{
    [Dependency] private readonly SharedWieldableSystem _wieldable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeForceUnwieldWeaponComponent, AttackedEvent>(OnAttacked);
    }

    private void OnAttacked(Entity<MeleeForceUnwieldWeaponComponent> ent, ref AttackedEvent args)
    {
        // Only trigger if hit by a melee weapon
        if (!TryComp<MeleeWeaponComponent>(args.Used, out var meleeWeaponComp))
            return;

        var damageSpec = meleeWeaponComp.Damage;
        var prob = 0L;

        foreach (var (key, value) in damageSpec.DamageDict)
        {
            if (ent.Comp.UnwieldDict.ContainsKey(key))
                prob = Math.Clamp(Math.Max(prob, (long)(damageSpec.DamageDict[key] * ent.Comp.UnwieldDict[key])), 0L, 100L);
        }

        if (_random.NextFloat(100) > prob)
            return;

        // Evil KS14 hack
        // the weapon grants the MeleeForceUnwieldWeaponComponent to the guy holding it
        // then this subscribes to when this guy gets hit via the component as a proxy
        // then unwields all. since we dont have 3 hands this wont crossinteract badly

        var didUnwield = _wieldable.TryUnwieldAll(ent.Owner, force: true);

        if (!didUnwield)
            return;

        // Show popup to the target if it actually did unwield something
        var message = Loc.GetString("melee-force-unwield-popup");
        _popup.PopupEntity(message, ent.Owner, ent.Owner);
    }
}
