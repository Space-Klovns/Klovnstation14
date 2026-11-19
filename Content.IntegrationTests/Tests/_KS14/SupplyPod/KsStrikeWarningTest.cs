using System.Threading.Tasks;
using Content.Client._KS14.SupplyPod.StrikeWarning;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.SupplyPod.LandingZone;
using Content.Shared._KS14.SupplyPod.StrikeWarning;
using Content.Shared.Trigger.Components.Effects;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.IntegrationTests.Tests._KS14.SupplyPod;

/// <summary>
///     <see cref="KsStrikeWarningComponent"/>: a falling pod set up with the hostile marker puts that marker down, sized
///         to its blast when it carries one, and every hostile marker in the game draws above mobs.
/// </summary>
[TestFixture]
[TestOf(typeof(KsStrikeWarningComponent))]
public sealed class KsStrikeWarningTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _explosivePod = "KsTestStrikeWarningExplosivePod";
    private readonly EntProtoId _inertPod = "KsTestStrikeWarningInertPod";
    private readonly EntProtoId _warning = "KsTestStrikeWarning";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestStrikeWarning
  components:
  - type: Sprite
    sprite: /Textures/_KS14/Objects/SupplyPod/strike_warning.rsi
    layers:
    - state: area
      map: [ enum.KsStrikeWarningLayers.Area ]
    - state: reticle
      map: [ enum.KsStrikeWarningLayers.Reticle ]
    - state: chevrons
      map: [ enum.KsStrikeWarningLayers.Chevrons ]
  - type: SupplyPodLandingZoneIndicator
  - type: KsStrikeWarning
    radius: 1.25
    minimumRadius: 0.5
    chevronStartScale: 2

- type: entity
  parent: SupplyPod
  id: KsTestStrikeWarningInertPod
  components:
  - type: SupplyPod
    height: 10
    fallDuration: 4s
    fallSoundDelay: 0s
    fallSound: null
  - type: SupplyPodLandingZone
    indicatorProto: KsTestStrikeWarning

- type: entity
  parent: KsTestStrikeWarningInertPod
  id: KsTestStrikeWarningExplosivePod
  components:
  - type: ExplosionOnTrigger
    totalIntensity: 400
    intensitySlope: 4
    maxTileIntensity: 50
";

    [Test]
    public async Task FallingPodPutsDownItsHostileMarkerSizedToItsBlast()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();

        EntityUid explosivePodUid = default;
        EntityUid inertPodUid = default;
        TimeSpan fallDuration = default;

        await server.WaitPost(() =>
        {
            explosivePodUid = server.EntMan.SpawnEntity(_explosivePod, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            inertPodUid = server.EntMan.SpawnEntity(_inertPod, new EntityCoordinates(testMap.Grid, 10.5f, 0.5f));
            fallDuration = server.EntMan.GetComponent<SupplyPodComponent>(explosivePodUid).FallDuration;
        });

        await Pair.RunTicksSync(5);

        EntityUid explosiveWarningUid = default;
        await server.WaitAssertion(() =>
        {
            explosiveWarningUid = AssertMarker(explosivePodUid);
            var inertWarningUid = AssertMarker(inertPodUid);

            var explosionComponent = server.EntMan.GetComponent<ExplosionOnTriggerComponent>(explosivePodUid);
            var blastRadius = server.EntMan.System<ExplosionSystem>().IntensityToRadius(
                explosionComponent.TotalIntensity,
                explosionComponent.IntensitySlope,
                explosionComponent.MaxTileIntensity);
            Assert.That(server.ProtoMan.Index(_warning).TryGetComponent<KsStrikeWarningComponent>(
                out var prototypeComponent, server.ResolveDependency<IComponentFactory>()));
            var ownRadius = prototypeComponent.Radius;

            Assert.That(server.EntMan.GetComponent<KsStrikeWarningComponent>(explosiveWarningUid).Radius,
                Is.EqualTo(blastRadius).Within(0.001f),
                "The marker under an explosive pod is not sized to its blast.");
            Assert.That(server.EntMan.GetComponent<KsStrikeWarningComponent>(inertWarningUid).Radius,
                Is.EqualTo(ownRadius).Within(0.001f),
                "The marker under a pod with no blast lost its own size.");
        });

        // Part way down, the chevrons are still closing in from outside the rim.
        await Pair.RunTicksSync((int)(fallDuration.TotalSeconds * 0.5 * (float)server.ResolveDependency<IGameTiming>().TickRate));

        await client.WaitAssertion(() =>
        {
            var clientWarningUid = Pair.ToClientUid(explosiveWarningUid);
            var spriteSystem = client.EntMan.System<SpriteSystem>();
            var spriteComponent = client.EntMan.GetComponent<SpriteComponent>(clientWarningUid);
            var warningComponent = client.EntMan.GetComponent<KsStrikeWarningComponent>(clientWarningUid);

            var areaScale = spriteComponent[spriteSystem.LayerMapGet((clientWarningUid, spriteComponent), KsStrikeWarningLayers.Area)].Scale.X;
            var chevronScale = spriteComponent[spriteSystem.LayerMapGet((clientWarningUid, spriteComponent), KsStrikeWarningLayers.Chevrons)].Scale.X;

            Assert.That(areaScale, Is.EqualTo(KsStrikeWarningSystem.AreaScale(warningComponent)).Within(0.001f),
                "The marked area is not drawn at the blast's size.");
            Assert.That(chevronScale / areaScale,
                Is.GreaterThan(1.05f).And.LessThan(warningComponent.ChevronStartScale - 0.05f),
                "The chevrons are not closing in on the rim.");
        });

        await Pair.RunTicksSync((int)(fallDuration.TotalSeconds * 2 * (float)server.ResolveDependency<IGameTiming>().TickRate));

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(explosiveWarningUid), "The marker outlived the pod's landing.");
        });

        EntityUid AssertMarker(EntityUid podUid)
        {
            var indicatorUid = server.EntMan.GetComponent<SupplyPodLandingZoneComponent>(podUid).IndicatorEntity;
            Assert.That(indicatorUid, Is.Not.Null, "No marker while the pod falls.");
            Assert.That(server.EntMan.GetComponent<MetaDataComponent>(indicatorUid.Value).EntityPrototype?.ID,
                Is.EqualTo(_warning.Id),
                "The pod put down some other marker than the hostile one it is set up with.");
            Assert.That(server.EntMan.HasComponent<KsStrikeWarningComponent>(indicatorUid.Value));
            return indicatorUid.Value;
        }
    }

    /// <summary>
    ///     Content: every marker for something hostile landing is drawn over the mobs standing on it.
    /// </summary>
    [Test]
    public async Task HostileMarkersDrawAboveMobs()
    {
        var client = Pair.Client;

        await client.WaitAssertion(() =>
        {
            var factory = client.ResolveDependency<IComponentFactory>();
            var markers = 0;

            foreach (var prototype in client.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (prototype.Abstract
                    || !prototype.TryGetComponent<KsStrikeWarningComponent>(out _, factory)
                    || prototype.ID == _warning.Id)
                    continue;

                markers++;
                Assert.That(prototype.TryGetComponent<SpriteComponent>(out var spriteComponent, factory),
                    $"{prototype.ID} has no sprite.");
                Assert.That(spriteComponent.DrawDepth, Is.GreaterThan((int)DrawDepth.Mobs),
                    $"{prototype.ID} draws under mobs standing on it.");
            }

            Assert.That(markers, Is.GreaterThan(0), "No hostile strike markers found to check.");
        });
    }
}
