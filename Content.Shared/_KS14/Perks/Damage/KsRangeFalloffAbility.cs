using Content.Shared._KS14.Weapons.Hitscan;
using Content.Shared._KS14.Weapons.Ranged;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     Shots lose damage over distance: full damage up to <see cref="StartRange"/> tiles, falling linearly to
///         <see cref="MinMultiplier"/> at <see cref="EndRange"/> and flat beyond. On top of any falloff the bullet has
///         of its own.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsRangeFalloffAbilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public float StartRange = 10f;

    [DataField, AutoNetworkedField]
    public float EndRange = 30f;

    [DataField, AutoNetworkedField]
    public float MinMultiplier = 0.5f;
}

public sealed partial class KsRangeFalloffSystem : EntitySystem
{
    [Dependency] private KsPerkGunSystem _perkGunSystem = default!;

    [SubscribeLocalEvent]
    private void OnFired(Entity<KsRangeFalloffAbilityComponent> entity, ref KsShooterFiredGunEvent args)
    {
        foreach (var projectileUid in args.ProjectileUids)
            _perkGunSystem.StampOrigin(projectileUid, entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnShotRangeModify(Entity<KsRangeFalloffAbilityComponent> entity, ref KsModifyPerkShotRangeDamageEvent args)
    {
        args.Multiplier *= GetMultiplier(entity.Comp, args.Distance);
    }

    [SubscribeLocalEvent]
    private void OnHitscanModify(Entity<KsRangeFalloffAbilityComponent> entity, ref KsHitscanDamageModifyEvent args)
    {
        if (_perkGunSystem.GetHitscanDistance(args) is { } distance)
            args.Damage *= GetMultiplier(entity.Comp, distance);
    }

    public static float GetMultiplier(KsRangeFalloffAbilityComponent component, float distance)
    {
        if (distance <= component.StartRange)
            return 1f;

        if (distance >= component.EndRange || component.EndRange <= component.StartRange)
            return component.MinMultiplier;

        return 1f + (component.MinMultiplier - 1f) * (distance - component.StartRange) / (component.EndRange - component.StartRange);
    }
}
