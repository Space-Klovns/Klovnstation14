using Content.Shared._KS14.Particles;
using Content.Shared.Damage.Systems;

namespace Content.Shared._KS14.Damage.DamagedParticles;

/// <inheritdoc cref="KsDamagedParticlesComponent"/>
public sealed partial class KsDamagedParticlesSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private KsSharedParticlesSystem _particlesSystem = default!;

    // By-value class event: no Entity<T> handler for it (RA0054).
    [SubscribeLocalEvent]
    private void OnDamageChanged(EntityUid uid, KsDamagedParticlesComponent component, DamageChangedEvent args)
    {
        _particlesSystem.SetEmitting(uid, _damageableSystem.GetTotalDamage((uid, args.Damageable)) >= component.Threshold);
    }
}
