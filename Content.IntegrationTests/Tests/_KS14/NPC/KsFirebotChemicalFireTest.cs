#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Firefighting;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Gravity;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Gravity;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Firebots fight chemical fires (<see cref="KsNpcFirefightingSystem"/>): a whole spill, not only one tile, and until
///         it is out for good rather than until the flames first die.
/// </summary>
public sealed class KsFirebotChemicalFireTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId FirebotPrototype = "MobFireBot";
    private static readonly ProtoId<ReagentPrototype> WeldingFuelReagent = "WeldingFuel";
    private const int TicksPerSecond = 30;

    /// <summary>
    ///     How long a spill has to stay unlit to count as out for good. A hot puddle relights within a few seconds of its
    ///         flames going out.
    /// </summary>
    private const int QuietSeconds = 15;

    private const int CheckTicks = TicksPerSecond / 2;

    /// <summary>
    ///     How hot the hot-puddle test holds the fuel, from the start, and for how long after its flames are out: past welding fuel's
    ///         400 K ignition point, and long enough for an untended blanket to give out. Much hotter and the puddle boils
    ///         foam off faster than one firebot can lay it.
    /// </summary>
    private const float HeldPuddleTemperature = 450f;
    private const int HeldHotSeconds = 30;

    private static readonly FixedPoint2 SpilledFuel = FixedPoint2.New(100);

    /// <summary>
    ///     A firebot that fights the spill saves at least this much of its fuel from burning.
    /// </summary>
    private const float SavedFuelFraction = 0.2f;

    /// <summary>
    ///     A hot fuel spill wide enough to burn on several tiles at once is put out by a firebot, and stays out, long before
    ///         it would have burned away.
    /// </summary>
    [Test]
    public async Task TestFirebotPutsOutALargeFuelFireForGood([Values(true, false)] bool withFirebot)
    {
        var server = Pair.Server;
        var gridUid = await SetUpBurningSpill();

        if (withFirebot)
            await SpawnFirebot(gridUid);

        var quietTicks = 0;
        for (var tick = 0; tick < TicksPerSecond * 120 && quietTicks < TicksPerSecond * QuietSeconds; tick += CheckTicks)
        {
            await server.WaitRunTicks(CheckTicks);

            var burning = false;
            await server.WaitPost(() => burning = CountGridFires(gridUid) > 0);
            quietTicks = burning ? 0 : quietTicks + CheckTicks;
        }

        var leftoverFuel = FixedPoint2.Zero;
        await server.WaitPost(() => leftoverFuel = CountFuel());

        // Left alone, the spill burns until its fuel is gone. Fuel left on the floor means something put it out.
        Assert.That(quietTicks, Is.GreaterThanOrEqualTo(TicksPerSecond * QuietSeconds), "The spill should be out for good by the end.");
        if (withFirebot)
            Assert.That(leftoverFuel, Is.GreaterThan(SpilledFuel * SavedFuelFraction), "The firebot should put the spill out long before it burns away.");
        else
            Assert.That(leftoverFuel, Is.LessThan(SpilledFuel * SavedFuelFraction), "Left alone, the spill should burn away, or the test proves nothing.");
    }

    /// <summary>
    ///     A puddle that stays hot after its flames are out would light again as soon as its foam boiled away. The firebot
    ///         keeps watch and tops the foam up, so it never does.
    /// </summary>
    [Test]
    public async Task TestFirebotKeepsAHotPuddleFromRelighting()
    {
        var server = Pair.Server;
        var gridUid = await SetUpBurningSpill();
        await SpawnFirebot(gridUid);

        // Something keeps the fuel hot past its ignition point the whole time, from before the firebot gets there: the
        //      flames only stay out where the foam is, so the firebot has to foam every hot puddle, not only the burning ones.
        var knockedDown = false;
        for (var tick = 0; tick < TicksPerSecond * 30 && !knockedDown; tick += CheckTicks)
        {
            await server.WaitPost(() => HeatFuelPuddles(HeldPuddleTemperature));
            await server.WaitRunTicks(CheckTicks);
            await server.WaitPost(() => knockedDown = CountGridFires(gridUid) == 0);
        }

        Assert.That(knockedDown, "The firebot should put the flames out first.");

        var relitChecks = 0;
        for (var tick = 0; tick < TicksPerSecond * HeldHotSeconds; tick += CheckTicks)
        {
            await server.WaitPost(() => HeatFuelPuddles(HeldPuddleTemperature));
            await server.WaitRunTicks(CheckTicks);
            await server.WaitPost(() =>
            {
                if (CountGridFires(gridUid) > 0)
                    relitChecks++;
            });
        }

        Assert.That(relitChecks, Is.Zero, "A hot puddle the firebot is watching should never relight.");
    }

    /// <summary>
    ///     Spills hot fuel in an open, aired room and lets it spread and catch. Returns the grid.
    /// </summary>
    private async Task<EntityUid> SetUpBurningSpill()
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

            // A station has gravity. Without it, the recoil of every spray shoves the firebot clear of the fire.
            var gravityComponent = entityManager.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            entityManager.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            var mapSystem = entityManager.System<SharedMapSystem>();
            for (var x = -4; x < 12; x++)
            {
                for (var y = -4; y < 5; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }
        });
        await server.WaitRunTicks(TicksPerSecond);

        await server.WaitPost(() =>
        {
            var tileRef = entityManager.System<SharedMapSystem>().GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(6, 0));
            var spilled = new Solution(WeldingFuelReagent.Id, SpilledFuel) { Temperature = 700f };
            entityManager.System<PuddleSystem>().TrySpillAt(tileRef, spilled, out _, sound: false);
        });

        await server.WaitRunTicks(TicksPerSecond * 3);

        var startingFires = 0;
        await server.WaitPost(() => startingFires = CountGridFires(testMap.Grid.Owner));
        Assert.That(startingFires, Is.GreaterThan(3), "The spill should burn on several tiles before the firebot arrives.");

        return testMap.Grid.Owner;
    }

    /// <summary>
    ///     Spawns a firebot a few tiles from the spill and wakes it, as a player nearby would.
    /// </summary>
    private async Task SpawnFirebot(EntityUid gridUid)
    {
        var entityManager = Pair.Server.EntMan;
        await Pair.Server.WaitPost(() =>
        {
            var firebotUid = entityManager.SpawnEntity(FirebotPrototype, new EntityCoordinates(gridUid, -2.5f, 0.5f));
            entityManager.System<NPCSystem>().WakeNPC(firebotUid, entityManager.GetComponent<HTNComponent>(firebotUid));
        });
    }

    private void HeatFuelPuddles(float temperature)
    {
        var entityManager = Pair.Server.EntMan;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var query = entityManager.EntityQueryEnumerator<PuddleComponent>();
        while (query.MoveNext(out var uid, out var puddleComponent))
        {
            if (solutionContainerSystem.ResolveSolution(uid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution) &&
                solution.GetTotalPrototypeQuantity(WeldingFuelReagent) > FixedPoint2.Zero)
                solutionContainerSystem.SetTemperature(puddleComponent.Solution.Value, temperature);
        }
    }

    private FixedPoint2 CountFuel()
    {
        var entityManager = Pair.Server.EntMan;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var total = FixedPoint2.Zero;
        var query = entityManager.EntityQueryEnumerator<PuddleComponent>();
        while (query.MoveNext(out var uid, out var puddleComponent))
        {
            if (solutionContainerSystem.ResolveSolution(uid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution))
                total += solution.GetTotalPrototypeQuantity(WeldingFuelReagent);
        }

        return total;
    }

    /// <summary>
    ///     The chemfires burning on the grid. Counting every <see cref="ChemicalFireComponent"/> would include the
    ///         paused template fires the chemfire system keeps in nullspace.
    /// </summary>
    private int CountGridFires(EntityUid gridUid)
    {
        if (!Pair.Server.EntMan.TryGetComponent<ChemicalFireGridComponent>(gridUid, out var chemicalFireGridComponent))
            return 0;

        var count = 0;
        foreach (var tileFires in chemicalFireGridComponent.Tiles.Values)
            count += tileFires.Fires.Count;

        return count;
    }
}
