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
    private static readonly ProtoId<MaterialPrototype> Plastic = "Plastic";

    private static readonly ProtoId<LatheRecipePrototype>[] FieldRecipes = ["RemoteDroneFpv", "Brutepack", "KsSandbag", "KsBarbedWireCoil"];

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
    public async Task SiloIsGodmodeAndStockedWithPlastic()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var siloUid = SSpawnAtPosition(Silo, testMap.GridCoords);

            Assert.That(server.EntMan.HasComponent<GodmodeComponent>(siloUid), Is.True, "the silo should be indestructible");
            Assert.That(server.System<SharedMaterialStorageSystem>().GetMaterialAmount(siloUid, Plastic.Id, localOnly: true), Is.GreaterThan(0), "the silo should start with plastic");
        });
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
