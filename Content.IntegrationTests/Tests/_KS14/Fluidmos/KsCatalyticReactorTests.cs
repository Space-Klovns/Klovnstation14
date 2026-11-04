#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Devices;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     Catalysed pipe reactions run only in the net of a catalytic reactor holding their catalyst, and a bed loses
///         activity to the poisons it takes up.
/// </summary>
[TestOf(typeof(KsCatalyticReactorSystem))]
public sealed class KsCatalyticReactorTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestCatalysisFeed
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestCatalysisProduct
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: reagent
  id: KsTestCatalysisPoison
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  boilingPoint: 5000
  meltingPoint: 100

- type: ksFluidCatalyst
  id: KsTestCatalyst
  name: reagent-name-water
  poisons:
    KsTestCatalysisPoison: 0.05
  poisonUptakeRate: 1

- type: ksFluidReaction
  id: KsTestCatalysedReaction
  catalyst: KsTestCatalyst
  reactants:
    KsTestCatalysisFeed: { amount: 1 }
  products:
    KsTestCatalysisProduct: { amount: 1 }
  minTemp: 600
  rate: 0.5
  referenceTemperature: 700

- type: entity
  id: KsTestCatalystBed
  name: test catalyst bed
  components:
  - type: Item
  - type: KsFluidCatalyst
    catalyst: KsTestCatalyst
";

    private static readonly EntProtoId ReactorPrototype = "KsCatalyticReactor";

    // Instance fields: these ids exist only in the test prototypes above.
    private readonly EntProtoId _bedPrototype = "KsTestCatalystBed";
    private readonly ProtoId<ReagentPrototype> _feedReagent = "KsTestCatalysisFeed";
    private readonly ProtoId<ReagentPrototype> _productReagent = "KsTestCatalysisProduct";
    private readonly ProtoId<ReagentPrototype> _poisonReagent = "KsTestCatalysisPoison";

    private const float Temperature = 700f;

    private static void Update(IEntityManager entityManager, EntityUid deviceUid, float deltaTime)
    {
        var ev = new AtmosDeviceUpdateEvent(deltaTime, null, null);
        entityManager.EventBus.RaiseLocalEvent(deviceUid, ref ev);
    }

    private void Fill(GasMixture mixture, ProtoId<ReagentPrototype> reagent, float units)
    {
        mixture.Clear();
        mixture.Temperature = Temperature;
        mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.OneAtmosphere * mixture.Volume / (Atmospherics.R * Temperature));
        mixture.Fluid = new KsFluidMixture();
        mixture.Fluid.AddLiquid(new KsFluidReagentKey(reagent, null), units);
    }

    private static float Liquid(GasMixture mixture, ProtoId<ReagentPrototype> reagent)
    {
        return mixture.Fluid != null && mixture.Fluid.TryGetAmount(new KsFluidReagentKey(reagent, null), out var amount) ? amount.Liquid : 0f;
    }

    private async Task<EntityUid> SpawnReactor()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid reactorUid = default;
        await server.WaitPost(() => reactorUid = server.EntMan.SpawnEntity(ReactorPrototype, testMap.GridCoords));
        await server.WaitRunTicks(5);
        return reactorUid;
    }

    [Test]
    public async Task CatalysedReactionRunsOnlyInAReactorHoldingItsCatalyst()
    {
        var server = Pair.Server;
        var reactorUid = await SpawnReactor();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var phaseSystem = server.System<KsFluidmosPhaseSystem>();
        var itemSlotsSystem = server.System<ItemSlotsSystem>();
        var gameTiming = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNode(reactorUid, "pipe", out PipeNode? node));
            var reactor = server.EntMan.GetComponent<KsCatalyticReactorComponent>(reactorUid);

            // A plain pipe, hot enough, but with no reactor.
            var pipe = new GasMixture(200f) { FluidCapable = true };
            Fill(pipe, _feedReagent, 100f);
            for (var i = 0; i < 6; i++)
            {
                pipe.Fluid!.LastPhaseTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5);
                if (pipe.Fluid.LastReactionTime != null)
                    pipe.Fluid.LastReactionTime = gameTiming.CurTime - TimeSpan.FromSeconds(0.5);
                phaseSystem.UpdatePhases(pipe, holder: null);
            }

            // The reactor, with its slot empty.
            Fill(node!.Air, _feedReagent, 100f);
            for (var i = 0; i < 6; i++)
            {
                Update(server.EntMan, reactorUid, 0.5f);
            }

            var emptyProduct = Liquid(node.Air, _productReagent);
            var emptyReacting = reactor.Reacting;

            // The reactor, with a bed.
            var bedUid = server.EntMan.SpawnEntity(_bedPrototype, server.EntMan.GetComponent<TransformComponent>(reactorUid).Coordinates);
            Assert.That(itemSlotsSystem.TryInsert(reactorUid, reactor.CatalystSlotId, bedUid, user: null), "The bed didn't go in the reactor's slot.");
            for (var i = 0; i < 6; i++)
            {
                Update(server.EntMan, reactorUid, 0.5f);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Liquid(pipe, _productReagent), Is.Zero, "A catalysed reaction ran in a pipe with no reactor.");
                Assert.That(emptyProduct, Is.Zero, "A catalysed reaction ran in a reactor with no bed.");
                Assert.That(emptyReacting, Is.False, "A reactor with no bed says it's reacting.");
                Assert.That(reactor.Reacting, "A reacting reactor doesn't say so, so its sprite shows it idle.");
                Assert.That(Liquid(node.Air, _productReagent), Is.GreaterThan(1f), "A reactor holding the catalyst didn't run its reaction.");
                Assert.That(Liquid(node.Air, _feedReagent) + Liquid(node.Air, _productReagent), Is.EqualTo(100f).Within(0.01f));
            }
        });
    }

    [Test]
    public async Task PoisonsAreTakenUpAndWearTheBedDown()
    {
        var server = Pair.Server;
        var reactorUid = await SpawnReactor();
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var itemSlotsSystem = server.System<ItemSlotsSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(nodeContainerSystem.TryGetNode(reactorUid, "pipe", out PipeNode? node));
            var reactor = server.EntMan.GetComponent<KsCatalyticReactorComponent>(reactorUid);
            var bedUid = server.EntMan.SpawnEntity(_bedPrototype, server.EntMan.GetComponent<TransformComponent>(reactorUid).Coordinates);
            Assert.That(itemSlotsSystem.TryInsert(reactorUid, reactor.CatalystSlotId, bedUid, user: null));
            var bed = server.EntMan.GetComponent<KsFluidCatalystComponent>(bedUid);

            Fill(node!.Air, _poisonReagent, 5f);
            Update(server.EntMan, reactorUid, 1f);

            // A bed filling its net (400 L of 400 L) takes up 1 - e^-1 of the poison in one second.
            var taken = 5f * (1f - MathF.Exp(-1f));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Liquid(node.Air, _poisonReagent), Is.EqualTo(5f - taken).Within(0.01f), "The bed didn't take up its share of the poison.");
                Assert.That(bed.Activity, Is.EqualTo(1f - taken * 0.05f).Within(0.001f), "Taking up poison didn't wear the bed down.");
            }
        });
    }
}
