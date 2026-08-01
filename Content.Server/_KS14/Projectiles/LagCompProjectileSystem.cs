using Content.Server.Movement.Components;
using Content.Server.Movement.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared._Trauma.Projectiles;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Server._KS14.Projectiles;

public sealed partial class ProjectileLagCompensationSystem : EntitySystem
{
    [Dependency] private LagCompensationSystem _lagCompensationSystem = default!;
    [Dependency] private PredictedProjectileSystem _projectile = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private RayCastSystem _rayCastSystem = default!;

    [Dependency] private EntityQuery<LagCompensationComponent> _lagCompensationQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerShotProjectileEvent>(OnShotProjectile);
    }

    // Oops! No Update Loop!

    private void OnShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (!_lagCompensationQuery.TryComp(args.User, out var lagCompensationComponent))
            return;

        EnsureComp<

        // add lag comp so it's fairer for high ping chuds
        var comp = EnsureComp<LagCompProjectileComponent>(args.Projectile);
        comp.ShooterSession = session;

        // this lets the client ignore the server-spawned projectile that it predicted shooting
        var ev = new ShotPredictedProjectileEvent()
        {
            Projectile = GetNetEntity(args.Projectile)
        };
        RaiseNetworkEvent(ev, session);
    }

    private void OnStartCollide(Entity<LagCompProjectileComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurEntity != ent.Owner || args.OurFixtureId != SharedFlyBySoundSystem.FlyByFixture)
            return;

        var target = args.OtherEntity;
        if (_lagQuery.HasComp(target))
            ent.Comp.Targets.Add(target);
    }

    private void OnEndCollide(Entity<LagCompProjectileComponent> ent, ref EndCollideEvent args)
    {
        if (args.OurEntity != ent.Owner || args.OurFixtureId != SharedFlyBySoundSystem.FlyByFixture)
            return;

        ent.Comp.Targets.Remove(args.OtherEntity);
    }
}
