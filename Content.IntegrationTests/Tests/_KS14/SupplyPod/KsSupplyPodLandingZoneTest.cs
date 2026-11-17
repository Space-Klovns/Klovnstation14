using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using NUnit.Framework;
using Robust.Shared.Timing;
using Content.Client._KS14.SupplyPod.LandingZone;
using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.SupplyPod.LandingZone;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.SupplyPod;

[TestFixture]
[TestOf(typeof(SupplyPodLandingZoneComponent))]
public sealed class KsSupplyPodLandingZoneTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId PodPrototype = "SupplyPodCargoBase";

    [Test]
    public async Task IndicatorCountsDownAndGoesWhenThePodLands()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();

        EntityUid podUid = default;
        TimeSpan fallDuration = default;

        await server.WaitPost(() =>
        {
            podUid = server.EntMan.SpawnEntity(PodPrototype, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            fallDuration = server.EntMan.GetComponent<SupplyPodComponent>(podUid).FallDuration;
        });

        await Pair.RunTicksSync(5);

        EntityUid indicatorUid = default;
        await server.WaitAssertion(() =>
        {
            var query = server.EntMan.AllEntityQueryEnumerator<SupplyPodLandingZoneIndicatorComponent>();
            Assert.That(query.MoveNext(out indicatorUid, out var indicatorComponent), "No indicator while the pod falls.");
            Assert.That(indicatorComponent.Duration, Is.EqualTo(fallDuration));
        });

        // Part way through the fall the slider ring must have closed in and the zone have turned.
        await Pair.RunTicksSync((int)(fallDuration.TotalSeconds * 0.5 * (float)server.ResolveDependency<IGameTiming>().TickRate));

        await client.WaitAssertion(() =>
        {
            var clientIndicatorUid = Pair.ToClientUid(indicatorUid);
            var spriteSystem = client.EntMan.System<SpriteSystem>();
            var spriteComponent = client.EntMan.GetComponent<SpriteComponent>(clientIndicatorUid);

            var sliderScale = spriteComponent[spriteSystem.LayerMapGet((clientIndicatorUid, spriteComponent), SupplyPodLandingZoneLayers.Slider)].Scale.X;
            var zoneRotation = spriteComponent[spriteSystem.LayerMapGet((clientIndicatorUid, spriteComponent), SupplyPodLandingZoneLayers.Zone)].Rotation;

            Assert.That(sliderScale, Is.LessThan(1.45f).And.GreaterThan(0.1f), "Slider ring did not close in.");
            Assert.That(zoneRotation.Degrees, Is.LessThan(-10).And.GreaterThan(-80), "Zone did not turn.");
        });

        await Pair.RunTicksSync((int)(fallDuration.TotalSeconds * 2 * (float)server.ResolveDependency<IGameTiming>().TickRate));

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(indicatorUid), "Indicator outlived the pod's landing.");
        });
    }
}
