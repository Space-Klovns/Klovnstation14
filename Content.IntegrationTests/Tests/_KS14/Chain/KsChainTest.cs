#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Chain;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Chain;

/// <summary>
///     The physics chain: spawning one, what "intact" means, and what breaking it does to the links, the edges
///         and the events raised.
/// </summary>
/// <remarks>
///     Chains are server-only (<see cref="ChainSystem.SpawnChain"/> returns nothing on the client).
///         Every break below is made on the server, and the assertions read the server's links.
/// </remarks>
[TestFixture]
[TestOf(typeof(ChainSystem))]
public sealed class KsChainTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId WireSegment = "KsWireSegment";

    private const float SegmentOffset = 0.125f;
    private const int ChainLength = 5;

    private ChainSystem Chains => SEntMan.System<ChainSystem>();
    private KsChainProbeSystem Probe => SEntMan.System<KsChainProbeSystem>();

    /// <summary>
    ///     Spawns a chain of <paramref name="length"/> wire segments at <paramref name="position"/> on the test grid.
    /// </summary>
    private async Task<List<EntityUid>> SpawnChainAsync(EntityUid grid, int length = ChainLength, Vector2? position = null)
    {
        var links = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            // The probe outlives the test on a pooled pair, so start from nothing.
            Probe.Clear();

            var coordinates = new EntityCoordinates(grid, position ?? new Vector2(0.5f, 0.5f));
            links = Chains.SpawnChain(WireSegment, coordinates, length, SegmentOffset, out _, out _);
        });

        await Pair.RunTicksSync(3);
        return links;
    }

    private bool HasJoint(EntityUid uid, string? jointId)
    {
        return jointId != null &&
            SEntMan.TryGetComponent<JointComponent>(uid, out var jointComponent) &&
            jointComponent.GetJoints.ContainsKey(jointId);
    }

    [Test]
    public async Task SpawnChainBuildsALinkedListWithEdgesAtBothEnds()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid);

        await Server.WaitAssertion(() =>
        {
            Assert.That(links, Has.Count.EqualTo(ChainLength));

            var start = links[0];
            var end = links[^1];

            for (var i = 0; i < links.Count; i++)
            {
                var link = SEntMan.GetComponent<ChainLinkComponent>(links[i]);

                Assert.That(link.PreviousLinkUid, Is.EqualTo(i == 0 ? null : (EntityUid?)links[i - 1]), $"link {i} previous");
                Assert.That(link.NextLinkUid, Is.EqualTo(i == links.Count - 1 ? null : (EntityUid?)links[i + 1]), $"link {i} next");
                Assert.That(link.PreviousLinkJointId == null, Is.EqualTo(i == 0), $"link {i} previous joint id");
                Assert.That(link.NextLinkJointId == null, Is.EqualTo(i == links.Count - 1), $"link {i} next joint id");

                // A joint id on the component is only worth anything if the joint is really on the body.
                Assert.That(HasJoint(links[i], link.PreviousLinkJointId), Is.EqualTo(i != 0), $"link {i} has previous joint");
                Assert.That(HasJoint(links[i], link.NextLinkJointId), Is.EqualTo(i != links.Count - 1), $"link {i} has next joint");

                Assert.That(link.EdgeUids, Is.EquivalentTo(new[] { start, end }), $"link {i} edges");
                Assert.That(Chains.IsChainFullyIntact(links[i]), Is.True, $"link {i} intact");
            }

            // Only the two ends are edges.
            for (var i = 0; i < links.Count; i++)
                Assert.That(SEntMan.HasComponent<ChainEdgeComponent>(links[i]), Is.EqualTo(i == 0 || i == links.Count - 1), $"link {i} edge");

            var startEdge = SEntMan.GetComponent<ChainEdgeComponent>(start);
            var endEdge = SEntMan.GetComponent<ChainEdgeComponent>(end);
            Assert.That(startEdge.OtherEdgeUid, Is.EqualTo(end));
            Assert.That(endEdge.OtherEdgeUid, Is.EqualTo(start));
            Assert.That(startEdge.Broken, Is.False);
            Assert.That(endEdge.Broken, Is.False);
            Assert.That(startEdge.LinkUids, Is.EquivalentTo(links));

            Assert.That(Chains.GetLinksWithoutEdges(start).ToArray(), Is.EquivalentTo(links.Skip(1).Take(ChainLength - 2)));
            Assert.That(Chains.HasAdjacentLink(start), Is.True);

            Assert.That(Probe.Segmented, Is.Empty, "spawning must not look like a break");
            Assert.That(Probe.InitiallyBroken, Is.Empty, "spawning must not look like a break");
        });
    }

    [Test]
    public async Task SpawnChainRejectsAnEmptyChain()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                Chains.SpawnChain(WireSegment, new EntityCoordinates(testMap.Grid, Vector2.One), 0, SegmentOffset, out _, out _));

            // The deliberate check, not an index error from further down.
            Assert.That(exception!.ParamName, Is.EqualTo("length"));
        });
    }

    [Test]
    public async Task DeletingAMiddleLinkSegmentsTheChain()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid);

        await Server.WaitPost(() =>
        {
            Probe.Clear();
            SEntMan.DeleteEntity(links[2]);
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var before = SEntMan.GetComponent<ChainLinkComponent>(links[1]);
            var after = SEntMan.GetComponent<ChainLinkComponent>(links[3]);

            // The neighbours of the hole no longer point into it ...
            Assert.That(before.NextLinkUid, Is.Null);
            Assert.That(before.NextLinkJointId, Is.Null);
            Assert.That(after.PreviousLinkUid, Is.Null);
            Assert.That(after.PreviousLinkJointId, Is.Null);

            // ... and say so.
            var segmentedOn = Probe.Segmented.Select(pair => pair.RaisedOn).ToList();
            Assert.That(segmentedOn, Does.Contain(links[1]).And.Contain(links[3]), "ChainSegmentedEvent goes to the two neighbours");
            Assert.That(segmentedOn, Does.Not.Contain(links[0]).And.Not.Contain(links[4]), "and nobody further away");

            // The chain as a whole is no longer intact, wherever you ask from.
            foreach (var link in new[] { links[0], links[1], links[3], links[4] })
            {
                if (SEntMan.HasComponent<ChainLinkComponent>(link))
                    Assert.That(Chains.IsChainFullyIntact(link), Is.False);
            }

            // The first break raises ChainInitiallyBrokenEvent on each edge exactly once.
            Assert.That(Probe.InitiallyBroken.Count(pair => pair.RaisedOn == links[0]), Is.EqualTo(1));
            Assert.That(Probe.InitiallyBroken.Count(pair => pair.RaisedOn == links[4]), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ABrokenChainIsBrokenForEveryRemainingLinkAndBothEdges()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid, length: 7);

        await Server.WaitPost(() => SEntMan.DeleteEntity(links[3]));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            // Edges that were not adjacent to the break are still alive and flagged.
            Assert.That(SEntMan.GetComponent<ChainEdgeComponent>(links[0]).Broken, Is.True, "start edge");
            Assert.That(SEntMan.GetComponent<ChainEdgeComponent>(links[6]).Broken, Is.True, "end edge");

            // Links that were nowhere near the break.
            Assert.That(Chains.IsChainFullyIntact(links[1]), Is.False);
            Assert.That(Chains.IsChainFullyIntact(links[5]), Is.False);
        });
    }

    [Test]
    public async Task AChainOnlyIsInitiallyBrokenOnce()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid, length: 8);

        await Server.WaitPost(() =>
        {
            Probe.Clear();
            SEntMan.DeleteEntity(links[2]);
        });
        await Pair.RunTicksSync(3);

        var firstBreakCount = 0;
        await Server.WaitPost(() => firstBreakCount = Probe.InitiallyBroken.Count);
        Assert.That(firstBreakCount, Is.GreaterThan(0), "the first break must be reported at all");

        // A second cut elsewhere in the chain is not "initially" broken any more.
        await Server.WaitPost(() => SEntMan.DeleteEntity(links[5]));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Probe.InitiallyBroken, Has.Count.EqualTo(firstBreakCount));
            Assert.That(Probe.Segmented.Select(pair => pair.RaisedOn), Does.Contain(links[4]).And.Contain(links[6]), "but the cut is still reported as a segmentation");
        });
    }

    [Test]
    public async Task TryBreakChainFromDetachesThatLinkAndRemovesItsJoints()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid);

        var broke = false;
        await Server.WaitPost(() =>
        {
            Probe.Clear();
            broke = Chains.TryBreakChainFrom(links[2], removeJoints: true);
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(broke, Is.True);

            // The link itself survives, it just is not part of a chain any more.
            Assert.That(SEntMan.EntityExists(links[2]), Is.True);
            Assert.That(SEntMan.HasComponent<ChainLinkComponent>(links[2]), Is.False);

            Assert.That(SEntMan.GetComponent<ChainLinkComponent>(links[1]).NextLinkUid, Is.Null);
            Assert.That(SEntMan.GetComponent<ChainLinkComponent>(links[3]).PreviousLinkUid, Is.Null);

            // removeJoints: true means the physical connection is gone too, not only the bookkeeping.
            var joints = SEntMan.TryGetComponent<JointComponent>(links[2], out var jointComponent) ? jointComponent.GetJoints.Count : 0;
            Assert.That(joints, Is.Zero, "no joint may still hold the detached link");

            Assert.That(Probe.Segmented.Select(pair => pair.RaisedOn), Does.Contain(links[1]).And.Contain(links[3]));
            Assert.That(Probe.InitiallyBroken.Count(pair => pair.RaisedOn == links[0]), Is.EqualTo(1));
            Assert.That(Probe.InitiallyBroken.Count(pair => pair.RaisedOn == links[4]), Is.EqualTo(1));
            Assert.That(Chains.IsChainFullyIntact(links[1]), Is.False);
        });
    }

    [Test]
    public async Task TryBreakChainFromIgnoresAnEntityThatIsNotAChainLink()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var notALink = SEntMan.SpawnEntity(null, new EntityCoordinates(testMap.Grid, Vector2.One));
            Assert.That(Chains.TryBreakChainFrom(notALink), Is.False);
            Assert.That(Chains.IsChainFullyIntact(notALink), Is.False);
            Assert.That(Chains.HasAdjacentLink(notALink), Is.False);
        });
    }

    [Test]
    public async Task DeletingAnEdgeBreaksTheChainAndOrphansTheOtherEdge()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid);

        await Server.WaitPost(() =>
        {
            Probe.Clear();
            SEntMan.DeleteEntity(links[0]);
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var otherEdge = SEntMan.GetComponent<ChainEdgeComponent>(links[^1]);
            Assert.That(otherEdge.Broken, Is.True);
            Assert.That(otherEdge.OtherEdgeUid, Is.EqualTo(EntityUid.Invalid), "the surviving edge must not keep pointing at the dead one");

            Assert.That(SEntMan.GetComponent<ChainLinkComponent>(links[1]).PreviousLinkUid, Is.Null);
            Assert.That(Chains.IsChainFullyIntact(links[2]), Is.False);
            Assert.That(Chains.IsChainFullyIntact(links[^1]), Is.False);

            Assert.That(Probe.InitiallyBroken.Any(pair => pair.RaisedOn == links[^1]), Is.True, "the far edge is told");
        });
    }

    [Test]
    public async Task RemovingAJointBetweenTwoLinksBreaksTheChain()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid);

        // Something else removing the joint (a physics reset, an admin command) is a break too.
        await Server.WaitPost(() =>
        {
            Probe.Clear();
            var jointId = SEntMan.GetComponent<ChainLinkComponent>(links[2]).NextLinkJointId!;
            SEntMan.System<SharedJointSystem>().RemoveJoint(links[2], jointId);
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<ChainLinkComponent>(links[2]).NextLinkUid, Is.Null);
            Assert.That(SEntMan.GetComponent<ChainLinkComponent>(links[3]).PreviousLinkUid, Is.Null);
            Assert.That(Chains.IsChainFullyIntact(links[1]), Is.False);
            Assert.That(SEntMan.GetComponent<ChainEdgeComponent>(links[0]).Broken, Is.True);
            Assert.That(SEntMan.GetComponent<ChainEdgeComponent>(links[4]).Broken, Is.True);
            Assert.That(Probe.Segmented.Select(pair => pair.RaisedOn), Does.Contain(links[2]).And.Contain(links[3]));
        });
    }

    /// <summary>
    ///     The joints are what makes it a chain: towing one end drags the other, and a broken chain lets go.
    ///         The broken chain is the control, without it this passes for any two bodies that happen to be near.
    /// </summary>
    [Test]
    public async Task AnIntactChainTowsItsOtherEndAndABrokenOneDoesNot()
    {
        var testMap = await Pair.CreateTestMap();
        var intact = await SpawnChainAsync(testMap.Grid, position: new Vector2(0.5f, 0.5f));
        var severed = await SpawnChainAsync(testMap.Grid, position: new Vector2(0.5f, 5.5f));

        await Server.WaitPost(() => SEntMan.DeleteEntity(severed[2]));
        await Pair.RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            var physics = SEntMan.System<SharedPhysicsSystem>();
            physics.SetLinearVelocity(intact[^1], new Vector2(6f, 0f));
            physics.SetLinearVelocity(severed[^1], new Vector2(6f, 0f));
        });
        await Pair.RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            var transform = SEntMan.System<SharedTransformSystem>();
            var intactSpan = (transform.GetWorldPosition(intact[^1]) - transform.GetWorldPosition(intact[0])).Length();
            var severedSpan = (transform.GetWorldPosition(severed[^1]) - transform.GetWorldPosition(severed[0])).Length();

            // Link spacing is at most the joint length plus the two anchor offsets.
            var reach = (ChainLength - 1) * SegmentOffset * 3f;
            Assert.That(intactSpan, Is.LessThanOrEqualTo(reach + 0.5f), "the intact chain's ends are held together");
            Assert.That(severedSpan, Is.GreaterThan(reach + 1f), "control: with the chain cut, the towed end leaves");
        });
    }

    private string? StretchJointId(EntityUid link) => SEntMan.GetComponent<ChainLinkComponent>(link).StretchJointId;

    [Test]
    public async Task AChainIsHeldTogetherEndToEndByOneStretchJoint()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid);

        await Server.WaitAssertion(() =>
        {
            var stretchJointId = StretchJointId(links[0]);
            Assert.That(stretchJointId, Is.Not.Null, "the start has a stretch joint");
            Assert.That(StretchJointId(links[^1]), Is.EqualTo(stretchJointId), "to the end");
            Assert.That(HasJoint(links[0], stretchJointId) && HasJoint(links[^1], stretchJointId), Is.True, "and it is really on both bodies");

            for (var i = 1; i < links.Count - 1; i++)
                Assert.That(StretchJointId(links[i]), Is.Null, $"link {i} is not an end");

            // Never shorter than the chain pulled straight, or it would fight the links' own joints
            var joint = (DistanceJoint)SEntMan.GetComponent<JointComponent>(links[0]).GetJoints[stretchJointId!];
            Assert.That(joint.MaxLength, Is.EqualTo((3 * ChainLength - 5) * SegmentOffset).Within(0.001f));
        });
    }

    [Test]
    public async Task BreakingAChainGivesEachLongEnoughSegmentItsOwnStretchJoint()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid, length: 9);

        // Leaves 0-4 (five links) and 6-8 (three); then 0-1 (two, no stretch joint) and 3-4 (two)
        await Server.WaitPost(() => SEntMan.DeleteEntity(links[5]));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(StretchJointId(links[0]), Is.Not.Null.And.EqualTo(StretchJointId(links[4])), "first segment, end to end");
            Assert.That(StretchJointId(links[6]), Is.Not.Null.And.EqualTo(StretchJointId(links[8])), "second segment, end to end");
            Assert.That(StretchJointId(links[0]), Is.Not.EqualTo(StretchJointId(links[6])), "one each");
            Assert.That(HasJoint(links[8], StretchJointId(links[0])), Is.False, "the old end-to-end joint is gone");
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(links[2]));
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            foreach (var link in new[] { links[0], links[1], links[3], links[4] })
                Assert.That(StretchJointId(link), Is.Null, "two links need no stretch joint");

            Assert.That(StretchJointId(links[6]), Is.Not.Null, "an untouched segment keeps its own");
        });
    }

    /// <summary>
    ///     A contained body's joints are relayed to its container's owner, so a stretch joint with both ends held by
    ///         one person would join them to themselves. It is taken off while an end is contained.
    /// </summary>
    [Test]
    public async Task AStretchJointIsOnlyThereWhileNeitherEndIsContained()
    {
        var testMap = await Pair.CreateTestMap();
        var links = await SpawnChainAsync(testMap.Grid);

        EntityUid holder = default;
        await Server.WaitPost(() =>
        {
            Probe.Clear();
            holder = SEntMan.SpawnEntity(null, new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            SEntMan.EnsureComponent<PhysicsComponent>(holder); // a relay target wakes its body
            var containerSystem = SEntMan.System<SharedContainerSystem>();
            var container = containerSystem.EnsureContainer<Container>(holder, "ks-test-chain-holder");
            Assert.That(containerSystem.Insert(links[0], container, force: true), Is.True, "precondition: the end goes in");
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(StretchJointId(links[0]), Is.Null, "contained start");
            Assert.That(StretchJointId(links[^1]), Is.Null, "and the other end lets go of it too");
            Assert.That(Probe.Segmented, Is.Empty, "taking a stretch joint off is not a break");
            Assert.That(Chains.IsChainFullyIntact(links[1]), Is.True);
        });

        await Server.WaitPost(() =>
        {
            var containerSystem = SEntMan.System<SharedContainerSystem>();
            Assert.That(containerSystem.TryRemoveFromContainer(links[0], force: true), Is.True, "precondition: the end comes out");
        });
        await Pair.RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(StretchJointId(links[0]), Is.Not.Null.And.EqualTo(StretchJointId(links[^1])), "back once out");
            Assert.That(Probe.Segmented, Is.Empty);
        });
    }
}
