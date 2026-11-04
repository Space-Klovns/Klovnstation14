#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Chemistry.InfiniteDispenser;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Chemistry;

/// <summary>
///     The chem dispenser makes its own reagents from nothing, as /tg/'s does, and nothing else.
/// </summary>
[TestOf(typeof(KsInfiniteReagentDispenserComponent))]
public sealed class KsInfiniteReagentDispenserTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    private static readonly EntProtoId ChemDispenser = "ChemDispenser";
    private static readonly EntProtoId LargeBeaker = "LargeBeaker";
    private static readonly ProtoId<ReagentPrototype> Carbon = "Carbon";
    private static readonly ProtoId<ReagentPrototype> Omnizine = "Omnizine";

    [Test]
    public async Task ChemDispenserMakesItsReagentsWithoutJugsAndNothingElse()
    {
        var testMap = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var dispenser = SEntMan.SpawnEntity(ChemDispenser, testMap.GridCoords);
            var beaker = SEntMan.SpawnEntity(LargeBeaker, testMap.GridCoords);
            var chemist = SEntMan.SpawnEntity(null, testMap.GridCoords);
            Assert.That(Server.System<ItemSlotsSystem>().TryInsert(dispenser, SharedReagentDispenser.OutputSlotName, beaker, user: null),
                Is.True, "precondition: the beaker goes in");
            Assert.That(SEntMan.GetComponent<StorageComponent>(dispenser).Container.ContainedEntities, Is.Empty,
                "it has no jugs to pour from");

            // 10u a press, the default amount.
            for (var i = 0; i < 3; i++)
            {
                Dispense(dispenser, chemist, Carbon);
            }
            Dispense(dispenser, chemist, Omnizine);

            Assert.That(Server.System<SharedSolutionContainerSystem>().TryGetFitsInDispenser(beaker, out _, out var solution), Is.True);
            Assert.That(solution!.GetTotalPrototypeQuantity(Carbon), Is.EqualTo(FixedPoint2.New(30)),
                "three presses make 30u of carbon from nothing");
            Assert.That(solution.GetTotalPrototypeQuantity(Omnizine), Is.EqualTo(FixedPoint2.Zero),
                "a reagent it doesn't make is refused");
        });
    }

    // Raised straight on the machine, as the interface would after checking the window is open.
    private void Dispense(EntityUid dispenser, EntityUid actor, ProtoId<ReagentPrototype> reagent)
    {
        SEntMan.EventBus.RaiseLocalEvent(dispenser, new KsReagentDispenserDispenseInfiniteMessage(reagent)
        {
            Actor = actor,
            UiKey = ReagentDispenserUiKey.Key,
        });
    }
}
