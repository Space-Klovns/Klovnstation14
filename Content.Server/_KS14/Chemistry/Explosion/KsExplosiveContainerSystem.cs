using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;

namespace Content.Server._KS14.Chemistry.Explosion;

/// <summary>
///     Sets off <see cref="KsExplosiveContainerComponent"/>s once they have taken enough heat or piercing damage.
/// </summary>
public sealed partial class KsExplosiveContainerSystem : EntitySystem
{
    [Dependency] private KsSolutionExplosionSystem _solutionExplosionSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;

    // DamageChangedEvent is a class, so the handler can't take an Entity<T> (RA0054).
    [SubscribeLocalEvent]
    private void OnDamageChanged(EntityUid uid, KsExplosiveContainerComponent component, DamageChangedEvent args)
    {
        if (!args.DamageIncreased || component.LifeStage > ComponentLifeStage.Running)
            return;

        var allDamage = _damageableSystem.GetAllDamage((uid, args.Damageable));
        var damage = FixedPoint2.Zero;
        foreach (var damageType in component.DamageTypes)
        {
            if (allDamage.DamageDict.TryGetValue(damageType, out var amount))
                damage += amount;
        }

        if ((float)damage < component.DamageThreshold)
            return;

        // Once, whether or not it held anything that explodes: what's left after a spill is not worth checking again.
        RemCompDeferred(uid, component);
        _solutionExplosionSystem.TryExplodeSolution(uid, component.Solution, args.Origin);
    }
}
