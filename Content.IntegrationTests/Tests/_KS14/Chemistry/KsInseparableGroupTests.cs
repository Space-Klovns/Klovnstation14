#nullable enable
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Interaction;
using Content.Server._Starlight.Plumbing.Components;
using Content.IntegrationTests.Fixtures;
using Content.Server.Chemistry.Components;
using Content.Server.Chemistry.EntitySystems;
using Content.Shared._KS14.Chemistry.Separation;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     Machines that pick one reagent out of a mixture move an inseparable reagent's whole group instead, in proportion,
///         and leave ordinary reagents as they were.
/// </summary>
[TestOf(typeof(KsReagentSeparationSystem))]
public sealed class KsInseparableGroupTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: ksReagentSeparationGroup
  id: KsTestSeparationGroup
  name: reagent-name-water

- type: reagent
  id: KsTestGroupHeavy
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-oily
  ksInseparableGroup: KsTestSeparationGroup

- type: reagent
  id: KsTestGroupLight
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-oily
  ksInseparableGroup: KsTestSeparationGroup

- type: reagent
  id: KsTestGroupLoner
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent

- type: reagent
  id: KsTestGroupTrigger
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent

- type: reaction
  id: KsTestGroupReaction
  reactants:
    KsTestGroupHeavy:
      amount: 1
    KsTestGroupTrigger:
      amount: 1
  products:
    KsTestGroupLoner: 2
";

    private static readonly EntProtoId ChemMasterPrototype = "ChemMaster";
    private static readonly EntProtoId BeakerPrototype = "Beaker";
    private static readonly EntProtoId FilterPrototype = "PlumbingFilter";
    private static readonly EntProtoId TankPrototype = "PlumbingTank";
    private static readonly EntProtoId ReactorPrototype = "PlumbingReactor";
    private static readonly EntProtoId FridgePrototype = "PlumbingSmartFridge";
    private static readonly EntProtoId JugPrototype = "Jug";
    private static readonly ProtoId<ReagentPrototype> DieselReagent = "KsDiesel";
    private static readonly ProtoId<ReagentPrototype> GasolineReagent = "KsGasoline";

    // Instance fields: these ids exist only in the test prototypes above.
    private readonly ProtoId<ReagentPrototype> _heavyReagent = "KsTestGroupHeavy";
    private readonly ProtoId<ReagentPrototype> _lightReagent = "KsTestGroupLight";
    private readonly ProtoId<ReagentPrototype> _lonerReagent = "KsTestGroupLoner";
    private readonly ProtoId<ReagentPrototype> _triggerReagent = "KsTestGroupTrigger";

    /// <summary>
    ///     30u heavy, 10u light (a 3:1 group) and 20u of an ordinary reagent, all times <paramref name="scale"/>.
    /// </summary>
    private Solution MakeMixture(float scale = 1f)
    {
        var solution = new Solution();
        solution.AddReagent(_heavyReagent, FixedPoint2.New(30f * scale));
        solution.AddReagent(_lightReagent, FixedPoint2.New(10f * scale));
        solution.AddReagent(_lonerReagent, FixedPoint2.New(20f * scale));
        return solution;
    }

    private float Quantity(Solution solution, ProtoId<ReagentPrototype> reagent)
    {
        return (float)solution.GetTotalPrototypeQuantity(reagent);
    }

    [Test]
    public async Task SelectingAMemberTakesTheWholeGroupInProportion()
    {
        var server = Pair.Server;
        var separationSystem = server.System<KsReagentSeparationSystem>();

        await server.WaitAssertion(() =>
        {
            var mixture = MakeMixture();
            var taken = separationSystem.SplitSelected(mixture, new ReagentId(_lightReagent, null), FixedPoint2.New(20), out var expanded);

            var lonerMixture = MakeMixture();
            var lonerTaken = separationSystem.SplitSelected(lonerMixture, new ReagentId(_lonerReagent, null), FixedPoint2.New(5), out var lonerExpanded);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(expanded, "Selecting a group member didn't say the group came along.");
                Assert.That(Quantity(taken, _heavyReagent), Is.EqualTo(15f).Within(0.02f), "The group wasn't taken in proportion.");
                Assert.That(Quantity(taken, _lightReagent), Is.EqualTo(5f).Within(0.02f), "The group wasn't taken in proportion.");
                Assert.That(Quantity(taken, _lonerReagent), Is.Zero, "An ordinary reagent came with the group.");
                Assert.That((float)taken.Volume, Is.EqualTo(20f).Within(0.001f), "The amount asked for didn't move.");

                Assert.That(lonerExpanded, Is.False);
                Assert.That((float)lonerTaken.Volume, Is.EqualTo(5f).Within(0.001f), "An ordinary reagent didn't move by itself.");
                Assert.That(Quantity(lonerMixture, _heavyReagent), Is.EqualTo(30f));

                Assert.That(separationSystem.ExpandToGroups([_lightReagent]), Is.EquivalentTo(new[] { _lightReagent, _heavyReagent }),
                    "A whitelist naming one member doesn't pick its whole group.");
            }
        });
    }

    /// <summary>
    ///     Fifty 1u transfers of a mixture with half a percent of a trace member. Rounding one way every time would leave
    ///         the trace behind, which would be a way to separate the group.
    /// </summary>
    [Test]
    public async Task SmallTransfersDoNotLeaveATraceMemberBehind()
    {
        var server = Pair.Server;
        var separationSystem = server.System<KsReagentSeparationSystem>();

        await server.WaitAssertion(() =>
        {
            var group = server.ProtoMan.Index(_heavyReagent).KsInseparableGroup!.Value;
            var source = new Solution();
            source.AddReagent(_heavyReagent, FixedPoint2.New(99.5f));
            source.AddReagent(_lightReagent, FixedPoint2.New(0.5f));

            var taken = FixedPoint2.Zero;
            var trace = FixedPoint2.Zero;
            for (var i = 0; i < 50; i++)
            {
                var part = separationSystem.SplitGroup(source, group, FixedPoint2.New(1));
                Assert.That(part.Volume, Is.EqualTo(FixedPoint2.New(1)), "A transfer didn't move exactly what was asked for.");
                taken += part.Volume;
                trace += part.GetTotalPrototypeQuantity(_lightReagent);
            }

            // A quarter of a unit expected. The bounds are about four standard deviations out.
            Assert.That((float)trace, Is.InRange(0.1f, 0.4f), "Small transfers didn't carry the trace member in proportion.");
        });
    }

    [Test]
    public async Task WhatIsTakenKeepsItsTemperature()
    {
        var server = Pair.Server;
        var separationSystem = server.System<KsReagentSeparationSystem>();

        await server.WaitAssertion(() =>
        {
            var mixture = MakeMixture();
            mixture.Temperature = 900f;
            var taken = separationSystem.SplitSelected(mixture, new ReagentId(_heavyReagent, null), FixedPoint2.New(10), out _);
            Assert.That(taken.Temperature, Is.EqualTo(900f), "Hot liquid cooled just by being moved.");
        });
    }

    private async Task<EntityUid> SpawnAnchoredReactor(TestMapData testMap)
    {
        var server = Pair.Server;
        EntityUid reactorUid = default;
        await server.WaitPost(() =>
        {
            reactorUid = server.EntMan.SpawnEntity(ReactorPrototype, testMap.GridCoords);
            if (!server.EntMan.GetComponent<TransformComponent>(reactorUid).Anchored)
                server.System<SharedTransformSystem>().AnchorEntity(reactorUid);
        });
        await server.WaitRunTicks(5);
        return reactorUid;
    }

    /// <summary>
    ///     A mixture too thin in its target can't reach it in the buffer, and it mustn't leave the reactor stuck.
    /// </summary>
    [Test]
    public async Task ReactorPassesAGroupThroughWhenItsTargetCannotFit()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var solutionSystem = server.System<SharedSolutionContainerSystem>();
        var reactorUid = await SpawnAnchoredReactor(testMap);

        await server.WaitAssertion(() =>
        {
            var reactor = server.EntMan.GetComponent<PlumbingReactorComponent>(reactorUid);
            reactor.ReagentTargets[_heavyReagent] = FixedPoint2.New(60);
            Assert.That(solutionSystem.TryGetSolution(reactorUid, reactor.BufferSolutionName, out var bufferEntity, out var buffer));
            Assert.That(solutionSystem.TryGetSolution(reactorUid, reactor.OutputSolutionName, out _, out var output));

            var mixture = new Solution();
            mixture.AddReagent(_heavyReagent, buffer!.MaxVolume / 3);
            mixture.AddReagent(_lightReagent, buffer.MaxVolume - buffer.MaxVolume / 3);
            Assert.That(solutionSystem.TryAddSolution(bufferEntity!.Value, mixture));

            var ev = new PlumbingDeviceUpdateEvent(1f);
            server.EntMan.EventBus.RaiseLocalEvent(reactorUid, ref ev);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.Zero), "A full buffer that can't reach its target stayed stuck.");
                Assert.That(Quantity(output!, _lightReagent) / Quantity(output!, _heavyReagent), Is.EqualTo(2f).Within(0.05f),
                    "The group didn't pass through as it came in.");
            }
        });
    }

    /// <summary>
    ///     After a pass, what's left of a target's group leaves with it, or each pass would leave the buffer fuller.
    /// </summary>
    [Test]
    public async Task ReactorSendsATargetsGroupOnAfterAPass()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var solutionSystem = server.System<SharedSolutionContainerSystem>();
        var reactorUid = await SpawnAnchoredReactor(testMap);

        await server.WaitAssertion(() =>
        {
            var reactor = server.EntMan.GetComponent<PlumbingReactorComponent>(reactorUid);
            reactor.ReagentTargets[_heavyReagent] = FixedPoint2.New(10);
            Assert.That(solutionSystem.TryGetSolution(reactorUid, reactor.BufferSolutionName, out var bufferEntity, out var buffer));
            Assert.That(solutionSystem.TryGetSolution(reactorUid, reactor.OutputSolutionName, out _, out var output));

            var mixture = new Solution { Temperature = reactor.TargetTemperature };
            mixture.AddReagent(_heavyReagent, FixedPoint2.New(10));
            mixture.AddReagent(_lightReagent, FixedPoint2.New(30));
            Assert.That(solutionSystem.TryAddSolution(bufferEntity!.Value, mixture));
            Assert.That(solutionSystem.TryAddReagent(bufferEntity.Value, _triggerReagent, FixedPoint2.New(5), out _));

            var ev = new PlumbingDeviceUpdateEvent(1f);
            server.EntMan.EventBus.RaiseLocalEvent(reactorUid, ref ev);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Quantity(output!, _lonerReagent), Is.GreaterThan(0f), "The reaction never ran, so this proved nothing.");
                Assert.That(Quantity(buffer!, _lightReagent), Is.Zero, "A group-mate stayed behind in the buffer.");
                Assert.That(Quantity(output!, _lightReagent), Is.EqualTo(30f).Within(0.02f), "A group-mate didn't reach the output.");
            }
        });
    }

    /// <summary>
    ///     A jug labelled for a trace of the mixture still gets the mixture, as much as it has room for.
    /// </summary>
    [Test]
    public async Task SmartFridgeFillsAJugWithTheWholeGroup()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var solutionSystem = server.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var fridgeUid = server.EntMan.SpawnEntity(FridgePrototype, testMap.GridCoords);
            var jugUid = server.EntMan.SpawnEntity(JugPrototype, testMap.GridCoords);
            var fridge = server.EntMan.GetComponent<PlumbingSmartFridgeComponent>(fridgeUid);
            Assert.That(solutionSystem.TryGetSolution(fridgeUid, fridge.SolutionName, out var fridgeEntity, out var stock));

            var mixture = new Solution();
            mixture.AddReagent(DieselReagent, FixedPoint2.New(98));
            mixture.AddReagent(GasolineReagent, FixedPoint2.New(2));
            Assert.That(solutionSystem.TryAddSolution(fridgeEntity!.Value, mixture));

            server.System<LabelSystem>().Label(jugUid, server.ProtoMan.Index(GasolineReagent).LocalizedName);
            var ev = new InteractUsingEvent(jugUid, jugUid, fridgeUid, testMap.GridCoords);
            server.EntMan.EventBus.RaiseLocalEvent(fridgeUid, ev);

            Assert.That(solutionSystem.TryGetRefillableSolution(jugUid, out _, out var jugSolution));
            var dispensed = (float)jugSolution!.Volume;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(dispensed, Is.EqualTo(100f).Within(0.01f), "The dispense was sized by the labelled reagent's own stock.");
                Assert.That((float)stock!.Volume, Is.Zero);
                Assert.That((float)jugSolution.GetTotalPrototypeQuantity(GasolineReagent), Is.EqualTo(2f).Within(0.02f), "The jug's mixture isn't the stock's.");
            }
        });
    }

    [Test]
    public async Task ChemMasterTransfersAGroupBackWhole()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var chemMasterSystem = server.System<ChemMasterSystem>();
        var solutionSystem = server.System<SharedSolutionContainerSystem>();
        var itemSlotsSystem = server.System<ItemSlotsSystem>();

        await server.WaitAssertion(() =>
        {
            var chemMasterUid = server.EntMan.SpawnEntity(ChemMasterPrototype, testMap.GridCoords);
            var beakerUid = server.EntMan.SpawnEntity(BeakerPrototype, testMap.GridCoords);
            Assert.That(solutionSystem.TryGetSolution(beakerUid, "beaker", out var beakerEntity, out var beakerSolution));
            Assert.That(solutionSystem.TryAddSolution(beakerEntity!.Value, MakeMixture()));
            Assert.That(itemSlotsSystem.TryInsert(chemMasterUid, SharedChemMaster.InputSlotName, beakerUid, user: null));

            var chemMaster = (chemMasterUid, server.EntMan.GetComponent<ChemMasterComponent>(chemMasterUid));
            chemMasterSystem.TransferReagents(chemMaster, new ReagentId(_heavyReagent, null), FixedPoint2.New(40), fromBuffer: false);
            Assert.That(Quantity(beakerSolution!, _heavyReagent), Is.Zero);

            chemMasterSystem.TransferReagents(chemMaster, new ReagentId(_lightReagent, null), FixedPoint2.New(20), fromBuffer: true);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Quantity(beakerSolution!, _heavyReagent), Is.EqualTo(15f).Within(0.02f), "Transferring back moved a member by itself.");
                Assert.That(Quantity(beakerSolution!, _lightReagent), Is.EqualTo(5f).Within(0.02f), "Transferring back moved a member by itself.");
            }
        });
    }

    [Test]
    public async Task ChemMasterTransfersAndDiscardsTheWholeGroup()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var chemMasterSystem = server.System<ChemMasterSystem>();
        var solutionSystem = server.System<SharedSolutionContainerSystem>();
        var itemSlotsSystem = server.System<ItemSlotsSystem>();

        await server.WaitAssertion(() =>
        {
            var chemMasterUid = server.EntMan.SpawnEntity(ChemMasterPrototype, testMap.GridCoords);
            var beakerUid = server.EntMan.SpawnEntity(BeakerPrototype, testMap.GridCoords);
            Assert.That(solutionSystem.TryGetSolution(beakerUid, "beaker", out var beakerEntity, out var beakerSolution));
            Assert.That(solutionSystem.TryAddSolution(beakerEntity!.Value, MakeMixture()), "The mixture didn't fit in the beaker.");
            Assert.That(itemSlotsSystem.TryInsert(chemMasterUid, SharedChemMaster.InputSlotName, beakerUid, user: null));

            var chemMaster = (chemMasterUid, server.EntMan.GetComponent<ChemMasterComponent>(chemMasterUid));
            chemMasterSystem.TransferReagents(chemMaster, new ReagentId(_heavyReagent, null), FixedPoint2.New(8), fromBuffer: false);

            Assert.That(solutionSystem.TryGetSolution(chemMasterUid, SharedChemMaster.BufferSolutionName, out _, out var buffer));
            var heavyMoved = Quantity(buffer!, _heavyReagent);
            var lightMoved = Quantity(buffer!, _lightReagent);

            chemMasterSystem.DiscardReagents(chemMaster, new ReagentId(_lonerReagent, null), FixedPoint2.New(20), fromBuffer: false);
            chemMasterSystem.DiscardReagents(chemMaster, new ReagentId(_lightReagent, null), FixedPoint2.New(4), fromBuffer: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(heavyMoved, Is.EqualTo(6f).Within(0.02f), "The ChemMaster moved a group member by itself.");
                Assert.That(lightMoved, Is.EqualTo(2f).Within(0.02f), "The ChemMaster moved a group member by itself.");
                Assert.That(Quantity(buffer!, _lonerReagent), Is.Zero);
                Assert.That(Quantity(beakerSolution!, _lonerReagent), Is.Zero, "Discarding an ordinary reagent took more than it.");
                Assert.That(Quantity(beakerSolution!, _heavyReagent) / Quantity(beakerSolution!, _lightReagent), Is.EqualTo(3f).Within(0.02f),
                    "What was left in the beaker isn't the group's own mixture.");
                Assert.That(Quantity(buffer!, _heavyReagent), Is.EqualTo(3f).Within(0.02f), "Discarding a member didn't take its group in proportion.");
                Assert.That(Quantity(buffer!, _lightReagent), Is.EqualTo(1f).Within(0.02f), "Discarding a member didn't take its group in proportion.");
            }
        });
    }

    [Test]
    public async Task PlumbingFilterPassesOnlyWholeGroups()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var solutionSystem = server.System<SharedSolutionContainerSystem>();
        EntityUid filterUid = default;
        EntityUid filteredTankUid = default;
        EntityUid passthroughTankUid = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            var transformSystem = server.System<SharedTransformSystem>();
            var origin = testMap.Tile.GridIndices;
            mapSystem.SetTile(testMap.Grid, origin + Vector2i.Left, testMap.Tile.Tile);
            mapSystem.SetTile(testMap.Grid, origin + Vector2i.Down, testMap.Tile.Tile);
            EntityCoordinates At(Vector2i offset) => mapSystem.GridTileToLocal(testMap.Grid, testMap.Grid.Comp, origin + offset);

            // The filter's filtered outlet faces west and its passthrough south; a tank's inlet faces north.
            filterUid = server.EntMan.SpawnEntity(FilterPrototype, At(Vector2i.Zero));
            passthroughTankUid = server.EntMan.SpawnEntity(TankPrototype, At(Vector2i.Down));
            filteredTankUid = server.EntMan.SpawnEntity(TankPrototype, At(Vector2i.Left));
            foreach (var degrees in new[] { 90, 180, 270 })
            {
                if (PipeDirection.North.RotatePipeDirection(Angle.FromDegrees(degrees)) == PipeDirection.East)
                    transformSystem.SetLocalRotation(filteredTankUid, Angle.FromDegrees(degrees));
            }

            // Plumbing only runs anchored.
            foreach (var uid in new[] { filterUid, passthroughTankUid, filteredTankUid })
            {
                if (!server.EntMan.GetComponent<TransformComponent>(uid).Anchored)
                    transformSystem.AnchorEntity(uid);
            }

            var filter = server.EntMan.GetComponent<PlumbingFilterComponent>(filterUid);
            filter.FilteredReagents.Add(_heavyReagent);
            Assert.That(solutionSystem.TryGetSolution(filterUid, filter.BufferSolutionName, out var bufferEntity, out _));
            // Half, to fit the filter's 40u buffer.
            Assert.That(solutionSystem.TryAddSolution(bufferEntity!.Value, MakeMixture(0.5f)), "The mixture didn't fit in the filter's buffer.");
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var nodeContainerSystem = server.System<Content.Server.NodeContainer.EntitySystems.NodeContainerSystem>();
            Assert.That(nodeContainerSystem.TryGetNode(filterUid, "outletFilter", out Content.Server._Starlight.Plumbing.Nodes.PlumbingNode? filtered));
            Assert.That(nodeContainerSystem.TryGetNode(filteredTankUid, "inlet", out Content.Server._Starlight.Plumbing.Nodes.PlumbingNode? filteredInlet));
            Assert.That(filtered!.NodeGroup, Is.SameAs(filteredInlet!.NodeGroup), "The test's filtered tank isn't plumbed to the filter.");
        });

        await server.WaitRunTicks(server.Timing.TickRate * 10);

        await server.WaitAssertion(() =>
        {
            Assert.That(solutionSystem.TryGetSolution(filteredTankUid, "tank", out _, out var filtered));
            Assert.That(solutionSystem.TryGetSolution(passthroughTankUid, "tank", out _, out var passthrough));

            // Either outlet may take the group, as a whole; which drains it first is the plumbing's business.
            var groupArrived = 0f;
            using (Assert.EnterMultipleScope())
            {
                foreach (var (name, tank) in new[] { ("filtered", filtered!), ("passthrough", passthrough!) })
                {
                    var heavy = Quantity(tank, _heavyReagent);
                    var light = Quantity(tank, _lightReagent);
                    groupArrived += heavy + light;
                    if (heavy + light <= 0f)
                        continue;

                    Assert.That(light, Is.GreaterThan(0f), $"The {name} outlet split a group member off.");
                    Assert.That(heavy / light, Is.EqualTo(3f).Within(0.05f), $"The {name} outlet's group isn't in the source's proportion.");
                }

                Assert.That(groupArrived, Is.GreaterThan(0f), "The group never left the filter.");
                Assert.That(Quantity(filtered!, _lonerReagent), Is.Zero, "The filtered outlet passed an unfiltered ordinary reagent.");
                Assert.That(Quantity(passthrough!, _lonerReagent), Is.GreaterThan(0f), "The passthrough outlet didn't pass the ordinary reagent.");
            }
        });
    }
}
