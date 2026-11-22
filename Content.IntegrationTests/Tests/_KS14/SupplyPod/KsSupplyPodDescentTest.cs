#nullable enable
using System;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.Trail;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.SupplyPod;

/// <summary>
///     <see cref="SupplyPodComponent.DescentDuration"/>: a pod seen only for the end of its fall is nowhere to be seen, its
///         trail included, until then, and falls in over what is left; one without it is seen the whole way down.
/// </summary>
[TestFixture]
[TestOf(typeof(SupplyPodComponent))]
public sealed class KsSupplyPodDescentTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _shortDescentPod = "KsTestDescentShortPod";
    private readonly EntProtoId _wholeDescentPod = "KsTestDescentWholePod";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestDescentTrail
  components:
  - type: KsTrail
    sprite:
      sprite: /Textures/_KS14/Objects/SupplyPod/supplypods_32x32.rsi
      state: smoke

- type: entity
  parent: SupplyPod
  id: KsTestDescentWholePod
  components:
  - type: SupplyPod
    height: 10
    fallDuration: 4s
    fallSound: null
    impactSound: null
    angularDeviation: 0
    trailProto: KsTestDescentTrail

- type: entity
  parent: KsTestDescentWholePod
  id: KsTestDescentShortPod
  components:
  - type: SupplyPod
    height: 10
    fallDuration: 4s
    descentDuration: 1.5s
    fallSound: null
    impactSound: null
    angularDeviation: 0
    trailProto: KsTestDescentTrail
";

    [Test]
    public async Task APodSeenOnlyAtTheEndOfItsFallIsHiddenUntilThen()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();
        var tickRate = (float) server.ResolveDependency<IGameTiming>().TickRate;

        EntityUid shortPodUid = default, wholePodUid = default;
        EntityUid shortTrailUid = default, wholeTrailUid = default;
        TimeSpan fall = default, descent = default;
        var height = 0f;

        await server.WaitPost(() =>
        {
            shortPodUid = server.EntMan.SpawnEntity(_shortDescentPod, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            wholePodUid = server.EntMan.SpawnEntity(_wholeDescentPod, new EntityCoordinates(testMap.Grid, 10.5f, 0.5f));

            var shortPodComponent = server.EntMan.GetComponent<SupplyPodComponent>(shortPodUid);
            fall = shortPodComponent.FallDuration;
            descent = SharedSupplyPodSystem.GetVisibleDescent(shortPodComponent);
            height = shortPodComponent.Height;
            shortTrailUid = shortPodComponent.TrailEntity!.Value;
            wholeTrailUid = server.EntMan.GetComponent<SupplyPodComponent>(wholePodUid).TrailEntity!.Value;
        });

        Assert.That(descent, Is.LessThan(fall), "precondition: a pod unseen for part of its fall");

        // Half way through the part nobody sees.
        var hidden = fall - descent;
        await Pair.RunTicksSync((int) (hidden.TotalSeconds * 0.5 * tickRate));

        await client.WaitAssertion(() =>
        {
            Assert.That(PodAlpha(shortPodUid), Is.LessThan(0.01f), "The pod shows before its descent.");
            Assert.That(HeightAboveLanding(shortPodUid), Is.GreaterThan(height * 0.95f), "The pod is not waiting up where its fall starts.");
            Assert.That(MaxTrailAlpha(shortTrailUid, shortPodUid), Is.LessThan(0.01f), "The pod's trail shows before its descent.");

            Assert.That(PodAlpha(wholePodUid), Is.GreaterThan(0.1f), "A pod seen the whole way down is hidden.");
            Assert.That(MaxTrailAlpha(wholeTrailUid, wholePodUid), Is.GreaterThan(0.1f), "The trail of a pod seen the whole way down is hidden.");
        });

        // Three quarters of the way through its descent: faded in, which takes half of it, and coming down.
        await Pair.RunTicksSync((int) ((hidden.TotalSeconds * 0.5 + descent.TotalSeconds * 0.75) * tickRate));

        await client.WaitAssertion(() =>
        {
            Assert.That(PodAlpha(shortPodUid), Is.GreaterThan(0.9f), "The pod is not seen during its descent.");
            var above = HeightAboveLanding(shortPodUid);
            Assert.That(above, Is.GreaterThan(height * 0.05f).And.LessThan(height * 0.5f),
                $"The pod is not falling over its descent: {above} above where it lands, falling from {height}.");
            Assert.That(MaxTrailAlpha(shortTrailUid, shortPodUid), Is.GreaterThan(0.1f), "The pod's trail is not drawn during its descent.");
        });

        float PodAlpha(EntityUid serverUid)
        {
            return client.EntMan.GetComponent<SpriteComponent>(Pair.ToClientUid(serverUid)).Color.A;
        }

        float HeightAboveLanding(EntityUid serverUid)
        {
            var clientUid = Pair.ToClientUid(serverUid);
            var activeComponent = client.EntMan.GetComponent<ActiveSupplyPodComponent>(clientUid);
            var localPosition = client.EntMan.GetComponent<TransformComponent>(clientUid).LocalPosition;
            return Vector2.Distance(localPosition, activeComponent.DestinationCoordinates.Position);
        }

        // As KsTrailOverlay draws it: revealed down to its source's place along it.
        float MaxTrailAlpha(EntityUid serverTrailUid, EntityUid serverPodUid)
        {
            var trailUid = Pair.ToClientUid(serverTrailUid);
            var trailComponent = client.EntMan.GetComponent<KsTrailComponent>(trailUid);
            var trailTransform = client.EntMan.GetComponent<TransformComponent>(trailUid);
            var podTransform = client.EntMan.GetComponent<TransformComponent>(Pair.ToClientUid(serverPodUid));
            var sourceDistance = Vector2.Dot(podTransform.LocalPosition - trailTransform.LocalPosition,
                trailTransform.LocalRotation.RotateVec(Vector2.UnitY));

            var trailSystem = client.EntMan.System<KsTrailSystem>();
            var curTime = client.ResolveDependency<IGameTiming>().CurTime;
            var max = 0f;
            for (var index = 1; index <= trailSystem.GetDrawLength(trailComponent); index++)
                max = MathF.Max(max, trailSystem.GetTileAlpha(trailComponent, index, curTime, sourceDistance));

            return max;
        }
    }
}
