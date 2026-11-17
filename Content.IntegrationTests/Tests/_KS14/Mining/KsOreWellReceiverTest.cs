#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.OreWell;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.OreWell;
using Content.Shared.Interaction;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Mining;

/// <summary>
///     The ore retrieval matrix: it takes the ore an ore well produces, but only while it is both powered and
///         switched on, and it keeps what it could not hand over yet as debt.
/// </summary>
/// <remarks>
///     The cycle length is forced to <see cref="TestIntervalSeconds"/>, and every amount and wait below is worked out
///         from it, so retuning the live cycle changes nothing here. The first cycle may still come at any point,
///         which is why the tests wait for a delivery to appear rather than counting ticks.
/// </remarks>
[TestFixture]
[TestOf(typeof(OreWellReceiverSystem))]
[EnsureCVar(Side.Server, typeof(KsCCVars), nameof(KsCCVars.OreWellReceiverInterval), TestIntervalSeconds)]
public sealed class KsOreWellReceiverTest : GameTest
{
    // Server only: the receiver is a server system.
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    ///     The delivery cycle for every test here, in seconds.
    /// </summary>
    private const float TestIntervalSeconds = 10f;

    // Ore per second of the test wells below, which must match their YAML.
    private const float HalfRate = 0.5f;
    private const float QuarterRate = 0.25f;
    private const float OverAStackRate = 4.5f;

    private static readonly EntProtoId ReceiverEnabled = "OreRetrievalMatrixEnabled";
    private static readonly EntProtoId ReceiverSwitchedOff = "OreRetrievalMatrix";
    private static readonly ProtoId<StackPrototype> SteelOre = "SteelOre";

    // One ore type with a fixed rate per second, so what a cycle delivers is a number that can be worked out by hand.
    private readonly ProtoId<OreWellSettingPrototype> _halfPerSecond = "KsTestOreWellHalf";
    private readonly ProtoId<OreWellSettingPrototype> _quarterPerSecond = "KsTestOreWellQuarter";
    private readonly ProtoId<OreWellSettingPrototype> _overAStackPerCycle = "KsTestOreWellOverAStack";

    [TestPrototypes]
    private const string Prototypes = @"
- type: oreWellSetting
  id: KsTestOreWellHalf
  totalResourceRateRange: 0.5,0.5
  resourceCountRange: 1,1
  possibleResourceTypes:
  - SteelOre

- type: oreWellSetting
  id: KsTestOreWellQuarter
  totalResourceRateRange: 0.25,0.25
  resourceCountRange: 1,1
  possibleResourceTypes:
  - SteelOre

# 45 a cycle at the test interval: more than a stack of steel ore (30), less than two.
- type: oreWellSetting
  id: KsTestOreWellOverAStack
  totalResourceRateRange: 4.5,4.5
  resourceCountRange: 1,1
  possibleResourceTypes:
  - SteelOre
";

    /// <returns>What a well with this rate makes over one cycle.</returns>
    private static float PerCycle(float rate) => rate * TestIntervalSeconds;

    private async Task<EntityUid> SpawnWellAsync(EntityUid grid, ProtoId<OreWellSettingPrototype> setting)
    {
        EntityUid well = default;
        await Server.WaitPost(() =>
        {
            well = SEntMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(8.5f, 8.5f)));
            SEntMan.System<OreWellSystem>().GenerateOreWellWithSettings(well, setting);
        });

        return well;
    }

    private async Task<EntityUid> SpawnReceiverAsync(EntityUid grid, EntProtoId prototype, float x, bool powered)
    {
        EntityUid receiver = default;
        HashSet<EntityUid> stacksBefore = [];
        await Server.WaitPost(() =>
        {
            stacksBefore = SEntMan.AllEntities<StackComponent>().Select(stack => stack.Owner).ToHashSet();
            receiver = SEntMan.SpawnEntity(prototype, new EntityCoordinates(grid, new Vector2(x, 0.5f)));

            // Actually unpowered: the machine needs power and there is none. Powered: it does not need any.
            var power = SEntMan.System<SharedPowerReceiverSystem>();
            if (powered)
                power.SetNeedsPower(receiver, false);
        });

        await Pair.RunTicksSync(3);

        // The cycle can come round in the ticks above, and then the delivery a test waits for is the second one, on
        //      top of this one and its debt. Undo it, so the receivers start empty and owe nothing whenever it fell.
        await Server.WaitPost(() =>
        {
            foreach (var stack in SEntMan.AllEntities<StackComponent>().ToList())
            {
                if (stack.Comp.StackTypeId == SteelOre && !stacksBefore.Contains(stack.Owner))
                    SEntMan.DeleteEntity(stack.Owner);
            }

            foreach (var receiverEntity in SEntMan.AllEntities<OreWellReceiverComponent>())
                receiverEntity.Comp.Debt.Clear();
        });

        return receiver;
    }

    /// <returns>Steel ore delivered to exactly where this receiver stands.</returns>
    private int OreAt(EntityUid receiver)
    {
        var transform = SEntMan.System<SharedTransformSystem>();
        var position = transform.GetWorldPosition(receiver);

        var total = 0;
        foreach (var stack in SEntMan.AllEntities<StackComponent>())
        {
            if (stack.Comp.StackTypeId != SteelOre ||
                (transform.GetWorldPosition(stack.Owner) - position).LengthSquared() > 0.0001f)
                continue;

            total += stack.Comp.Count;
        }

        return total;
    }

    private int OreEverywhere()
    {
        return SEntMan.AllEntities<StackComponent>()
            .Where(stack => stack.Comp.StackTypeId == SteelOre)
            .Sum(stack => stack.Comp.Count);
    }

    /// <summary>
    ///     Runs until the world holds more steel ore than it did, i.e. until a delivery cycle has happened.
    /// </summary>
    /// <returns>False if no delivery came inside two cycles.</returns>
    private async Task<bool> RunUntilADeliveryAsync()
    {
        var tickRate = (int)Server.ResolveDependency<IGameTiming>().TickRate;

        var before = 0;
        await Server.WaitPost(() => before = OreEverywhere());

        // Two whole cycles, whatever point of the cycle the pair is at, and some to spare.
        var seconds = (int)MathF.Ceiling(TestIntervalSeconds * 2f) + 10;
        for (var second = 0; second < seconds; second++)
        {
            await Pair.RunTicksSync(tickRate);

            var now = 0;
            await Server.WaitPost(() => now = OreEverywhere());
            if (now != before)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Runs a little longer than two cycles, for a case where nothing is supposed to arrive.
    /// </summary>
    private async Task RunTwoCyclesAsync()
    {
        var tickRate = (int)Server.ResolveDependency<IGameTiming>().TickRate;
        await Pair.RunTicksSync(tickRate * ((int)MathF.Ceiling(TestIntervalSeconds * 2f) + 5));
    }

    private OreWellReceiverComponent Receiver(EntityUid uid) => SEntMan.GetComponent<OreWellReceiverComponent>(uid);

    [Test]
    public async Task APoweredEnabledReceiverGetsTheWellsRateOverACycle()
    {
        var testMap = await Pair.CreateTestMap();
        await SpawnWellAsync(testMap.Grid, _halfPerSecond);
        var receiver = await SpawnReceiverAsync(testMap.Grid, ReceiverEnabled, 0.5f, powered: true);

        Assert.That(await RunUntilADeliveryAsync(), Is.True, "a powered, enabled receiver must receive ore");

        await Server.WaitAssertion(() =>
        {
            // A whole cycle's worth, all of it to the only receiver.
            Assert.That(OreAt(receiver), Is.EqualTo((int)PerCycle(HalfRate)));
            Assert.That(Receiver(receiver).Debt, Is.Empty);
        });
    }

    [Test]
    public async Task AnUnpoweredReceiverGetsNothingWhileAPoweredOneDoes()
    {
        var testMap = await Pair.CreateTestMap();
        await SpawnWellAsync(testMap.Grid, _halfPerSecond);
        var unpowered = await SpawnReceiverAsync(testMap.Grid, ReceiverEnabled, 0.5f, powered: false);
        var powered = await SpawnReceiverAsync(testMap.Grid, ReceiverEnabled, 4.5f, powered: true);

        Assert.That(await RunUntilADeliveryAsync(), Is.True);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Receiver(unpowered).Powered, Is.False, "precondition: it really has no power");
            Assert.That(OreAt(unpowered), Is.Zero, "no power, no ore");

            // The control: the same cycle delivered to the one that has power, and the dead one took no share of it.
            Assert.That(OreAt(powered), Is.EqualTo((int)PerCycle(HalfRate)));
        });

        // Once power is back it joins in, and the well's output is split between the two.
        await Server.WaitPost(() => SEntMan.System<SharedPowerReceiverSystem>().SetNeedsPower(unpowered, false));
        await Pair.RunTicksSync(3);

        var before = 0;
        await Server.WaitPost(() => before = OreAt(powered));
        Assert.That(await RunUntilADeliveryAsync(), Is.True);

        var share = PerCycle(HalfRate) / 2f;
        Assert.That(share % 1f, Is.Not.Zero, "precondition: a share has a fraction, so something is owed");

        await Server.WaitAssertion(() =>
        {
            Assert.That(OreAt(unpowered), Is.EqualTo((int)share), "half a cycle's worth, rounded down");
            Assert.That(OreAt(powered) - before, Is.EqualTo((int)share));
            Assert.That(Receiver(unpowered).Debt[SteelOre], Is.EqualTo(share % 1f).Within(0.0001f), "and the fraction each is owed");
        });
    }

    [Test]
    public async Task ASwitchedOffReceiverGetsNothingUntilItIsActivated()
    {
        var testMap = await Pair.CreateTestMap();
        await SpawnWellAsync(testMap.Grid, _halfPerSecond);
        var receiver = await SpawnReceiverAsync(testMap.Grid, ReceiverSwitchedOff, 0.5f, powered: true);

        await RunTwoCyclesAsync();

        await Server.WaitAssertion(() =>
        {
            Assert.That(Receiver(receiver).Powered, Is.True, "precondition: powered");
            Assert.That(Receiver(receiver).Enabled, Is.False, "precondition: switched off");
            Assert.That(OreEverywhere(), Is.Zero, "powered but switched off, so nothing");
        });

        // Press the button.
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(receiver, new ActivateInWorldEvent(receiver, receiver, complex: true)));
        Assert.That(await RunUntilADeliveryAsync(), Is.True, "switching it on must start the deliveries");
        await Server.WaitAssertion(() => Assert.That(OreAt(receiver), Is.EqualTo((int)PerCycle(HalfRate))));

        // And again to turn it off.
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(receiver, new ActivateInWorldEvent(receiver, receiver, complex: true)));
        var delivered = 0;
        await Server.WaitPost(() => delivered = OreEverywhere());
        await RunTwoCyclesAsync();
        await Server.WaitAssertion(() => Assert.That(OreEverywhere(), Is.EqualTo(delivered), "switched off again, so no more ore"));
    }

    [Test]
    public async Task LosingPowerStopsTheDeliveries()
    {
        var testMap = await Pair.CreateTestMap();
        await SpawnWellAsync(testMap.Grid, _halfPerSecond);
        var receiver = await SpawnReceiverAsync(testMap.Grid, ReceiverEnabled, 0.5f, powered: true);

        Assert.That(await RunUntilADeliveryAsync(), Is.True, "precondition: it delivers while powered");

        await Server.WaitPost(() => SEntMan.System<SharedPowerReceiverSystem>().SetPowerDisabled(receiver, true));
        await Pair.RunTicksSync(3);

        var delivered = 0;
        await Server.WaitPost(() => delivered = OreEverywhere());
        await RunTwoCyclesAsync();

        await Server.WaitAssertion(() =>
        {
            Assert.That(Receiver(receiver).Powered, Is.False, "precondition: the power is really gone");
            Assert.That(OreEverywhere(), Is.EqualTo(delivered));
        });
    }

    [Test]
    public async Task WhatCouldNotBeSpawnedWholeIsOwedAndPaidNextCycle()
    {
        var testMap = await Pair.CreateTestMap();
        await SpawnWellAsync(testMap.Grid, _quarterPerSecond);
        var receiver = await SpawnReceiverAsync(testMap.Grid, ReceiverEnabled, 0.5f, powered: true);

        // A cycle's worth with half an ore left over: the whole ore now, the half owed.
        var perCycle = PerCycle(QuarterRate);
        Assert.That(perCycle % 1f, Is.EqualTo(0.5f).Within(0.0001f), "precondition: a cycle leaves half an ore over");

        Assert.That(await RunUntilADeliveryAsync(), Is.True);
        await Server.WaitAssertion(() =>
        {
            Assert.That(OreAt(receiver), Is.EqualTo((int)perCycle));
            Assert.That(Receiver(receiver).Debt[SteelOre], Is.EqualTo(0.5f).Within(0.0001f));
        });

        // Next cycle: another cycle's worth plus the half owed comes out whole, nothing left over. Two cycles' worth
        //      over two cycles is the real rate.
        Assert.That(await RunUntilADeliveryAsync(), Is.True);
        await Server.WaitAssertion(() =>
        {
            Assert.That(OreAt(receiver), Is.EqualTo((int)(perCycle * 2f)));
            Assert.That(Receiver(receiver).Debt, Is.Empty, "a debt that is paid off is removed");
        });
    }

    [Test]
    public async Task AFullStackIsNotExceededAndTheRestIsOwed()
    {
        var testMap = await Pair.CreateTestMap();
        await SpawnWellAsync(testMap.Grid, _overAStackPerCycle);
        var receiver = await SpawnReceiverAsync(testMap.Grid, ReceiverEnabled, 0.5f, powered: true);

        // A cycle makes more than a stack of steel ore holds, but less than two.
        var perCycle = PerCycle(OverAStackRate);
        var maxCount = SProtoMan.Index(SteelOre).MaxCount ?? int.MaxValue;
        Assert.That(perCycle, Is.GreaterThan(maxCount).And.LessThan(maxCount * 2), "precondition: between one and two stacks a cycle");

        Assert.That(await RunUntilADeliveryAsync(), Is.True);

        await Server.WaitAssertion(() =>
        {
            var stackCounts = new List<int>();
            var transform = SEntMan.System<SharedTransformSystem>();
            foreach (var stack in SEntMan.AllEntities<StackComponent>())
            {
                if (stack.Comp.StackTypeId == SteelOre &&
                    (transform.GetWorldPosition(stack.Owner) - transform.GetWorldPosition(receiver)).LengthSquared() < 0.0001f)
                    stackCounts.Add(stack.Comp.Count);
            }

            Assert.That(stackCounts, Is.EqualTo(new[] { maxCount }), "one full stack, no oversized one");
            Assert.That(Receiver(receiver).Debt[SteelOre], Is.EqualTo(perCycle - maxCount).Within(0.0001f), "the rest is owed, not lost");
        });
    }
}
