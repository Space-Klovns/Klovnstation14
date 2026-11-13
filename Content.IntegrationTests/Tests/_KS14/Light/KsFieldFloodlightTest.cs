using System.Numerics;
using Content.IntegrationTests.Tests._KS14.RCD;
using Content.Server._KS14.Light;
using Content.Shared.Light.Components;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Light;

/// <summary>
///     A <see cref="KsHandheldLightStartsOnComponent"/> light comes out lit, and the field engineer RCD puts up floodlights
///         that are already bolted down and lit.
/// </summary>
[TestOf(typeof(KsHandheldLightStartsOnSystem))]
public sealed class KsFieldFloodlightTest : KsFieldEngineerRcdTest
{
    private static readonly EntProtoId Floodlight = "KsFloodlightField";
    private static readonly ProtoId<RCDPrototype> RcdSettingFloodlight = "KsFieldFloodlight";

    // A plain floodlight, and the same with the component, which only this test defines.
    private static readonly EntProtoId PlainFloodlight = "Floodlight";
    private readonly EntProtoId _litFloodlight = "KsTestLitFloodlight";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: Floodlight
  id: KsTestLitFloodlight
  components:
  - type: KsHandheldLightStartsOn
";

    [Test]
    public async Task OnlyTheComponentLightsItAtSpawn()
    {
        EntityUid plainUid = default;
        EntityUid litUid = default;

        await Server.WaitPost(() =>
        {
            plainUid = SEntMan.SpawnEntity(PlainFloodlight, SEntMan.GetCoordinates(PlayerCoords));
            litUid = SEntMan.SpawnEntity(_litFloodlight, SEntMan.GetCoordinates(PlayerCoords));
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<HandheldLightComponent>(plainUid).Activated, Is.False, "a plain floodlight starts dark");
            Assert.That(SEntMan.GetComponent<HandheldLightComponent>(litUid).Activated, Is.True, "the component lights it");
            Assert.That(SEntMan.GetComponent<Robust.Server.GameObjects.PointLightComponent>(litUid).Enabled, Is.True, "and its light shines");
        });
    }

    [Test]
    public async Task RcdPutsUpABoltedDownLitFloodlight()
    {
        var target = Transform.WithEntityId(new EntityCoordinates(SPlayer, new Vector2(0, 1)), MapData.Grid);
        await SetTile(PlatingRCD, SEntMan.GetNetCoordinates(target), MapData.Grid);

        var rcd = await PlaceInHands(FieldEngineerRcd);
        await SetRcdProto(rcd, RcdSettingFloodlight);

        await Interact(null, target, awaitDoAfters: true);
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            EntityUid? floodlightUid = null;
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var metaDataComponent))
            {
                if (metaDataComponent.EntityPrototype?.ID == Floodlight.Id)
                    floodlightUid = uid;
            }

            Assert.That(floodlightUid, Is.Not.Null, "the RCD should have put up a floodlight");
            Assert.That(SEntMan.GetComponent<TransformComponent>(floodlightUid!.Value).Anchored, Is.True, "it comes bolted down");
            Assert.That(SEntMan.GetComponent<HandheldLightComponent>(floodlightUid.Value).Activated, Is.True, "it comes lit");
        });
    }
}
