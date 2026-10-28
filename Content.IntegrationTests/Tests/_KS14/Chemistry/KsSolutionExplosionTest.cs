#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Chemistry.Explosion;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Explosion.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     Containers explode as strongly as what they hold (<see cref="KsSolutionExplosionSystem"/>): when damaged, and on
///         their own once explosive contents get hot (<see cref="KsCookOffSystem"/>).
/// </summary>
public sealed class KsSolutionExplosionTest : GameTest
{
    private static readonly EntProtoId FuelTankPrototype = "WeldingFuelTankFull";
    private static readonly EntProtoId EmptyFuelTankPrototype = "WeldingFuelTank";
    private static readonly EntProtoId HighCapacityFuelTankPrototype = "WeldingFuelTankHighCapacity";
    private static readonly EntProtoId FuelJugPrototype = "JugWeldingFuel";
    private static readonly EntProtoId JugPrototype = "Jug";
    private static readonly ProtoId<ReagentPrototype> WeldingFuelReagent = "WeldingFuel";
    private static readonly ProtoId<ReagentPrototype> NapalmReagent = "Napalm";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<DamageTypePrototype> HeatDamage = "Heat";
    private const string DemolitionExplosion = "DemolitionCharge"; // 800 K, no fire stacks
    private static readonly ProtoId<DamageTypePrototype> PiercingDamage = "Piercing";
    private const int TicksPerSecond = 30;

    /// <summary>
    ///     A full fuel tank and a high-capacity one explode as strongly as they did when the strength was fixed per tank,
    ///         and napalm harder than the same volume of fuel.
    /// </summary>
    [Test]
    public async Task TestStrengthComesFromTheContents()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var explosionSystem = entityManager.System<KsSolutionExplosionSystem>();
            var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

            float IntensityOf(EntProtoId prototype)
            {
                var uid = entityManager.SpawnEntity(prototype, testMap.GridCoords);
                solutionContainerSystem.TryGetSolution(uid, "tank", out _, out var solution);
                var intensity = KsSolutionExplosionSystem.GetIntensity(explosionSystem.GetExplosivePower(solution!));
                entityManager.DeleteEntity(uid);
                return intensity;
            }

            var fuel = new Solution(WeldingFuelReagent.Id, FixedPoint2.New(100));
            var napalm = new Solution(NapalmReagent.Id, FixedPoint2.New(100));
            var water = new Solution(WaterReagent.Id, FixedPoint2.New(100));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(IntensityOf(FuelTankPrototype), Is.EqualTo(60f).Within(0.01f));
                Assert.That(IntensityOf(HighCapacityFuelTankPrototype), Is.EqualTo(120f).Within(0.01f));
                Assert.That(explosionSystem.GetExplosivePower(napalm), Is.GreaterThan(explosionSystem.GetExplosivePower(fuel)));
                Assert.That(explosionSystem.GetExplosivePower(water), Is.Zero);
            }
        });
    }

    /// <summary>
    ///     Burning a fuel tank sets it off. The same tank holding water only takes the damage.
    /// </summary>
    [Test]
    public async Task TestDamagedTankExplodesOnlyIfItHoldsSomethingExplosive([Values(true, false)] bool holdsFuel)
    {
        var testMap = await Pair.CreateTestMap();
        var tankUid = await SpawnFilled(testMap.GridCoords, holdsFuel ? FuelTankPrototype : EmptyFuelTankPrototype,
            holdsFuel ? (ProtoId<ReagentPrototype>?)null : WaterReagent, "tank");

        await Damage(tankUid, HeatDamage, 10);
        await Pair.Server.WaitRunTicks(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(ExplosionCount(), holdsFuel ? Is.GreaterThan(0) : Is.Zero));
    }

    /// <summary>
    ///     An explosion is an open flame to the fuel it passes over, even one that carries no fire of its own and barely
    ///         warms it. Cold fuel spilled near a demolition charge catches.
    /// </summary>
    [Test]
    public async Task TestExplosionSparksFuelItPassesOver()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            entityManager.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            entityManager.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            var mapSystem = entityManager.System<SharedMapSystem>();
            for (var x = -8; x < 9; x++)
            {
                for (var y = -8; y < 9; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitPost(() =>
        {
            var mapSystem = entityManager.System<SharedMapSystem>();
            var tileRef = mapSystem.GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(2, 0));
            entityManager.System<Content.Server.Fluids.EntitySystems.PuddleSystem>().TrySpillAt(tileRef,
                new Solution(WeldingFuelReagent.Id, FixedPoint2.New(20)) { Temperature = Atmospherics.T20C }, out _, sound: false);
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitPost(() =>
        {
            var epicenter = entityManager.System<SharedTransformSystem>().ToMapCoordinates(new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f));
            entityManager.System<Content.Server.Explosion.EntitySystems.ExplosionSystem>().QueueExplosion(epicenter,
                DemolitionExplosion, totalIntensity: 30f, slope: 1f, maxTileIntensity: 10f, cause: null, tileBreakScale: 0f);
        });

        var lit = false;
        for (var tick = 0; tick < TicksPerSecond * 5 && !lit; tick += 5)
        {
            await server.WaitRunTicks(5);
            await server.WaitPost(() => lit =
                entityManager.TryGetComponent<Content.Shared._KS14.Atmos.ChemicalFire.ChemicalFireGridComponent>(testMap.Grid.Owner, out var fires) &&
                fires.Tiles.Count > 0);
        }

        Assert.That(lit, "The explosion should light the cold fuel it passed over.");
    }

    /// <summary>
    ///     A jug of fuel that is shot explodes; a jug of water doesn't.
    /// </summary>
    [Test]
    public async Task TestShotJugExplodesOnlyIfItHoldsSomethingExplosive([Values(true, false)] bool holdsFuel)
    {
        var testMap = await Pair.CreateTestMap();
        var jugUid = await SpawnFilled(testMap.GridCoords, holdsFuel ? FuelJugPrototype : JugPrototype,
            holdsFuel ? (ProtoId<ReagentPrototype>?)null : WaterReagent, "drink");

        await Damage(jugUid, PiercingDamage, 6);
        await Pair.Server.WaitRunTicks(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(ExplosionCount(), holdsFuel ? Is.GreaterThan(0) : Is.Zero));
    }

    /// <summary>
    ///     A fuel tank in a burning room heats up and cooks off. One in an ordinary room, or one holding water, never does.
    /// </summary>
    [Test]
    public async Task TestTankCooksOffInHotAir([Values(true, false)] bool hotAir, [Values(true, false)] bool holdsFuel)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = hotAir ? 1500f : Atmospherics.T20C };
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.MolesCellStandard);
            entityManager.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            entityManager.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            var mapSystem = entityManager.System<SharedMapSystem>();
            for (var x = -2; x < 3; x++)
            {
                for (var y = -2; y < 3; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }
        });
        await server.WaitRunTicks(TicksPerSecond);

        await SpawnFilled(new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f), holdsFuel ? FuelTankPrototype : EmptyFuelTankPrototype,
            holdsFuel ? (ProtoId<ReagentPrototype>?)null : WaterReagent, "tank");

        var exploded = false;
        // Every tick: an explosion's visuals entity only lasts as long as the explosion does.
        for (var tick = 0; tick < TicksPerSecond * 60 && !exploded; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => exploded = ExplosionCount() > 0);
        }

        Assert.That(exploded, Is.EqualTo(hotAir && holdsFuel),
            "Only a tank of fuel in hot air should cook off.");
    }

    /// <summary>
    ///     Spawns a container, filled with 960u of <paramref name="fillReagent"/> if one is given.
    /// </summary>
    private async Task<EntityUid> SpawnFilled(EntityCoordinates coordinates, EntProtoId prototype, ProtoId<ReagentPrototype>? fillReagent, string solutionName)
    {
        var entityManager = Pair.Server.EntMan;
        EntityUid uid = default;
        await Pair.Server.WaitPost(() =>
        {
            uid = entityManager.SpawnEntity(prototype, coordinates);
            if (fillReagent is not { } reagent)
                return;

            var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
            Assert.That(solutionContainerSystem.TryGetSolution(uid, solutionName, out var solutionEntity, out _));
            solutionContainerSystem.TryAddReagent(solutionEntity!.Value, reagent.Id, FixedPoint2.New(960), out _);
        });

        return uid;
    }

    private async Task Damage(EntityUid uid, ProtoId<DamageTypePrototype> damageType, float amount)
    {
        var entityManager = Pair.Server.EntMan;
        await Pair.Server.WaitPost(() =>
        {
            var damage = new DamageSpecifier(Pair.Server.ProtoMan.Index(damageType), FixedPoint2.New(amount));
            entityManager.System<DamageableSystem>().ChangeDamage(uid, damage, ignoreResistances: true);
        });
    }

    private int ExplosionCount()
    {
        return Pair.Server.EntMan.Count<ExplosionVisualsComponent>();
    }
}
