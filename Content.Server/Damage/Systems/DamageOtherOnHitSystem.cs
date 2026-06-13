using Content.Shared.Damage.Systems;

namespace Content.Server.Damage.Systems;

<<<<<<< HEAD
// Trauma - moved here everything to shared
public sealed class DamageOtherOnHitSystem : SharedDamageOtherOnHitSystem;
=======
public sealed partial class DamageOtherOnHitSystem : SharedDamageOtherOnHitSystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private GunSystem _guns = default!;
    [Dependency] private Shared.Damage.Systems.DamageableSystem _damageable = default!;
    [Dependency] private SharedCameraRecoilSystem _sharedCameraRecoil = default!;
    [Dependency] private SharedColorFlashEffectSystem _color = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageOtherOnHitComponent, ThrowDoHitEvent>(OnDoHit);
    }

    private void OnDoHit(EntityUid uid, DamageOtherOnHitComponent component, ThrowDoHitEvent args)
    {
        if (TerminatingOrDeleted(args.Target))
            return;

        var dmg = _damageable.ChangeDamage(args.Target, component.Damage * _damageable.UniversalThrownDamageModifier, component.IgnoreResistances, origin: args.Component.Thrower);

        // Log damage only for mobs. Useful for when people throw spears at each other, but also avoids log-spam when explosions send glass shards flying.
        if (HasComp<MobStateComponent>(args.Target))
            _adminLogger.Add(LogType.ThrowHit, $"{ToPrettyString(args.Target):target} received {dmg.GetTotal():damage} damage from collision");

        if (!dmg.Empty)
        {
            _color.RaiseEffect(Color.Red, [args.Target], Filter.Pvs(args.Target, entityManager: EntityManager));
        }

        _guns.PlayImpactSound(args.Target, dmg, null, false);
        if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0f)
        {
            var direction = body.LinearVelocity.Normalized();
            _sharedCameraRecoil.KickCamera(args.Target, direction);
        }
    }
}
>>>>>>> upstream/master
