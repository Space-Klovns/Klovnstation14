using Content.Server._KS14.NPC.LineOfFire;
using Content.Server.Examine;
using Content.Server.Interaction;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCJukeSystem : EntitySystem
{
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;

    /// <summary>
    ///     The whole cone <paramref name="gunEntity"/>'s next shot can land in, at the widest the gun's spread ever gets, and how
    ///         many projectiles it throws into it. See <see cref="NpcLineOfFireSystem.GetShotCone"/>.
    /// </summary>
    public (Angle Spread, int Projectiles) GetShotSpread(Entity<GunComponent> gunEntity)
    {
        var cone = _npcLineOfFireSystem.GetShotCone(gunEntity, worstCase: true);
        return (cone.Spread, cone.Projectiles);
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
