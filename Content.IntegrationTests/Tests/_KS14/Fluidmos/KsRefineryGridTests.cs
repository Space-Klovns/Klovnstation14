#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos.Devices;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._KS14.Fluidmos;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.NodeContainer;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Loads the demonstration refinery and lets it run: everything is powered, and crude comes out of the far end split
///         into cuts, with the reformer making gasoline and hydrogen gas. A content test: it's meant to fail when that map
///         stops working, so a change to its layout or pump rates may need the map retuned. It finds things by what they
///         hold, so it doesn't care where they are.
/// </summary>
[TestOf(typeof(KsCatalyticReactorSystem))]
public sealed class KsRefineryGridTests : GameTest
{
    /// <summary>
    ///     How far a working train has to have moved a cut from crude. Light ends flash off unheated pipe, so any difference
    ///         at all would pass with the stills doing nothing.
    /// </summary>
    private const float MinimumEnrichment = 2f;

    public override PoolSettings PoolSettings => new() { Connected = false, Dirty = true };

    private static readonly ResPath GridPath = new("/Maps/_KS14/Grids/refinery.yml");
    private const string TankSolution = "tank";

    private static readonly ProtoId<ReagentPrototype> NaphthaReagent = "KsNaphtha";
    private static readonly ProtoId<ReagentPrototype> BitumenReagent = "KsBitumen";
    private static readonly ProtoId<ReagentPrototype> GasolineReagent = "KsGasoline";
    private static readonly EntProtoId CrudeGeyserPrototype = "KsGeyserCrudeOil";

    private EntityUid _grid;

    private Vector2i TileOf(EntityUid uid)
    {
        var server = Pair.Server;
        var coordinates = server.System<SharedTransformSystem>().GetMoverCoordinates(uid);
        return server.System<SharedMapSystem>().CoordinatesToTile(_grid, server.EntMan.GetComponent<MapGridComponent>(_grid), coordinates);
    }

    private static float Share(Solution solution, ProtoId<ReagentPrototype> reagent)
    {
        return (float)solution.GetTotalPrototypeQuantity(reagent) / (float)solution.Volume;
    }

    [Test]
    public async Task RefineryRunsFromTheGeyserToItsProducts()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var solutionSystem = server.System<SharedSolutionContainerSystem>();
        var tickRate = (int)server.ResolveDependency<IGameTiming>().TickRate;

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().CreateMap(out var mapId);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, GridPath, out var grid));
            _grid = grid!.Value.Owner;
        });

        // The crude's own composition, to compare the cuts against.
        var crudeBitumenShare = 0f;
        var crudeNaphthaShare = 0f;
        await server.WaitAssertion(() =>
        {
            var query = entityManager.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var metaData))
            {
                if (metaData.EntityPrototype?.ID != CrudeGeyserPrototype.Id)
                    continue;

                Assert.That(solutionSystem.TryGetSolution(uid, "solution", out _, out var crude));
                crudeBitumenShare = Share(crude!, BitumenReagent);
                crudeNaphthaShare = Share(crude!, NaphthaReagent);
            }

            Assert.That(crudeBitumenShare, Is.GreaterThan(0f), "The refinery has no crude geyser.");
        });

        // Long enough to start up and fill the stills: the plant moves a few units a second.
        await server.WaitRunTicks(tickRate * 60 * 20);

        await server.WaitAssertion(() =>
        {
            var unpowered = entityManager.EntityQuery<ApcPowerReceiverComponent>()
                .Where(receiver => !receiver.Powered && !receiver.PowerDisabled)
                .Select(receiver => $"{entityManager.GetComponent<MetaDataComponent>(receiver.Owner).EntityPrototype?.ID} at {TileOf(receiver.Owner)}")
                .ToList();

            var gasoline = 0f;
            var nodeQuery = entityManager.EntityQueryEnumerator<NodeContainerComponent>();
            var seenGroups = new HashSet<object>();
            while (nodeQuery.MoveNext(out _, out var nodeContainer))
            {
                foreach (var node in nodeContainer.Nodes.Values)
                {
                    if (node is not PipeNode pipe || pipe.NodeGroup is not { } group || !seenGroups.Add(group) || pipe.Air.Fluid is not { } fluid)
                        continue;

                    if (fluid.TryGetAmount(new KsFluidReagentKey(GasolineReagent, null), out var amount))
                        gasoline += amount.Liquid + amount.Vapor;
                }
            }

            // Whichever tank holds the heaviest cut is the bottom of the train, wherever it is.
            Solution? residue = null;
            var tankQuery = entityManager.EntityQueryEnumerator<PlumbingStorageComponent>();
            while (tankQuery.MoveNext(out var tankUid, out _))
            {
                if (solutionSystem.TryGetSolution(tankUid, TankSolution, out _, out var tank) &&
                    tank.Volume > 0 &&
                    (residue == null || Share(tank, BitumenReagent) > Share(residue, BitumenReagent)))
                    residue = tank;
            }

            var hydrogenMoles = 0f;
            var canisterQuery = entityManager.EntityQueryEnumerator<GasCanisterComponent>();
            while (canisterQuery.MoveNext(out var canister))
            {
                hydrogenMoles += canister.Air.GetMoles(Gas.Hydrogen);
            }

            Assert.That(residue, Is.Not.Null, "No tank on the refinery holds anything.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(unpowered, Is.Empty, "Something on the refinery has no power.");
                Assert.That(Share(residue!, BitumenReagent), Is.GreaterThan(crudeBitumenShare * MinimumEnrichment), "The heaviest tank isn't much richer in bitumen than crude is, so the stills barely split anything.");
                Assert.That(Share(residue!, NaphthaReagent), Is.LessThan(crudeNaphthaShare / MinimumEnrichment), "The heaviest cut still holds nearly as much naphtha as crude does.");
                Assert.That(gasoline, Is.GreaterThan(0f), "The reformer made no gasoline.");
                Assert.That(hydrogenMoles, Is.GreaterThan(0f), "No hydrogen gas reached a canister.");
            }
        });
    }
}
