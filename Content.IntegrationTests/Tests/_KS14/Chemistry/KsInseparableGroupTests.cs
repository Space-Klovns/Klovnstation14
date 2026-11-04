#nullable enable
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
";

    private static readonly EntProtoId ChemMasterPrototype = "ChemMaster";
    private static readonly EntProtoId BeakerPrototype = "Beaker";
    private static readonly EntProtoId FilterPrototype = "PlumbingFilter";
    private static readonly EntProtoId TankPrototype = "PlumbingTank";

    // Instance fields: these ids exist only in the test prototypes above.
    private readonly ProtoId<ReagentPrototype> _heavyReagent = "KsTestGroupHeavy";
    private readonly ProtoId<ReagentPrototype> _lightReagent = "KsTestGroupLight";
    private readonly ProtoId<ReagentPrototype> _lonerReagent = "KsTestGroupLoner";

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
