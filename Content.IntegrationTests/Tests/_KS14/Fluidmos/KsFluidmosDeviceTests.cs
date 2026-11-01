#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Fluid meeting tiles through devices: liquid leaves as a puddle and unlinked vapor as smoke, without ever reaching a
///         tile mixture (which the Debug <c>Merge</c> assert would catch), and siphons and scrubbers draw puddles and smoke
///         back in. Run under Debug.
/// </summary>
[TestOf(typeof(KsFluidmosTileSystem))]
public sealed class KsFluidmosDeviceTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> EthanolReagent = "Ethanol";

    private static readonly EntProtoId OutletInjectorPrototype = "GasOutletInjector";
    private static readonly EntProtoId PassiveVentPrototype = "GasPassiveVent";
    private static readonly EntProtoId PipePrototype = "GasPipeStraight";
    private static readonly EntProtoId OxygenTankPrototype = "OxygenTankFilled";
    private static readonly EntProtoId WaterCanisterPrototype = "KsFluidmosCanisterWater";
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    private static readonly KsFluidReagentKey WaterKey = new(WaterReagent, null);
    private static readonly KsFluidReagentKey EthanolKey = new(EthanolReagent, null);

    private const float WaterUnits = 20f;
    private const float EthanolVaporUnits = 5f;

    private async Task<TestMapData> CreateBreathableTestMap()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(30);
        return testMap;
    }

    /// <summary>
    ///     Water liquid and ethanol vapor, at a pressure that pushes them out of the device.
    /// </summary>
    private void FillWithFluid(GasMixture air)
    {
        Assert.That(Pair.Server.System<KsFluidmosPropertiesSystem>().TryGetProperties(EthanolReagent, out var ethanol));

        air.Temperature = Atmospherics.T20C;
        air.AdjustMoles(Gas.Nitrogen, 3f * Atmospherics.OneAtmosphere * air.Volume / (Atmospherics.R * air.Temperature));
        air.Fluid = new KsFluidMixture();
        air.Fluid.AddLiquid(WaterKey, WaterUnits);
        air.Fluid.SetAmount(EthanolKey, new KsFluidAmount(0f, 0f, EthanolVaporUnits * ethanol!.MolesPerUnit));
    }

    /// <summary>
    ///     The centre of the test map's tile, where something unanchored spills onto it. <see cref="TestMapData.GridCoords"/>
    ///         is the tile's corner, from which the slightest nudge spills it onto a neighbour instead.
    /// </summary>
    private static EntityCoordinates TileCentre(TestMapData testMap)
    {
        return new EntityCoordinates(testMap.Grid.Owner, testMap.Tile.GridIndices.X + 0.5f, testMap.Tile.GridIndices.Y + 0.5f);
    }

    private bool PuddleHas(TestMapData testMap, ProtoId<ReagentPrototype> reagent)
    {
        var puddleSystem = Pair.Server.System<PuddleSystem>();
        return puddleSystem.TryGetCachedPuddle(testMap.Grid.Owner, testMap.Tile.GridIndices, out var puddle) &&
               puddle.Comp.Solution is { } solution &&
               solution.Comp.Solution.GetTotalPrototypeQuantity(reagent) > FixedPoint2.Zero;
    }

    private bool AnySmokeHas(ProtoId<ReagentPrototype> reagent)
    {
        var entityManager = Pair.Server.EntMan;
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        var query = entityManager.AllEntityQueryEnumerator<SmokeComponent>();

        while (query.MoveNext(out var smokeUid, out _))
        {
            if (solutionContainerSystem.TryGetSolution(smokeUid, SmokeComponent.SolutionName, out _, out var solution) &&
                solution.GetTotalPrototypeQuantity(reagent) > FixedPoint2.Zero)
                return true;
        }

        return false;
    }

    [Test]
    public async Task OutletInjectorReleasesLiquidAsAPuddleAndVaporAsSmoke()
    {
        await ReleasesThroughDevice(OutletInjectorPrototype);
    }

    [Test]
    public async Task PassiveVentReleasesLiquidAsAPuddleAndVaporAsSmoke()
    {
        await ReleasesThroughDevice(PassiveVentPrototype);
    }

    private async Task ReleasesThroughDevice(EntProtoId devicePrototype)
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        EntityUid deviceUid = default;

        await server.WaitPost(() => deviceUid = server.EntMan.SpawnEntity(devicePrototype, testMap.GridCoords));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNode(deviceUid, "pipe", out PipeNode? pipe), $"{devicePrototype} has no pipe node.");
            FillWithFluid(pipe!.Air);
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var tileAir = atmosphereSystem.GetTileMixture(testMap.Grid.Owner, null, testMap.Tile.GridIndices);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(PuddleHas(testMap, WaterReagent), $"{devicePrototype} released no puddle of its liquid.");
                Assert.That(AnySmokeHas(EthanolReagent), $"{devicePrototype} released no smoke of its unlinked vapor.");
                Assert.That(tileAir?.Fluid, Is.Null, "A tile mixture holds fluid.");
            }
        });
    }

    [Test]
    public async Task SiphonDrainsThePuddleAndScrubbingDrawsInSmoke()
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();
        var tileSystem = server.System<KsFluidmosTileSystem>();
        EntityUid deviceUid = default;

        await server.WaitPost(() =>
        {
            deviceUid = server.EntMan.SpawnEntity(null, testMap.GridCoords);

            // Enough unlinked vapor to release as smoke straight away, and a puddle under the device.
            var released = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C, FluidCapable = true };
            FillWithFluid(released);
            released.Fluid!.TakeLiquidAsSolution(WaterUnits);
            tileSystem.ReleaseToTile(deviceUid, released);

            server.System<PuddleSystem>().TrySpillAt(testMap.Tile, new Solution(WaterReagent, FixedPoint2.New(10)), out _, sound: false);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(AnySmokeHas(EthanolReagent), "The test's vapor did not become smoke.");

            var destination = new GasMixture(200f) { Temperature = Atmospherics.T20C, FluidCapable = true };
            const float drained = 4f;
            tileSystem.AbsorbFromTile(deviceUid, destination, fraction: 1f, smoke: true, puddleUnits: drained);

            Assert.That(destination.Fluid, Is.Not.Null, "Nothing was drawn in.");
            Assert.That(destination.Fluid!.TryGetAmount(WaterKey, out var water));
            Assert.That(destination.Fluid.TryGetAmount(EthanolKey, out var ethanol));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(water.Liquid, Is.EqualTo(drained).Within(0.02f), "The siphon did not drain the puddle at its rate.");
                Assert.That(ethanol.Liquid, Is.GreaterThan(0f), "The scrubber drew in no smoke.");
            }
        });
    }

    /// <summary>
    ///     A pipe deleted outright, as an admin deletes one, takes its fluid with it, as it takes its gas: nothing spills.
    /// </summary>
    [Test]
    public async Task DeletedPipeSpillsNothing()
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();
        EntityUid pipeUid = default;

        await server.WaitPost(() => pipeUid = server.EntMan.SpawnEntity(PipePrototype, TileCentre(testMap)));
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            FillWithFluid(PipeAir(pipeUid));
            server.EntMan.DeleteEntity(pipeUid);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(PuddleHas(testMap, WaterReagent), Is.False, "A deleted pipe spilled its liquid.");
                Assert.That(AnySmokeHas(EthanolReagent), Is.False, "A deleted pipe released its vapor as smoke.");
            }
        });
    }

    /// <summary>
    ///     A pipe broken or destroyed by damage spills its share of its net's liquid once, and its net keeps the rest:
    ///         nothing is spilled twice, made or lost.
    /// </summary>
    [TestCase(150f)] // breaks it
    [TestCase(300f)] // destroys it outright
    public async Task DamagedPipeSpillsItsShareOfTheNetOnce(float damage)
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();
        EntityUid damagedUid = default;
        EntityUid keptUid = default;
        var keptTile = testMap.Tile.GridIndices + new Robust.Shared.Maths.Vector2i(0, 1);

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().SetTile(testMap.Grid.Owner, testMap.Grid.Comp, keptTile, testMap.Tile.Tile);
            damagedUid = server.EntMan.SpawnEntity(PipePrototype, TileCentre(testMap));
            keptUid = server.EntMan.SpawnEntity(PipePrototype, new EntityCoordinates(testMap.Grid.Owner, keptTile.X + 0.5f, keptTile.Y + 0.5f));
        });
        await server.WaitRunTicks(5);

        var share = 0f;
        await server.WaitPost(() =>
        {
            var netAir = PipeAir(damagedUid);
            Assert.That(PipeAir(keptUid), Is.SameAs(netAir), "The two pipes aren't one net.");
            FillWithFluid(netAir);
            share = PipeNode(damagedUid).Volume / netAir.Volume;

            var damageSpecifier = new DamageSpecifier(server.ProtoMan.Index(BluntDamage), FixedPoint2.New(damage));
            server.System<DamageableSystem>().TryChangeDamage(damagedUid, damageSpecifier, ignoreResistances: true);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(damagedUid), "The pipe wasn't broken.");
            var spilled = PuddleWater(testMap);
            var kept = PipeAir(keptUid).Fluid?.LiquidUnits ?? 0f;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(spilled, Is.EqualTo(WaterUnits * share).Within(0.1f), "The damaged pipe didn't spill its share of the net's liquid, once.");
                Assert.That(spilled + kept, Is.EqualTo(WaterUnits).Within(0.1f), "Liquid was made or lost.");
            }
        });
    }

    private PipeNode PipeNode(EntityUid pipeUid)
    {
        return Pair.Server.EntMan.GetComponent<Content.Shared.NodeContainer.NodeContainerComponent>(pipeUid)
            .Nodes.Values.OfType<PipeNode>().First();
    }

    private GasMixture PipeAir(EntityUid pipeUid)
    {
        return PipeNode(pipeUid).Air;
    }

    private float PuddleWater(TestMapData testMap)
    {
        return Pair.Server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Grid.Owner, testMap.Tile.GridIndices, out var puddle) &&
               puddle.Comp.Solution is { } solution
            ? (float)solution.Comp.Solution.GetTotalPrototypeQuantity(WaterReagent)
            : 0f;
    }

    [TestCase(350f)]
    [TestCase(10000f)]
    public async Task BrokenCanisterSpillsItsLiquid(float damage)
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();
        EntityUid canisterUid = default;

        await server.WaitPost(() => canisterUid = server.EntMan.SpawnEntity(WaterCanisterPrototype, TileCentre(testMap)));
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            var damageSpecifier = new DamageSpecifier(server.ProtoMan.Index(BluntDamage), FixedPoint2.New(damage));
            server.System<DamageableSystem>().TryChangeDamage(canisterUid, damageSpecifier, ignoreResistances: true);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(canisterUid), "The canister didn't break.");
            Assert.That(PuddleHas(testMap, WaterReagent), "A broken canister's liquid vanished instead of spilling.");
        });
    }

    [Test]
    public async Task DestroyedGasTankSpillsItsFluid()
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();

        await server.WaitPost(() =>
        {
            var tankUid = server.EntMan.SpawnEntity(OxygenTankPrototype, TileCentre(testMap));
            FillWithFluid(server.EntMan.GetComponent<GasTankComponent>(tankUid).Air);
            server.EntMan.DeleteEntity(tankUid);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(PuddleHas(testMap, WaterReagent), "A destroyed gas tank's liquid vanished instead of spilling.");
            Assert.That(AnySmokeHas(EthanolReagent), "A destroyed gas tank's unlinked vapor vanished instead of becoming smoke.");
        });
    }

    [Test]
    public async Task InternalsDeliverNoFluid()
    {
        var server = Pair.Server;
        var testMap = await CreateBreathableTestMap();
        var gasTankSystem = server.System<GasTankSystem>();

        await server.WaitAssertion(() =>
        {
            var tankUid = server.EntMan.SpawnEntity(OxygenTankPrototype, TileCentre(testMap));
            var tank = new Entity<GasTankComponent>(tankUid, server.EntMan.GetComponent<GasTankComponent>(tankUid));
            Assert.That(tank.Comp.Air.FluidCapable, "Gas tank air cannot hold fluid.");

            FillWithFluid(tank.Comp.Air);
            var liquidBefore = tank.Comp.Air.Fluid!.LiquidUnits;
            var vaporBefore = tank.Comp.Air.Fluid.VaporMoles;

            var breath = gasTankSystem.RemoveAirOutput(tank, Atmospherics.BreathVolume);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(breath.TotalMoles, Is.GreaterThan(0f), "Internals delivered no gas.");
                Assert.That(breath.Fluid, Is.Null, "Internals delivered fluid.");
                Assert.That(tank.Comp.Air.Fluid!.LiquidUnits, Is.EqualTo(liquidBefore).Within(0.001f), "The tank lost liquid to a breath.");
                Assert.That(tank.Comp.Air.Fluid.VaporMoles, Is.EqualTo(vaporBefore).Within(1e-6f), "The tank lost vapor to a breath.");
            }
        });
    }
}
