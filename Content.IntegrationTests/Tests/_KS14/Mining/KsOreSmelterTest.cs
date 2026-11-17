#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.EntityProcessor;
using Content.Shared._KS14.EntityProcessor.StackProcessor;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Mining;

/// <summary>
///     The automatic ore smelter: ore that goes in comes out as the matching sheet or ingot after its processing
///         time, and anything it cannot smelt, or any attempt without power, is turned away.
/// </summary>
[TestFixture]
[TestOf(typeof(KsStackProcessorSystem))]
public sealed class KsOreSmelterTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId Smelter = "KsOreSmelter";
    private readonly EntProtoId _doublingSmelter = "KsTestOreSmelterDoubling";

    private static readonly EntProtoId SteelOre = "SteelOre1";
    private static readonly EntProtoId Coal = "Coal1";
    private static readonly EntProtoId SheetSteel = "SheetSteel";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsOreSmelter
  id: KsTestOreSmelterDoubling
  components:
  - type: KsStackProcessor
    multiplier: 2
";

    private EntityUid _grid;

    private async Task<EntityUid> SetUpSmelterAsync(EntProtoId prototype, bool powered = true)
    {
        var testMap = await Pair.CreateTestMap();
        _grid = testMap.Grid;

        EntityUid smelter = default;
        await Server.WaitPost(() =>
        {
            smelter = SEntMan.SpawnEntity(prototype, new EntityCoordinates(_grid, new Vector2(0.5f, 0.5f)));
            if (powered)
                SEntMan.System<SharedPowerReceiverSystem>().SetNeedsPower(smelter, false);
        });

        await Pair.RunTicksSync(3);
        return smelter;
    }

    private async Task<EntityUid> SpawnStackAsync(EntProtoId prototype, int count)
    {
        EntityUid stack = default;
        await Server.WaitPost(() =>
        {
            stack = SEntMan.SpawnEntity(prototype, new EntityCoordinates(_grid, new Vector2(4.5f, 4.5f)));
            SEntMan.System<SharedStackSystem>().SetCount((stack, null), count);
        });

        return stack;
    }

    private async Task<bool> TryProcessAsync(EntityUid smelter, EntityUid stack)
    {
        var accepted = false;
        await Server.WaitPost(() => accepted = SEntMan.System<KsEntityProcessorSystem>().TryStartProcessing(smelter, stack));
        return accepted;
    }

    private async Task RunSecondsAsync(float seconds)
    {
        var tickRate = (float)Server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync((int)Math.Ceiling(seconds * tickRate));
    }

    /// <returns>Everything in the world spawned from <paramref name="prototype"/>, added together by stack count.</returns>
    private int Total(EntProtoId prototype)
    {
        return SEntMan.AllEntities<StackComponent>()
            .Where(stack => SEntMan.GetComponent<MetaDataComponent>(stack).EntityPrototype?.ID == prototype.Id)
            .Sum(stack => stack.Comp.Count);
    }

    [Test]
    public async Task OreIsHeldWhileProcessingThenBecomesTheMatchingSheet()
    {
        var smelter = await SetUpSmelterAsync(Smelter);
        var ore = await SpawnStackAsync(SteelOre, 10);

        Assert.That(await TryProcessAsync(smelter, ore), Is.True, "powered smelter takes steel ore");

        // Processing time is 3 seconds. Half way through: ore is inside, nothing out yet.
        await RunSecondsAsync(1.5f);
        await Server.WaitAssertion(() =>
        {
            var container = SEntMan.System<SharedContainerSystem>().GetContainer(smelter, "object-processor-container");
            Assert.That(container.ContainedEntities, Does.Contain(ore));
            Assert.That(Total(SheetSteel), Is.Zero, "too early for any sheets");
            Assert.That(SEntMan.System<SharedAppearanceSystem>().TryGetData<bool>(smelter, KsStackProcessorVisuals.Active, out var active) && active,
                Is.True, "the smelter shows it is working");
        });

        await RunSecondsAsync(2.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Total(SheetSteel), Is.EqualTo(10), "one sheet per ore");
            Assert.That(SEntMan.Deleted(ore), Is.True, "the ore is used up");
            Assert.That(SEntMan.HasComponent<KsActiveEntityProcessorComponent>(smelter), Is.False, "nothing left to process");
            Assert.That(SEntMan.System<SharedAppearanceSystem>().TryGetData<bool>(smelter, KsStackProcessorVisuals.Active, out var active) && active,
                Is.False, "and it shows it has stopped");
        });
    }

    [TestCase("GoldOre1", "IngotGold")]
    [TestCase("SpaceQuartz1", "SheetGlass")]
    [TestCase("PlasmaOre1", "SheetPlasma")]
    public async Task EachOreBecomesItsOwnMaterial(string oreId, string expectedId)
    {
        var smelter = await SetUpSmelterAsync(Smelter);
        var ore = await SpawnStackAsync(oreId, 4);

        Assert.That(await TryProcessAsync(smelter, ore), Is.True);
        await RunSecondsAsync(4f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Total(expectedId), Is.EqualTo(4));
            Assert.That(Total(SheetSteel), Is.Zero, "and not some other conversion");
        });
    }

    [Test]
    public async Task AnUnpoweredSmelterTurnsOreAway()
    {
        var smelter = await SetUpSmelterAsync(Smelter, powered: false);
        var ore = await SpawnStackAsync(SteelOre, 5);

        Assert.That(await TryProcessAsync(smelter, ore), Is.False);
        await RunSecondsAsync(4f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(ore), Is.False, "the ore is left alone");
            Assert.That(Total(SheetSteel), Is.Zero);
        });

        // Power it, and the very same ore is accepted: it was the power that was missing.
        await Server.WaitPost(() => SEntMan.System<SharedPowerReceiverSystem>().SetNeedsPower(smelter, false));
        await Pair.RunTicksSync(3);
        Assert.That(await TryProcessAsync(smelter, ore), Is.True);
    }

    [Test]
    public async Task OreItDoesNotKnowHowToSmeltIsTurnedAway()
    {
        var smelter = await SetUpSmelterAsync(Smelter);
        var coal = await SpawnStackAsync(Coal, 5);
        var steel = await SpawnStackAsync(SteelOre, 5);

        Assert.That(await TryProcessAsync(smelter, coal), Is.False);
        await RunSecondsAsync(1f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(coal), Is.False);
            Assert.That(SEntMan.System<SharedContainerSystem>().IsEntityInContainer(coal), Is.False, "not swallowed either");
        });

        // The control: the same smelter, at the same moment, does take ore it knows.
        Assert.That(await TryProcessAsync(smelter, steel), Is.True);
    }

    [TestCase(20, 40)]
    [TestCase(30, 60)] // a whole number of 30-ore stacks
    [TestCase(7, 14)]
    public async Task TheMultiplierScalesTheOutputWhateverTheStackSizes(int oreCount, int expectedSheets)
    {
        var smelter = await SetUpSmelterAsync(_doublingSmelter);
        var ore = await SpawnStackAsync(SteelOre, oreCount);

        Assert.That(await TryProcessAsync(smelter, ore), Is.True);
        await RunSecondsAsync(4f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Total(SheetSteel), Is.EqualTo(expectedSheets));

            // No empty husks left lying around by the splitting.
            Assert.That(SEntMan.AllEntities<StackComponent>().Any(stack => stack.Comp.Count <= 0), Is.False);
        });
    }

    [Test]
    public async Task OrePulledOutBeforeItIsDoneIsNotSmeltedAnyway()
    {
        var smelter = await SetUpSmelterAsync(Smelter);
        var ore = await SpawnStackAsync(SteelOre, 6);
        Assert.That(await TryProcessAsync(smelter, ore), Is.True);

        await RunSecondsAsync(1f);
        await Server.WaitPost(() =>
        {
            var containerSystem = SEntMan.System<SharedContainerSystem>();
            var container = containerSystem.GetContainer(smelter, "object-processor-container");
            // Somewhere away from the intake, or the machine simply swallows it again.
            containerSystem.Remove(ore, container, destination: new EntityCoordinates(_grid, new Vector2(6.5f, 6.5f)));
        });

        await RunSecondsAsync(4f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(ore), Is.False, "the ore was taken back, so it must not be consumed");
            Assert.That(Total(SheetSteel), Is.Zero, "and nothing may be produced from ore that is no longer inside");
            Assert.That(SEntMan.HasComponent<KsActiveEntityProcessorComponent>(smelter), Is.False, "nothing is being processed any more");
            Assert.That(SEntMan.System<SharedAppearanceSystem>().TryGetData<bool>(smelter, KsStackProcessorVisuals.Active, out var active) && active,
                Is.False, "so it must not look busy forever");
        });
    }

    [Test]
    public async Task TakingOneStackOutLeavesTheOthersProcessing()
    {
        var smelter = await SetUpSmelterAsync(Smelter);
        var kept = await SpawnStackAsync(SteelOre, 3);
        var pulled = await SpawnStackAsync(SteelOre, 5);
        Assert.That(await TryProcessAsync(smelter, kept), Is.True);
        Assert.That(await TryProcessAsync(smelter, pulled), Is.True);

        await RunSecondsAsync(1f);
        await Server.WaitPost(() =>
        {
            var containerSystem = SEntMan.System<SharedContainerSystem>();
            containerSystem.Remove(pulled, containerSystem.GetContainer(smelter, "object-processor-container"),
                destination: new EntityCoordinates(_grid, new Vector2(6.5f, 6.5f)));
        });
        await RunSecondsAsync(4f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Total(SheetSteel), Is.EqualTo(3), "only the stack that stayed inside is smelted");
            Assert.That(SEntMan.Deleted(pulled), Is.False);
            Assert.That(SEntMan.HasComponent<KsActiveEntityProcessorComponent>(smelter), Is.False);
        });
    }

    [Test]
    public async Task AStackInsertedTheSameTickTheLastOneLeftIsStillProcessed()
    {
        var smelter = await SetUpSmelterAsync(Smelter);
        var first = await SpawnStackAsync(SteelOre, 3);
        var second = await SpawnStackAsync(SteelOre, 4);
        Assert.That(await TryProcessAsync(smelter, first), Is.True);
        await RunSecondsAsync(1f);

        var accepted = false;
        await Server.WaitPost(() =>
        {
            var containerSystem = SEntMan.System<SharedContainerSystem>();
            containerSystem.Remove(first, containerSystem.GetContainer(smelter, "object-processor-container"),
                destination: new EntityCoordinates(_grid, new Vector2(6.5f, 6.5f)));
            accepted = SEntMan.System<KsEntityProcessorSystem>().TryStartProcessing(smelter, second);
        });
        Assert.That(accepted, Is.True);

        await RunSecondsAsync(4f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Total(SheetSteel), Is.EqualTo(4), "the newcomer is smelted");
            Assert.That(SEntMan.Deleted(first), Is.False);
            Assert.That(SEntMan.HasComponent<KsActiveEntityProcessorComponent>(smelter), Is.False, "and the machine settles afterwards");
            Assert.That(SEntMan.System<SharedAppearanceSystem>().TryGetData<bool>(smelter, KsStackProcessorVisuals.Active, out var active) && active,
                Is.False);
        });
    }

    [Test]
    public async Task SeveralStacksAreProcessedAtOnceAndOutOnTheirOwnSchedule()
    {
        var smelter = await SetUpSmelterAsync(Smelter);
        var first = await SpawnStackAsync(SteelOre, 3);
        Assert.That(await TryProcessAsync(smelter, first), Is.True);

        await RunSecondsAsync(2f);
        var second = await SpawnStackAsync(SteelOre, 5);
        Assert.That(await TryProcessAsync(smelter, second), Is.True);

        // 3.5s in: the first (started at 0) is done, the second (started at 2) is not.
        await RunSecondsAsync(1.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Total(SheetSteel), Is.EqualTo(3));
            Assert.That(SEntMan.HasComponent<KsActiveEntityProcessorComponent>(smelter), Is.True, "still busy with the second");
        });

        await RunSecondsAsync(2f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Total(SheetSteel), Is.EqualTo(8));
            Assert.That(SEntMan.HasComponent<KsActiveEntityProcessorComponent>(smelter), Is.False);
        });
    }
}
