#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Chain;
using Content.Shared._KS14.LinkedChain;
using Content.Shared.Interaction;
using Content.Shared.Trigger.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Chain;

/// <summary>
///     The wire gadget built on chains: a wire start used on a wire end strings a chain between them, and a trigger
///         on the start only reaches the end while that chain is whole.
/// </summary>
[TestFixture]
[TestOf(typeof(LinkedChainSystem))]
public sealed class KsLinkedChainTest : GameTest
{
    // Server only. On a connected pair the client logs "joint already existed" errors when a chain is strung between
    //      two pre-existing entities (see the report on #237), and every error log fails the test.
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId WireStart = "KsWireStart";
    private static readonly EntProtoId WireEnd = "KsWireEnd";
    private static readonly EntProtoId WireSegment = "KsWireSegment";

    // KsWireStart's prototype asks for this many segments.
    private const int PrototypeSegments = 11;

    private ChainSystem Chains => SEntMan.System<ChainSystem>();
    private KsChainProbeSystem Probe => SEntMan.System<KsChainProbeSystem>();

    private async Task<(EntityUid Start, EntityUid End, EntityUid User, EntityCoordinates EndCoordinates)> SetUpAsync(EntityUid grid)
    {
        EntityUid start = default, end = default, user = default;
        var endCoordinates = new EntityCoordinates(grid, new Vector2(3.5f, 0.5f));

        await Server.WaitPost(() =>
        {
            start = SEntMan.SpawnEntity(WireStart, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            end = SEntMan.SpawnEntity(WireEnd, endCoordinates);
            user = SEntMan.SpawnEntity(null, new EntityCoordinates(grid, new Vector2(2.5f, 0.5f)));
        });

        await Pair.RunTicksSync(3);
        return (start, end, user, endCoordinates);
    }

    private async Task LinkAsync(EntityUid start, EntityUid end, EntityUid user, EntityCoordinates coordinates)
    {
        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(end, new InteractUsingEvent(user, start, end, coordinates));
        });

        await Pair.RunTicksSync(3);
    }

    /// <returns>Every entity from <paramref name="start"/> along <see cref="ChainLinkComponent.NextLinkUid"/>.</returns>
    private List<EntityUid> WalkChain(EntityUid start)
    {
        var walked = new List<EntityUid>();
        EntityUid? current = start;
        while (current is { } uid && SEntMan.TryGetComponent<ChainLinkComponent>(uid, out var link) && walked.Count < 100)
        {
            walked.Add(uid);
            current = link.NextLinkUid;
        }

        return walked;
    }

    private int CountSegments()
    {
        return SEntMan.AllEntities<ChainLinkComponent>()
            .Count(entity => SEntMan.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == WireSegment.Id);
    }

    [Test]
    public async Task UsingAStartOnAnEndStringsAWholeChainBetweenThem()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<LinkedChainStartComponent>(start).EndUid, Is.Null, "nothing is linked before the interaction");
            Assert.That(SEntMan.HasComponent<ChainLinkComponent>(start), Is.False);
        });

        await LinkAsync(start, end, user, coordinates);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<LinkedChainStartComponent>(start).EndUid, Is.EqualTo(end));
            Assert.That(SEntMan.GetComponent<LinkedChainEndComponent>(end).StartUid, Is.EqualTo(start));

            var walked = WalkChain(start);
            Assert.That(walked, Has.Count.EqualTo(PrototypeSegments + 2), "start, every segment, then end");
            Assert.That(walked[0], Is.EqualTo(start));
            Assert.That(walked[^1], Is.EqualTo(end));
            Assert.That(CountSegments(), Is.EqualTo(PrototypeSegments));

            Assert.That(SEntMan.HasComponent<ChainEdgeComponent>(start), Is.True);
            Assert.That(SEntMan.HasComponent<ChainEdgeComponent>(end), Is.True);
            Assert.That(Chains.IsChainFullyIntact(start), Is.True);
            Assert.That(Chains.IsChainFullyIntact(end), Is.True);
        });
    }

    [Test]
    public async Task AnEndThatIsAlreadyLinkedIgnoresAnotherStart()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);
        await LinkAsync(start, end, user, coordinates);

        EntityUid secondStart = default;
        await Server.WaitPost(() => secondStart = SEntMan.SpawnEntity(WireStart, new EntityCoordinates(testMap.Grid, new Vector2(1.5f, 2.5f))));
        await LinkAsync(secondStart, end, user, coordinates);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<LinkedChainEndComponent>(end).StartUid, Is.EqualTo(start), "the end stays with its first start");
            Assert.That(SEntMan.GetComponent<LinkedChainStartComponent>(secondStart).EndUid, Is.Null);
            Assert.That(SEntMan.HasComponent<ChainLinkComponent>(secondStart), Is.False);
            Assert.That(CountSegments(), Is.EqualTo(PrototypeSegments), "and no second chain was strung");
        });
    }

    [Test]
    public async Task ATriggerOnTheStartReachesTheEndWhileTheChainIsIntact()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);
        await LinkAsync(start, end, user, coordinates);

        await Server.WaitPost(() =>
        {
            Probe.Clear();
            SEntMan.System<TriggerSystem>().Trigger(start, user, "trigger");
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Probe.EndTriggers, Is.EqualTo(new[] { (end, (string?)"trigger") }));
        });
    }

    [Test]
    public async Task OnlyTheStartsKeysAreCarriedToTheEnd()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);
        await LinkAsync(start, end, user, coordinates);

        await Server.WaitPost(() =>
        {
            Probe.Clear();
            SEntMan.System<TriggerSystem>().Trigger(start, user, "somethingelse");
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Probe.EndTriggers, Is.Empty, "a key outside KeysIn must stay on the start");
        });

        // Control: the same setup does carry the key it is listening for, so the above is the key's doing.
        await Server.WaitPost(() => SEntMan.System<TriggerSystem>().Trigger(start, user, "trigger"));
        await Pair.RunTicksSync(2);
        await Server.WaitAssertion(() => Assert.That(Probe.EndTriggers, Has.Count.EqualTo(1)));
    }

    [Test]
    public async Task ANullKeyTriggersTheEndToo()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);
        await LinkAsync(start, end, user, coordinates);

        await Server.WaitPost(() =>
        {
            Probe.Clear();
            SEntMan.System<TriggerSystem>().Trigger(start, user, key: null);
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() => Assert.That(Probe.EndTriggers.Select(trigger => trigger.End), Is.EqualTo(new[] { end })));
    }

    [Test]
    public async Task ATriggerDoesNotReachTheEndOnceTheChainIsBroken()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);
        await LinkAsync(start, end, user, coordinates);

        // Cut it in the middle of the wire.
        await Server.WaitPost(() =>
        {
            var middle = WalkChain(start)[PrototypeSegments / 2 + 1];
            SEntMan.DeleteEntity(middle);
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            // Both ends forget each other: nothing is left to talk to.
            Assert.That(SEntMan.GetComponent<LinkedChainStartComponent>(start).EndUid, Is.Null);
            Assert.That(SEntMan.GetComponent<LinkedChainEndComponent>(end).StartUid, Is.Null);
            Probe.Clear();
        });

        await Server.WaitPost(() => SEntMan.System<TriggerSystem>().Trigger(start, user, "trigger"));
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() => Assert.That(Probe.EndTriggers, Is.Empty));
    }

    /// <summary>
    ///     The start forgets its end the moment the chain breaks, so the check in the trigger itself is a second line
    ///         of defence. Pinned here by making the start believe it is still linked.
    /// </summary>
    [Test]
    public async Task AStartThatStillBelievesItIsLinkedCannotTriggerThroughABrokenChain()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);
        await LinkAsync(start, end, user, coordinates);

        await Server.WaitPost(() => SEntMan.DeleteEntity(WalkChain(start)[PrototypeSegments / 2 + 1]));
        await Pair.RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            Probe.Clear();
            SEntMan.GetComponent<LinkedChainStartComponent>(start).EndUid = end;
            SEntMan.System<TriggerSystem>().Trigger(start, user, "trigger");
        });
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() => Assert.That(Probe.EndTriggers, Is.Empty));
    }

    /// <summary>
    ///     What the "separate wire" alt verb does.
    /// </summary>
    [Test]
    public async Task BreakingTheChainFromTheStartSeparatesBothEnds()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);
        await LinkAsync(start, end, user, coordinates);

        var broke = false;
        await Server.WaitPost(() =>
        {
            Probe.Clear();
            broke = Chains.TryBreakChainFrom(start, removeJoints: true);
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(broke, Is.True);
            Assert.That(SEntMan.GetComponent<LinkedChainStartComponent>(start).EndUid, Is.Null);
            Assert.That(SEntMan.GetComponent<LinkedChainEndComponent>(end).StartUid, Is.Null);
            Assert.That(Chains.HasAdjacentLink(start), Is.False, "the start must not be left holding a wire");
            Assert.That(SEntMan.HasComponent<ChainLinkComponent>(start), Is.False, "the start is out of the chain");
            Assert.That(WalkChain(start), Is.Empty);
            Assert.That(Chains.IsChainFullyIntact(end), Is.False);
        });
    }

    [Test]
    public async Task DeletingTheStartFreesTheEndAndViceVersa()
    {
        var testMap = await Pair.CreateTestMap();
        var (start, end, user, coordinates) = await SetUpAsync(testMap.Grid);
        await LinkAsync(start, end, user, coordinates);

        await Server.WaitPost(() => SEntMan.DeleteEntity(start));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<LinkedChainEndComponent>(end).StartUid, Is.Null, "the end must not point at a deleted start");
        });

        // And the other way around, with a fresh pair.
        EntityUid secondStart = default, secondEnd = default;
        await Server.WaitPost(() =>
        {
            secondStart = SEntMan.SpawnEntity(WireStart, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 3.5f)));
            secondEnd = SEntMan.SpawnEntity(WireEnd, new EntityCoordinates(testMap.Grid, new Vector2(3.5f, 3.5f)));
        });
        await Pair.RunTicksSync(3);
        await LinkAsync(secondStart, secondEnd, user, new EntityCoordinates(testMap.Grid, new Vector2(3.5f, 3.5f)));

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<LinkedChainStartComponent>(secondStart).EndUid, Is.EqualTo(secondEnd), "precondition: linked"));

        await Server.WaitPost(() => SEntMan.DeleteEntity(secondEnd));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<LinkedChainStartComponent>(secondStart).EndUid, Is.Null, "the start must not point at a deleted end");
        });
    }
}
