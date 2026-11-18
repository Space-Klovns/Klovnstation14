using Content.Shared._KS14.Weapons.Hitscan;
using Content.Shared._KS14.Weapons.Ranged;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     Shots hit harder the closer they land: the full bonus up to <see cref="FullBonusRange"/> tiles, falling
///         linearly to none at <see cref="NoBonusRange"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsCloseRangeBonusAbilityComponent : Component
{
    /// <summary>
    ///     Damage added at point blank, as a fraction.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxBonus = 0.25f;

    [DataField, AutoNetworkedField]
    public float FullBonusRange = 2f;

    [DataField, AutoNetworkedField]
    public float NoBonusRange = 10f;
}

public sealed partial class KsCloseRangeBonusSystem : EntitySystem
{
    [Dependency] private KsPerkGunSystem _perkGunSystem = default!;

    [SubscribeLocalEvent]
    private void OnFired(Entity<KsCloseRangeBonusAbilityComponent> entity, ref KsShooterFiredGunEvent args)
    {
        foreach (var projectileUid in args.ProjectileUids)
            _perkGunSystem.StampOrigin(projectileUid, entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnShotRangeModify(Entity<KsCloseRangeBonusAbilityComponent> entity, ref KsModifyPerkShotRangeDamageEvent args)
    {
        args.Multiplier *= GetMultiplier(entity.Comp, args.Distance);
    }

    [SubscribeLocalEvent]
    private void OnHitscanModify(Entity<KsCloseRangeBonusAbilityComponent> entity, ref KsHitscanDamageModifyEvent args)
    {
        if (_perkGunSystem.GetHitscanDistance(args) is { } distance)
            args.Damage *= GetMultiplier(entity.Comp, distance);
    }

    public static float GetMultiplier(KsCloseRangeBonusAbilityComponent component, float distance)
    {
        if (distance <= component.FullBonusRange)
            return 1f + component.MaxBonus;

        if (distance >= component.NoBonusRange || component.NoBonusRange <= component.FullBonusRange)
            return 1f;

        return 1f + component.MaxBonus * (component.NoBonusRange - distance) / (component.NoBonusRange - component.FullBonusRange);
    }
}
