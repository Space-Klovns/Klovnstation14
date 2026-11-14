#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage.Components;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     The field commander's tech fab, silo and field fortifications: what the fab prints, what the silo starts with, and
///         what barbed wire does to someone wading through it.
/// </summary>
[TestFixture]
public sealed class KsFieldCommanderLogisticsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId TechFab = "KsFieldCommanderTechFab";
    private static readonly EntProtoId Silo = "KsMaterialSiloFieldCommander";
    private static readonly EntProtoId BarbedWire = "KsBarbedWire";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId SandbagWall = "KsSandbagWall";
    private static readonly EntProtoId PistolBullet = "BulletPistol";
    private static readonly ProtoId<Content.Shared.Construction.Prototypes.ConstructionGraphPrototype> SandbagGraph = "KsSandbagWall";
    private static readonly ProtoId<MaterialPrototype> Plastic = "Plastic";
    private static readonly ProtoId<Content.Shared.Damage.Prototypes.DamageTypePrototype> Blunt = "Blunt";

    private static readonly ProtoId<LatheRecipePrototype>[] FieldRecipes = ["KsFieldRemoteDroneFpv", "Brutepack", "KsSandbag", "KsBarbedWireCoil"];

    [Test]
    public async Task TechFabOffersFieldCommanderRecipes()
    {
        var server = Pair.Server;
        var latheSystem = server.System<SharedLatheSystem>();
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var techFabUid = SSpawnAtPosition(TechFab, testMap.GridCoords);
            var recipes = latheSystem.GetAllPossibleRecipes(SComp<LatheComponent>(techFabUid));

            foreach (var recipe in FieldRecipes)
                Assert.That(recipes, Does.Contain(recipe), $"the field tech fab should offer {recipe}");
        });
    }

    [Test]
    public async Task SiloCannotBeDamagedAndIsStockedWithPlastic()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var siloUid = SSpawnAtPosition(Silo, testMap.GridCoords);

            var damageableSystem = server.System<Content.Shared.Damage.Systems.DamageableSystem>();
            var damage = new Content.Shared.Damage.DamageSpecifier(SProtoMan.Index(Blunt), Content.Shared.FixedPoint.FixedPoint2.New(1000));
            damageableSystem.TryChangeDamage(siloUid, damage, ignoreResistances: true);
            Assert.That(server.EntMan.Deleted(siloUid), Is.False, "the silo should be indestructible");
            Assert.That(damageableSystem.GetTotalDamage(siloUid), Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.Zero), "the silo should take no damage");
            Assert.That(server.System<SharedMaterialStorageSystem>().GetMaterialAmount(siloUid, Plastic.Id, localOnly: true), Is.GreaterThan(0), "the silo should start with plastic");
        });
    }

    /// <summary>
    ///     Fires a pistol bullet east across a sandbag wall at a mob behind it, and says which of the two it hurt.
    /// </summary>
    private async Task<(bool WallHit, bool TargetHit)> FireAcrossSandbags()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid wallUid = default;
        EntityUid targetUid = default;

        await server.WaitPost(() =>
        {
            for (var x = 0; x <= 6; x++)
            {
                server.System<SharedMapSystem>().SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Robust.Shared.Maths.Vector2i(x, 0), testMap.Tile.Tile);
            }

            var shooterUid = SSpawnAtPosition(Human, testMap.GridCoords.Offset(new System.Numerics.Vector2(0.5f, 0.5f)));
            wallUid = SSpawnAtPosition(SandbagWall, testMap.GridCoords.Offset(new System.Numerics.Vector2(3.5f, 0.5f)));
            targetUid = SSpawnAtPosition(Human, testMap.GridCoords.Offset(new System.Numerics.Vector2(5.5f, 0.5f)));

            var bulletUid = SSpawnAtPosition(PistolBullet, testMap.GridCoords.Offset(new System.Numerics.Vector2(1.5f, 0.5f)));
            server.System<Content.Shared.Projectiles.SharedProjectileSystem>().SetShooter(bulletUid, SComp<Content.Shared.Projectiles.ProjectileComponent>(bulletUid), shooterUid);

            var physicsSystem = server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            physicsSystem.SetLinearVelocity(bulletUid, new System.Numerics.Vector2(20f, 0f));
        });
        await Pair.RunTicksSync(30);

        var result = (false, false);
        await server.WaitPost(() =>
        {
            var damageableSystem = server.System<Content.Shared.Damage.Systems.DamageableSystem>();
            result = (damageableSystem.GetTotalDamage(wallUid) > 0, damageableSystem.GetTotalDamage(targetUid) > 0);
        });
        return result;
    }

    /// <summary>
    ///     Sandbags are cover to shoot over, not a wall: a shot not aimed at them flies on. One aimed at them hits them, as
    ///         with any <see cref="Content.Shared.Damage.Components.RequireProjectileTargetComponent"/>.
    /// </summary>
    [Test]
    public async Task BulletsFlyOverSandbags()
    {
        var (wallHit, targetHit) = await FireAcrossSandbags();
        Assert.Multiple(() =>
        {
            Assert.That(wallHit, Is.False, "a bullet aimed past the sandbags should fly over them");
            Assert.That(targetHit, Is.True, "the bullet that flew over should reach the mob behind");
        });
    }

    [Test]
    public async Task SandbagWallComesApartByHandForEveryBag()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid wallUid = default;

        await server.WaitPost(() =>
        {
            wallUid = SSpawnAtPosition(SandbagWall, testMap.GridCoords.Offset(new System.Numerics.Vector2(0.5f, 0.5f)));
        });

        EntityUid userUid = default;
        var disassembleSeconds = 0f;
        await server.WaitPost(() =>
        {
            userUid = SSpawnAtPosition(Human, testMap.GridCoords.Offset(new System.Numerics.Vector2(1.5f, 0.5f)));
            Assert.That(server.EntMan.TryGetComponent<Content.Shared.Engineering.Components.DisassembleOnAltVerbComponent>(wallUid, out var disassembleComponent),
                "the sandbag wall should come apart by hand");

            // What the alt verb does when clicked.
            disassembleSeconds = (float)disassembleComponent!.DisassembleTime.TotalSeconds;
            server.System<Content.Shared.Engineering.Systems.DisassembleOnAltVerbSystem>().StartDisassembly((wallUid, disassembleComponent), userUid);
        });
        await Pair.RunTicksSync((int)((disassembleSeconds + 1f) * (float)server.Timing.TickRate));

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(wallUid), "the wall should be gone once pulled apart");

            var bags = 0;
            foreach (var heldUid in server.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().EnumerateHeld(userUid))
            {
                if (server.EntMan.TryGetComponent<Content.Shared.Stacks.StackComponent>(heldUid, out var stackComponent))
                    bags += stackComponent.Count;
            }

            Assert.That(bags, Is.EqualTo(SandbagWallCost()), "pulling a wall apart should hand back every bag it was built from");
        });
    }

    /// <summary>
    ///     How many sandbags the construction recipe puts into a wall.
    /// </summary>
    private int SandbagWallCost()
    {
        var graph = SProtoMan.Index<Content.Shared.Construction.Prototypes.ConstructionGraphPrototype>(SandbagGraph);
        foreach (var node in graph.Nodes.Values)
        {
            foreach (var edge in node.Edges)
            {
                foreach (var step in edge.Steps)
                {
                    if (step is Content.Shared.Construction.Steps.MaterialConstructionGraphStep materialStep)
                        return materialStep.Amount;
                }
            }
        }

        Assert.Fail("the sandbag wall's construction graph has no material step");
        return 0;
    }

    [Test]
    public async Task BarbedWireSlowsAMobStandingInIt()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        var wireTile = testMap.GridCoords.Offset(new System.Numerics.Vector2(0.5f, 0.5f));
        var freeTile = testMap.GridCoords.Offset(new System.Numerics.Vector2(3.5f, 0.5f));

        // The wire goes down first, so the mob walks into it as it would in play. A mob already standing still has
        //     gone to sleep, and a sleeping body's contacts are never updated.
        EntityUid wadingMobUid = default;
        EntityUid freeMobUid = default;
        await server.WaitPost(() =>
        {
            // Without gravity a mob is weightless, and weightless mobs wade over contact slowdowns.
            server.EntMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(testMap.MapUid).Enabled = true;

            SSpawnAtPosition(BarbedWire, wireTile);
            wadingMobUid = SSpawnAtPosition(Human, wireTile);
            freeMobUid = SSpawnAtPosition(Human, freeTile);

            // Kinematic mobs spawn asleep, and a sleeping body's contacts are never updated. Walking mobs are awake.
            var physicsSystem = server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            foreach (var mobUid in new[] { wadingMobUid, freeMobUid })
            {
                var physicsComponent = server.EntMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(mobUid);
                physicsSystem.SetSleepingAllowed(mobUid, physicsComponent, false);
                physicsSystem.WakeBody(mobUid, body: physicsComponent);
            }
        });
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var freeWalkSpeed = SComp<MovementSpeedModifierComponent>(freeMobUid).CurrentWalkSpeed;
            var wadingWalkSpeed = SComp<MovementSpeedModifierComponent>(wadingMobUid).CurrentWalkSpeed;
            Assert.That(wadingWalkSpeed, Is.LessThan(freeWalkSpeed), "wading through barbed wire should be slower than walking free");
        });
    }
}
