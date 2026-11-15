#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.Projectiles;

/// <summary>
///     Rounds and what cannot be damaged. A round is spent on a hard thing with no <c>DamageableComponent</c>
///         (<c>PredictedProjectileSystem.DoHit</c>), so the bread dog and cake cat, mobs that stop rounds, take damage from
///         them. PA particles are the exception (<c>ProjectileComponent.KsPassesUndamageable</c>): they cross the
///         containment field unspent, to the singularity inside it.
/// </summary>
public sealed class KsUndamageableTargetTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsUndamageableTargetTestShooter
";

    [Test]
    public async Task TestARoundHurtsTheMobItStopsOn([Values("MobBreadDog", "MobCatCake")] string mob)
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, mobUid = default, shooterUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -8), new Vector2i(3, 3)).Owner;
            mobUid = SpawnAt(entManager, mob, gridUid, 0, 0);
            shooterUid = SpawnAt(entManager, "KsUndamageableTargetTestShooter", gridUid, 0, -6);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            var bulletUid = entManager.SpawnAtPosition("BulletPistol", new EntityCoordinates(gridUid, new Vector2(0.5f, -3.5f)));
            entManager.System<GunSystem>().ShootProjectile(bulletUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null, user: shooterUid);
        });

        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
#pragma warning disable CS0618 // a hit at all is what is asked, not how much
            Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(mobUid).Float(), Is.GreaterThan(0f),
                $"the round should hurt the {mob} it stops on");
#pragma warning restore CS0618
        });
    }

    [Test]
    public async Task TestAParticleCrossesTheContainmentFieldUnspent()
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, particleUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -8), new Vector2i(3, 8)).Owner;
            SpawnAt(entManager, "ContainmentField", gridUid, 0, 0);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            particleUid = entManager.SpawnAtPosition("ParticlesProjectile", new EntityCoordinates(gridUid, new Vector2(0.5f, -4.5f)));
            entManager.System<GunSystem>().ShootProjectile(particleUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null);
        });

        await Pair.RunTicksSync(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(entManager.GetComponent<TransformComponent>(particleUid).LocalPosition.Y, Is.GreaterThan(1.5f),
                "the particle should be through the field");
            Assert.That(entManager.GetComponent<ProjectileComponent>(particleUid).ProjectileSpent, Is.False,
                "and not spent on it: it has the singularity still to reach");
        });
    }
}
