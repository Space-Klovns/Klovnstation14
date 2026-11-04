#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos;
using Content.Server._KS14.Fluidmos.Devices;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Triggers;
using Robust.Shared.Containers;
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

- type: entity
  parent: KsTestCatalystBed
  id: KsTestWornCatalystBed
  components:
  - type: KsFluidCatalyst
    catalyst: KsTestCatalyst
    activity: 0.5
";

    private static readonly EntProtoId ReactorPrototype = "KsCatalyticReactor";

    // Instance fields: these ids exist only in the test prototypes above.
    private readonly EntProtoId _bedPrototype = "KsTestCatalystBed";
    private readonly EntProtoId _wornBedPrototype = "KsTestWornCatalystBed";
    private readonly ProtoId<KsFluidReactionPrototype> _reaction = "KsTestCatalysedReaction";
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

    /// <summary>
    ///     A reactor's bed comes out at each of its damage thresholds. They're read from the reactor, so a change to its
    ///         durability doesn't change what's being tested.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task WreckingAReactorDropsItsBed(bool obliterated)
    {
        var server = Pair.Server;
        var reactorUid = await SpawnReactor();
        var itemSlotsSystem = server.System<ItemSlotsSystem>();
        var damageableSystem = server.System<DamageableSystem>();
        var containerSystem = server.System<SharedContainerSystem>();

        EntityUid bedUid = default;
        await server.WaitAssertion(() =>
        {
            var reactor = server.EntMan.GetComponent<KsCatalyticReactorComponent>(reactorUid);
            bedUid = server.EntMan.SpawnEntity(_bedPrototype, server.EntMan.GetComponent<TransformComponent>(reactorUid).Coordinates);
            Assert.That(itemSlotsSystem.TryInsert(reactorUid, reactor.CatalystSlotId, bedUid, user: null));

            var thresholds = server.EntMan.GetComponent<DestructibleComponent>(reactorUid).Thresholds
                .Select(threshold => threshold.Trigger)
                .OfType<DamageTrigger>()
                .Select(trigger => trigger.Damage)
                .ToList();
            Assert.That(thresholds, Is.Not.Empty);

            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", obliterated ? thresholds.Max() : thresholds.Min());
            damageableSystem.TryChangeDamage(reactorUid, damage, ignoreResistances: true);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(reactorUid), "The reactor survived its own damage thresholds.");
            Assert.That(server.EntMan.EntityExists(bedUid), "The catalyst bed was destroyed with its reactor.");
            Assert.That(containerSystem.IsEntityInContainer(bedUid), Is.False, "The bed is still in a container.");
        });
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

    /// <summary>
    ///     What a bed does scales with how worn it is and how much of its net it fills.
    /// </summary>
    [Test]
    public async Task BedActivityAndShareOfItsNetScaleItsRate()
    {
        var server = Pair.Server;
        var nodeContainerSystem = server.System<NodeContainerSystem>();
        var itemSlotsSystem = server.System<ItemSlotsSystem>();
        var freshUid = await SpawnReactor();
        var wornUid = await SpawnReactor();
        var dilutedUid = await SpawnReactor();

        await server.WaitAssertion(() =>
        {
            float Product(EntityUid reactorUid, EntProtoId bed, float volumeScale)
            {
                Assert.That(nodeContainerSystem.TryGetNode(reactorUid, "pipe", out PipeNode? node));
                var reactor = server.EntMan.GetComponent<KsCatalyticReactorComponent>(reactorUid);
                var bedUid = server.EntMan.SpawnEntity(bed, server.EntMan.GetComponent<TransformComponent>(reactorUid).Coordinates);
                Assert.That(itemSlotsSystem.TryInsert(reactorUid, reactor.CatalystSlotId, bedUid, user: null));

                node!.Air.Volume *= volumeScale;
                Fill(node.Air, _feedReagent, 100f);
                Update(server.EntMan, reactorUid, 0.1f);
                return Liquid(node.Air, _productReagent);
            }

            var reaction = server.ProtoMan.Index(_reaction);
            float Expected(float scale) => 100f * (1f - MathF.Exp(-reaction.Rate * 0.1f * scale));

            // Twice its bed's volume, whatever that is, so that the bed fills half its net.
            var reactorVolume = server.EntMan.GetComponent<KsCatalyticReactorComponent>(dilutedUid).BedVolume;
            Assert.That(nodeContainerSystem.TryGetNode(dilutedUid, "pipe", out PipeNode? dilutedNode));
            var dilution = 2f * reactorVolume / dilutedNode!.Air.Volume;

            var fresh = Product(freshUid, _bedPrototype, 1f);
            var worn = Product(wornUid, _wornBedPrototype, 1f);
            var diluted = Product(dilutedUid, _bedPrototype, dilution);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fresh, Is.EqualTo(Expected(1f)).Within(0.05f));
                Assert.That(worn, Is.EqualTo(Expected(0.5f)).Within(0.05f), "A half-worn bed didn't run at half rate.");
                Assert.That(diluted, Is.EqualTo(Expected(0.5f)).Within(0.05f), "A bed filling half its net didn't run at half rate.");
            }
        });
    }

    [Test]
    public async Task UnanchoringStopsShowingAReaction()
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
            Fill(node!.Air, _feedReagent, 100f);
            Update(server.EntMan, reactorUid, 0.5f);
            Assert.That(reactor.Reacting, "The reactor never started.");

            server.System<SharedTransformSystem>().Unanchor(reactorUid);
            Assert.That(reactor.Reacting, Is.False, "A reactor taken off its pipes still shows a reaction.");
        });
    }

    /// <summary>
    ///     As a pipe does: the gas goes into the room, not away with the reactor.
    /// </summary>
    [Test]
    public async Task BreakingAReactorLeaksItsGas()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var atmosphereSystem = server.System<AtmosphereSystem>();
        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            atmosphereSystem.SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });
        await server.WaitRunTicks(30);

        EntityUid reactorUid = default;
        await server.WaitPost(() => reactorUid = server.EntMan.SpawnEntity(ReactorPrototype, testMap.GridCoords));
        await server.WaitRunTicks(5);

        const float leakedOxygen = 50f;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<NodeContainerSystem>().TryGetNode(reactorUid, "pipe", out PipeNode? node));
            node!.Air.Clear();
            node.Air.Temperature = Atmospherics.T20C;
            node.Air.SetMoles(Gas.Oxygen, leakedOxygen);

            var thresholds = server.EntMan.GetComponent<DestructibleComponent>(reactorUid).Thresholds
                .Select(threshold => threshold.Trigger)
                .OfType<DamageTrigger>()
                .Select(trigger => trigger.Damage)
                .ToList();

            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", thresholds.Min());
            server.System<DamageableSystem>().TryChangeDamage(reactorUid, damage, ignoreResistances: true);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(reactorUid), "Breaking the reactor didn't remove it.");
            var tile = atmosphereSystem.GetTileMixture(testMap.Grid.Owner, testMap.MapUid, testMap.Tile.GridIndices);
            Assert.That(tile?.GetMoles(Gas.Oxygen), Is.GreaterThan(leakedOxygen * 0.5f), "The reactor's gas didn't reach the room.");
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

            // A bed takes up its share of the net's poison, 1 - e^-share of it in one second.
            var share = MathF.Min(1f, reactor.BedVolume / node.Air.Volume);
            var taken = 5f * (1f - MathF.Exp(-share));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Liquid(node.Air, _poisonReagent), Is.EqualTo(5f - taken).Within(0.01f), "The bed didn't take up its share of the poison.");
                Assert.That(bed.Activity, Is.EqualTo(1f - taken * 0.05f).Within(0.001f), "Taking up poison didn't wear the bed down.");
            }
        });
    }
}
