#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._Starlight.Plumbing.EntitySystems;
using Content.Server._Starlight.Plumbing.Nodes;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Plumbing;

/// <summary>
///     Plumbing moves reagents as themselves, data and all, so blood keeps its DNA; and machines that count what they
///         hold count every data variant of a reagent, not only the one without data.
/// </summary>
[TestOf(typeof(PlumbingPullSystem))]
public sealed class KsPlumbingReagentDataTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> BloodReagent = "Blood";

    private static readonly EntProtoId PlumbingInputPrototype = "PlumbingInput";
    private static readonly EntProtoId PlumbingTankPrototype = "PlumbingTank";
    private static readonly EntProtoId PlumbingReactorPrototype = "PlumbingReactor";
    private static readonly EntProtoId PlumbingSmartFridgePrototype = "PlumbingSmartFridge";

    private static ReagentId Blood(string dna)
    {
        return new ReagentId(BloodReagent, new List<ReagentData> { new DnaData { DNA = dna } });
    }

    /// <summary>
    ///     A machine at the origin, and a plumbing input on the tile north of it, where the machine's inlet faces once it
    ///         is turned to <paramref name="machineRotation"/>.
    /// </summary>
    private async Task<(EntityUid Machine, EntityUid Input)> SpawnMachineUnderInput(EntProtoId machinePrototype, Angle machineRotation)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid machineUid = default;
        EntityUid inputUid = default;

        await server.WaitPost(() =>
        {
            var mapSystem = server.System<SharedMapSystem>();
            var north = testMap.Tile.GridIndices + Vector2i.Up;
            mapSystem.SetTile(testMap.Grid, north, testMap.Tile.Tile);

            machineUid = server.EntMan.SpawnEntity(machinePrototype, testMap.GridCoords);
            server.System<SharedTransformSystem>().SetLocalRotation(machineUid, machineRotation);
            inputUid = server.EntMan.SpawnEntity(PlumbingInputPrototype, mapSystem.GridTileToLocal(testMap.Grid, testMap.Grid.Comp, north));
        });

        await server.WaitRunTicks(5);
        return (machineUid, inputUid);
    }

    private Entity<SolutionComponent> SolutionEntity(EntityUid uid, string name)
    {
        Assert.That(Pair.Server.System<SharedSolutionContainerSystem>().TryGetSolution(uid, name, out var solutionEntity, out _),
            $"{uid} has no {name} solution.");
        return solutionEntity!.Value;
    }

    [Test]
    public async Task PullingSpecificReagentsKeepsEachDataVariant()
    {
        var server = Pair.Server;
        // The tank's inlet points north already.
        var (tankUid, inputUid) = await SpawnMachineUnderInput(PlumbingTankPrototype, Angle.Zero);
        var pullSystem = server.System<PlumbingPullSystem>();

        await server.WaitAssertion(() =>
        {
            var input = SolutionEntity(inputUid, "input").Comp.Solution;
            input.AddReagent(Blood("AAAA"), FixedPoint2.New(5));
            input.AddReagent(Blood("CCCC"), FixedPoint2.New(5));

            Assert.That(server.System<NodeContainerSystem>().TryGetNode(tankUid, "inlet", out PlumbingNode? inlet) && inlet!.PlumbingNet != null,
                "The tank's inlet isn't on a plumbing network.");
            var tank = SolutionEntity(tankUid, "tank");
            var pulled = pullSystem.PullSpecificReagents(tankUid,
                inlet!.PlumbingNet!,
                tank,
                new Dictionary<string, FixedPoint2> { [BloodReagent.Id] = FixedPoint2.New(8) },
                FixedPoint2.New(20));

            var tankSolution = tank.Comp.Solution;
            using (Assert.EnterMultipleScope())
            {
                Assert.That((float)pulled.GetValueOrDefault(BloodReagent.Id), Is.EqualTo(8f).Within(0.01f), "The pull didn't take what was asked.");
                Assert.That((float)(tankSolution.GetReagentQuantity(Blood("AAAA")) + tankSolution.GetReagentQuantity(Blood("CCCC"))), Is.EqualTo(8f).Within(0.01f),
                    "Blood pulled for a reactor lost its DNA.");
                Assert.That((float)tankSolution.GetReagentQuantity(new ReagentId(BloodReagent, null)), Is.Zero, "Some of the blood was made generic.");
                Assert.That((float)input.GetTotalPrototypeQuantity(BloodReagent), Is.EqualTo(2f).Within(0.01f), "The input didn't give up what was pulled.");
            }
        });
    }

    [Test]
    public async Task SmartFridgeCountsBloodWithDnaTowardsItsLimit()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid fridgeUid = default;
        await server.WaitPost(() => fridgeUid = server.EntMan.SpawnEntity(PlumbingSmartFridgePrototype, testMap.GridCoords));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var fridge = server.EntMan.GetComponent<PlumbingSmartFridgeComponent>(fridgeUid);
            SolutionEntity(fridgeUid, fridge.SolutionName).Comp.Solution.AddReagent(Blood("AAAA"), fridge.MaxPerReagent);

            var ev = new Content.Server._Starlight.Plumbing.PlumbingPullIntoAttemptEvent(EntityUid.Invalid, BloodReagent.Id, FixedPoint2.New(10));
            server.EntMan.EventBus.RaiseLocalEvent(fridgeUid, ref ev);
            Assert.That(ev.Cancelled, "A smart fridge full of blood with DNA still took more.");
        });
    }

    [Test]
    public async Task ReactorCountsBloodWithDnaTowardsItsTarget()
    {
        var server = Pair.Server;
        var (reactorUid, inputUid) = await SpawnMachineUnderInput(PlumbingReactorPrototype, Angle.Zero);
        const float target = 10f;

        await server.WaitPost(() =>
        {
            var reactor = server.EntMan.GetComponent<PlumbingReactorComponent>(reactorUid);
            reactor.ReagentTargets[BloodReagent] = FixedPoint2.New(target);
            // Already met, with blood that has DNA.
            SolutionEntity(reactorUid, reactor.BufferSolutionName).Comp.Solution.AddReagent(Blood("AAAA"), FixedPoint2.New(target));
            SolutionEntity(inputUid, "input").Comp.Solution.AddReagent(Blood("CCCC"), FixedPoint2.New(30));
        });
        await server.WaitRunTicks((int)(5f * server.ResolveDependency<IGameTiming>().TickRate));

        await server.WaitAssertion(() =>
        {
            var reactor = server.EntMan.GetComponent<PlumbingReactorComponent>(reactorUid);
            var buffer = SolutionEntity(reactorUid, reactor.BufferSolutionName).Comp.Solution;
            Assert.That((float)buffer.GetTotalPrototypeQuantity(BloodReagent), Is.EqualTo(target).Within(0.01f),
                "A reactor whose target was met with blood that has DNA pulled more.");
        });
    }
}
