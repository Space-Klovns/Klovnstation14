using Content.Shared._KS14.Particles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Weapons.GunSmoke;

/// <inheritdoc cref="KsGunOverheatSmokeComponent"/>
public sealed partial class KsGunOverheatSmokeSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsSharedParticlesSystem _particlesSystem = default!;

    [SubscribeLocalEvent]
    private void OnGunShot(Entity<KsGunOverheatSmokeComponent> entity, ref GunShotEvent args)
    {
        var now = _gameTiming.CurTime;
        var component = entity.Comp;
        component.Heat = GetHeat(component, now) + component.HeatPerShot;
        component.LastShot = now;

        if (component.SmokeUntil == null && component.Heat < component.SmokeAt)
            return;

        // Smoking, from now or still: until it would cool below StopBelow with no more shots.
        component.SmokeUntil = now + TimeSpan.FromSeconds(Math.Max(0f, component.Heat - component.StopBelow) / component.CoolPerSecond);
        EnsureComp<KsGunOverheatSmokingComponent>(entity);
        _particlesSystem.SetEmitting(entity.Owner, true);
    }

    public float GetHeat(KsGunOverheatSmokeComponent component, TimeSpan now)
    {
        var cooled = (float)(now - component.LastShot).TotalSeconds * component.CoolPerSecond;
        return Math.Max(0f, component.Heat - cooled);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Only the guns smoking now.
        var now = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsGunOverheatSmokingComponent, KsGunOverheatSmokeComponent>();
        while (query.MoveNext(out var uid, out _, out var component))
        {
            if (component.SmokeUntil is { } smokeUntil && now < smokeUntil)
                continue;

            component.SmokeUntil = null;
            _particlesSystem.SetEmitting(uid, false);
            RemCompDeferred<KsGunOverheatSmokingComponent>(uid);
        }
    }
}
