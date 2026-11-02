#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Fluidmos.CookOff;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Jittering;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     A pipe net of explosive fluid past its cook-off point rattles its bends first, then ruptures them; one cold, or
///         cooled, stops.
/// </summary>
[TestOf(typeof(KsPipeCookOffSystem))]
public sealed class KsPipeCookOffTest : GameTest
{
    // An explosive whose spray stays a puddle, so the test can see it: welding fuel, at a net's temperature, is far past
    //      its 188 K boiling point and boils off into smoke as soon as the puddles' tile effects next run.
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestCookOffSludge
  name: reagent-name-welding-fuel
  desc: reagent-desc-welding-fuel
  physicalDesc: reagent-physical-desc-oily
  boilingPoint: 5000
  meltingPoint: 100
  ksExplosivePower: 1
  ksCookOffTemperature: 400
";

    // Deliberately not static: it exists only in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _sludgeReagent = "KsTestCookOffSludge";

    private static readonly ProtoId<ReagentPrototype> WeldingFuelReagent = "WeldingFuel";
    private static readonly EntProtoId StraightPipePrototype = "GasPipeStraight";
    private static readonly EntProtoId BendPipePrototype = "GasPipeBend";

    private static readonly KsFluidReagentKey WeldingFuelKey = new(WeldingFuelReagent, null);

    private const int TicksPerSecond = 30;

    /// <summary>
    ///     Straight pipes running north from the origin.
    /// </summary>
    private const int ColumnLength = 4;

    private const float HotTemperature = 600f;

    /// <summary>
    ///     A column of straight pipes, a bend at its top turning west, and one lateral pipe past it: one net, one bend.
    /// </summary>
    private async Task<(TestMapData TestMap, List<EntityUid> Pipes, EntityUid BendUid)> CreateNet()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var pipes = new List<EntityUid>();
        EntityUid bendUid = default;

        await server.WaitPost(() =>
        {
            // Pipe nets join their grid's atmosphere when they form, so it comes first.
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            var transformSystem = server.System<SharedTransformSystem>();
            var gridUid = testMap.Grid.Owner;

            EntityUid SpawnPipe(EntProtoId prototype, Vector2i tile, Angle rotation)
            {
                mapSystem.SetTile(gridUid, testMap.Grid.Comp, tile, testMap.Tile.Tile);
                var pipeUid = server.EntMan.SpawnEntity(prototype, new EntityCoordinates(gridUid, tile.X + 0.5f, tile.Y + 0.5f));
                transformSystem.SetLocalRotation(pipeUid, rotation);
                pipes.Add(pipeUid);
                return pipeUid;
            }

            for (var y = 0; y < ColumnLength; y++)
                SpawnPipe(StraightPipePrototype, new Vector2i(0, y), Angle.Zero);

            // A bend's base shape opens south and west.
            bendUid = SpawnPipe(BendPipePrototype, new Vector2i(0, ColumnLength), Angle.Zero);
            SpawnPipe(StraightPipePrototype, new Vector2i(-1, ColumnLength), Angle.FromDegrees(90));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(Net(bendUid).Nodes, Has.Count.EqualTo(pipes.Count), "The test's pipes did not join into one net.");
            Assert.That(server.EntMan.GetComponent<NodeContainerComponent>(bendUid).Nodes.Values.OfType<PipeNode>().Single().CurrentPipeDirection,
                Is.EqualTo(PipeDirection.SWBend), "The bend is not a bend.");
        });

        return (testMap, pipes, bendUid);
    }

    private IPipeNet Net(EntityUid pipeUid)
    {
        var node = Pair.Server.EntMan.GetComponent<NodeContainerComponent>(pipeUid).Nodes.Values.OfType<PipeNode>().Single();
        return (IPipeNet)node.NodeGroup!;
    }

    /// <summary>
    ///     Fills the net with welding fuel vapor: hot, liquid fuel would boil, and the latent heat cool the sealed net
    ///         below its cook-off point.
    /// </summary>
    private void Fill(IPipeNet net, int nodeCount, float powerPerNode, float temperature)
    {
        Assert.That(Pair.Server.System<KsFluidmosPropertiesSystem>().TryGetProperties(WeldingFuelReagent, out var properties));
        var fuelUnits = powerPerNode * nodeCount / Pair.Server.ProtoMan.Index(WeldingFuelReagent).KsExplosivePower;
        net.Air.Temperature = temperature;
        net.Air.Fluid = new KsFluidMixture();
        net.Air.Fluid.SetAmount(WeldingFuelKey, new KsFluidAmount(0f, 0f, fuelUnits * properties!.MolesPerUnit));
    }

    /// <summary>
    ///     Fills the net with liquid <see cref="_sludgeReagent"/>, which stays liquid however hot.
    /// </summary>
    private void FillWithSludge(IPipeNet net, int nodeCount, float powerPerNode, float temperature)
    {
        net.Air.Temperature = temperature;
        net.Air.Fluid = new KsFluidMixture();
        net.Air.Fluid.AddLiquid(new KsFluidReagentKey(_sludgeReagent, null), powerPerNode * nodeCount);
    }

    private int Rattling(List<EntityUid> pipes)
    {
        return pipes.Count(uid => Pair.Server.EntMan.HasComponent<KsPipeCookOffComponent>(uid));
    }

    /// <summary>
    ///     Rattles as many pipes as its power takes, the bend among them, without rupturing them; then, once its time is
    ///         up, ruptures them.
    /// </summary>
    [Test]
    public async Task HotNetRattlesItsBendFirstThenRupturesIt()
    {
        var server = Pair.Server;
        var (_, pipes, bendUid) = await CreateNet();

        // Twice the reference density, so it takes half the reference time, and two pipes' worth of ruptures.
        const float powerPerNode = KsPipeCookOffSystem.ReferencePowerPerNode * 2f;
        var totalPower = powerPerNode * pipes.Count;
        var expectedRuptures = (int)MathF.Ceiling(totalPower / KsPipeCookOffSystem.PowerPerRupture);
        var seconds = Math.Clamp(KsPipeCookOffSystem.ReferenceSeconds / 2f, KsPipeCookOffSystem.MinSeconds, KsPipeCookOffSystem.MaxSeconds);
        Assert.That(expectedRuptures, Is.InRange(2, pipes.Count - 1), "The test's net would rupture every pipe or only the bend, so the bend going first proves nothing.");

        await server.WaitPost(() => Fill(Net(bendUid), pipes.Count, powerPerNode, HotTemperature));
        await server.WaitRunTicks(TicksPerSecond * 2);

        List<EntityUid> rattling = [];
        await server.WaitAssertion(() =>
        {
            rattling = pipes.Where(uid => server.EntMan.HasComponent<KsPipeCookOffComponent>(uid)).ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(rattling, Has.Count.EqualTo(expectedRuptures), $"The net did not rattle as many pipes as its power ruptures (net at {Net(bendUid).Air.Temperature} K, {Net(bendUid).Air.Fluid?.LiquidUnits} u liquid, {Net(bendUid).Air.Fluid?.VaporMoles} mol vapor, {Net(bendUid).Air.Pressure} kPa).");
                Assert.That(rattling, Does.Contain(bendUid), "The bend, the weakest link, was not among the pipes rattling.");
                Assert.That(rattling.All(uid => server.EntMan.HasComponent<JitteringComponent>(uid)), "A rattling pipe does not jitter.");
                Assert.That(pipes.All(uid => server.EntMan.EntityExists(uid)), "A pipe ruptured before its time.");
            }
        });

        await server.WaitRunTicks((int)MathF.Ceiling(seconds + 2f) * TicksPerSecond);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(rattling.All(uid => !server.EntMan.EntityExists(uid)), "The rattling pipes did not rupture once their time was up.");
                Assert.That(pipes.Count(uid => server.EntMan.EntityExists(uid)), Is.LessThan(pipes.Count), "Nothing ruptured.");
            }
        });
    }

    /// <summary>
    ///     Dense enough, a net ruptures on the spot, before the shortest build-up could have run.
    /// </summary>
    [Test]
    public async Task DenseNetRupturesAtOnce()
    {
        var server = Pair.Server;
        var (testMap, pipes, bendUid) = await CreateNet();

        var bendTile = Vector2i.Zero;
        await server.WaitPost(() =>
        {
            var transformComponent = server.EntMan.GetComponent<TransformComponent>(bendUid);
            bendTile = server.System<SharedMapSystem>().TileIndicesFor(transformComponent.GridUid!.Value,
                server.EntMan.GetComponent<MapGridComponent>(transformComponent.GridUid.Value), transformComponent.Coordinates);
            FillWithSludge(Net(bendUid), pipes.Count, KsPipeCookOffSystem.InstantPowerPerNode * 1.5f, HotTemperature);
        });

        // Tick by tick, to catch the chain between its first burst and its second.
        var checkTicks = (int)(KsPipeCookOffSystem.CheckInterval.TotalSeconds * TicksPerSecond) + 1;
        for (var i = 0; i < checkTicks && server.EntMan.EntityExists(bendUid); i++)
            await server.WaitRunTicks(1);

        // The spray lands on the next update.
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(server.EntMan.EntityExists(bendUid), Is.False, "A net past the instant density did not burst its bend at once.");
                Assert.That(pipes.Count(uid => server.EntMan.EntityExists(uid)), Is.GreaterThan(0),
                    "Every pipe burst at the same moment, rather than in a chain from the bend.");

                Assert.That(server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Grid.Owner, bendTile, out var puddle) &&
                            puddle.Comp.Solution is { } solution &&
                            solution.Comp.Solution.GetTotalPrototypeQuantity(_sludgeReagent) > FixedPoint2.Zero,
                    "The burst bend sprayed none of its net's explosive onto its tile.");
            }
        });

        await server.WaitRunTicks((int)MathF.Ceiling((float)KsPipeCookOffSystem.ChainDelay.TotalSeconds * pipes.Count * TicksPerSecond) + 2);
        await server.WaitAssertion(() =>
            Assert.That(pipes.Count(uid => server.EntMan.EntityExists(uid)), Is.Zero, "The chain stopped before bursting every pipe its power takes."));
    }

    /// <summary>
    ///     With the spill fraction at 0, a burst sprays nothing and the explosions take it all.
    /// </summary>
    [Test]
    public async Task NoSprayAtZeroSpillFraction()
    {
        var server = Pair.Server;
        await OverrideCVar(Side.Server, KsCCVars.AtmosPipeCookOffSpillFraction, 0f);
        var (testMap, pipes, bendUid) = await CreateNet();

        var bendTile = Vector2i.Zero;
        await server.WaitPost(() =>
        {
            var transformComponent = server.EntMan.GetComponent<TransformComponent>(bendUid);
            bendTile = server.System<SharedMapSystem>().TileIndicesFor(transformComponent.GridUid!.Value,
                server.EntMan.GetComponent<MapGridComponent>(transformComponent.GridUid.Value), transformComponent.Coordinates);
            FillWithSludge(Net(bendUid), pipes.Count, KsPipeCookOffSystem.InstantPowerPerNode * 1.5f, HotTemperature);
        });

        var checkTicks = (int)(KsPipeCookOffSystem.CheckInterval.TotalSeconds * TicksPerSecond) + 1;
        for (var i = 0; i < checkTicks && server.EntMan.EntityExists(bendUid); i++)
            await server.WaitRunTicks(1);

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(server.EntMan.EntityExists(bendUid), Is.False, "The bend did not burst, so this proves nothing.");
                Assert.That(server.System<PuddleSystem>().TryGetCachedPuddle(testMap.Grid.Owner, bendTile, out var puddle) &&
                            puddle.Comp.Solution is { } solution &&
                            solution.Comp.Solution.GetTotalPrototypeQuantity(_sludgeReagent) > FixedPoint2.Zero,
                    Is.False, "A burst sprayed its explosive with the spill fraction at 0.");
            }
        });
    }

    /// <summary>
    ///     Below its cook-off point, a net holds; cooled back below it while rattling, it stops.
    /// </summary>
    [Test]
    public async Task ColdNetHoldsAndCoolingStopsTheRattle()
    {
        var server = Pair.Server;
        var (_, pipes, bendUid) = await CreateNet();
        var cookOffTemperature = server.ProtoMan.Index(WeldingFuelReagent).KsCookOffTemperature!.Value;

        await server.WaitPost(() => Fill(Net(bendUid), pipes.Count, KsPipeCookOffSystem.ReferencePowerPerNode, Atmospherics.T20C));
        await server.WaitRunTicks(TicksPerSecond * 3);
        await server.WaitAssertion(() => Assert.That(Rattling(pipes), Is.Zero, "A net below its cook-off point rattled."));

        await server.WaitPost(() => Net(bendUid).Air.Temperature = HotTemperature);
        await server.WaitRunTicks(TicksPerSecond * 2);
        await server.WaitAssertion(() => Assert.That(Rattling(pipes), Is.GreaterThan(0), "A hot net did not rattle, so this proves nothing."));

        await server.WaitPost(() => Net(bendUid).Air.Temperature = cookOffTemperature - 50f);
        await server.WaitRunTicks(TicksPerSecond * 2);
        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Rattling(pipes), Is.Zero, "A cooled net kept rattling.");
                Assert.That(pipes.Any(uid => server.EntMan.HasComponent<JitteringComponent>(uid)), Is.False, "A pipe kept jittering after its net cooled.");
                Assert.That(pipes.All(uid => server.EntMan.EntityExists(uid)), "A cooled net ruptured.");
            }
        });
    }
}
