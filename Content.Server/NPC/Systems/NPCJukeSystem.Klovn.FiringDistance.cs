using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.Examine;
using Content.Server.Interaction;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCJukeSystem : EntitySystem
{
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;

    [Dependency] private EntityQuery<CartridgeAmmoComponent> _cartridgeAmmoQuery = default!;
    [Dependency] private EntityQuery<ProjectileSpreadComponent> _projectileSpreadQuery = default!;

    /// <summary>
    ///     The whole cone <paramref name="gunEntity"/>'s next shot can land in, and how many projectiles it throws
    ///         into it: the gun's own spread, plus a pellet spread if the ammo it will fire next splits into
    ///         several projectiles (buckshot is one cartridge whose projectile spawns the pellets). With no ammo
    ///         to look at, just the gun's spread and one projectile.
    /// </summary>
    public (Angle Spread, int Projectiles) GetShotSpread(Entity<GunComponent> gunEntity)
    {
        var gunSpread = gunEntity.Comp.MaxAngleModified;

        if (!_npcLineOfFireSystem.TryGetNextAmmo(gunEntity.Owner, out var ammoUid, out var ammoPrototype) ||
            !TryGetProjectileSpread(ammoUid, ammoPrototype, out var projectileSpread))
            return (gunSpread, 1);

        // Gun modifiers such as a choke narrow the pellet spread through this, as they do when it actually fires.
        var spreadEvent = new GunGetAmmoSpreadEvent(projectileSpread.Spread);
        RaiseLocalEvent(gunEntity.Owner, ref spreadEvent);

        return (gunSpread + spreadEvent.Spread, Math.Max(projectileSpread.Count, 1));
    }

    /// <summary>
    ///     Looks through a round to what it actually fires: a cartridge's projectile, or the round itself.
    /// </summary>
    private bool TryGetProjectileSpread(EntityUid? ammoUid, EntProtoId? ammoPrototype, out ProjectileSpreadComponent projectileSpread)
    {
        projectileSpread = default!;

        EntProtoId? projectilePrototype = null;

        if (ammoUid is { } uid)
        {
            if (_cartridgeAmmoQuery.TryComp(uid, out var cartridgeComponent))
                projectilePrototype = cartridgeComponent.Prototype;
            else if (_projectileSpreadQuery.TryComp(uid, out var ownSpread))
            {
                projectileSpread = ownSpread;
                return true;
            }
        }
        else if (ammoPrototype is { } prototypeId && ProtoMan.TryIndex(prototypeId, out var ammoEntityPrototype))
        {
            if (ammoEntityPrototype.TryComp<CartridgeAmmoComponent>(out var cartridgeComponent, Factory))
                projectilePrototype = cartridgeComponent.Prototype;
            else if (ammoEntityPrototype.TryComp<ProjectileSpreadComponent>(out var ownSpread, Factory))
            {
                projectileSpread = ownSpread;
                return true;
            }
        }

        return projectilePrototype is { } projectileId &&
            ProtoMan.TryIndex(projectileId, out var projectileEntityPrototype) &&
            projectileEntityPrototype.TryComp(out projectileSpread!, Factory);
    }

    /// <summary>
    ///     Given a shooter and target distance, and gun, returns the minimum distance to maybe hit the target (depending on the value of <paramref name="k"/>).
    ///         Returns 0 if no distance could be found.
    /// </summary>
    /// <param name="k">Desired minimum 'coverage' factor; how much the shooter would want to hit the target. k=0.5 means very accurate and almost guaranteed hits, k=1 means spread roughly matches target width, k=1.5-2.0 means the shooter is willing to take less accurate shots.</param>
    public float GetDesiredFiringDistance(Entity<FixturesComponent?> targetEntity, Angle spread, float k, CollisionGroup requiredCollisionLayer = CollisionGroup.BulletImpassable)
    {
        if (!Resolve(targetEntity, ref targetEntity.Comp))
            return 0f;

        // shitty estimate, just get radius of biggest valid fixture
        var targetWidth = 0f;
        foreach (var fixture in targetEntity.Comp.Fixtures.Values)
        {
            // Only consider this if it can be hit by bullets or something, this sucks as hardcode
            var collisionLayer = (CollisionGroup)fixture.CollisionLayer;
            if (!collisionLayer.HasFlag(requiredCollisionLayer))
                continue;

            var radius = fixture.Shape.Radius;
            if (radius < targetWidth)
                continue;

            targetWidth = radius;
        }

        if (targetWidth == 0f)
        {
            // bb = boundingbox
            Log.Error($"When trying to get desired firing distance, could not determine a non-zero width of target entity {ToPrettyString(targetEntity)}! This usually means the target entity has no fixtures nor BB.");
            return 0f;
        }

        /*
            Formula for this, where
                W = target width
                D = distance
                A = spread angle (radians)
                k = desired minimum 'coverage' factor

            D = kW / 2tan(A/2)
        */

        return (k * targetWidth) / (2f * MathF.Tan((float)spread.Theta / 2f));
    }
}
