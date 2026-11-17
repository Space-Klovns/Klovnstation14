#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Singularity.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.Projectiles;

/// <summary>
///     An emitter's bolt still charges the containment field generator it hits. The generator has no
///         <c>DamageableComponent</c>, so a round is now spent on it (<c>PredictedProjectileSystem.DoHit</c>), and the
///         look-ahead (<c>KsRayCollisionSystem</c>) has it hit before physics does: the generator charges on the contact
///         physics makes (<c>ContainmentFieldGeneratorSystem.HandleGeneratorCollide</c>), and that must still happen.
/// </summary>
public sealed class KsEmitterBoltChargeTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task TestEmitterBoltChargesTheGenerator([Range(2, 6)] int range)
    {
        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default, generatorUid = default, boltUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-3, -10), new Vector2i(3, 3)).Owner;
            generatorUid = SpawnAt(entManager, "ContainmentFieldGenerator", gridUid, 0, 0);
        });

        await Pair.RunTicksSync(5);

        var before = 0;
        await Server.WaitPost(() =>
        {
            before = entManager.GetComponent<ContainmentFieldGeneratorComponent>(generatorUid).PowerBuffer;
            // As an emitter fires it: spawned and shot in one go.
            boltUid = entManager.SpawnAtPosition("EmitterBolt", new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f - range)));
            entManager.System<GunSystem>().ShootProjectile(boltUid, new Vector2(0f, 1f), Vector2.Zero, gunUid: null);
        });

        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
            Assert.That(entManager.GetComponent<ContainmentFieldGeneratorComponent>(generatorUid).PowerBuffer, Is.GreaterThan(before),
                "the bolt should have charged the generator"));
    }
}
