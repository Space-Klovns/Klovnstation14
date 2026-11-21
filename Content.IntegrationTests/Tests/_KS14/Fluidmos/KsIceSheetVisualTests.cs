#nullable enable
using Content.Client.IconSmoothing;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Fluidmos.Ice;
using Content.Shared._KS14.Fluidmos.Ice;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluidmos;

/// <summary>
///     An ice sheet is drawn as the puddle it froze out of, frozen.
/// </summary>
[TestOf(typeof(KsIceSheetComponent))]
public sealed class KsIceSheetVisualTests : GameTest
{
    private static readonly EntProtoId PuddlePrototype = "Puddle";

    private const float ColdTemperature = 253.15f;

    // Deliberately NOT static: it only exists in this test's YAML, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _inertReagent = "KsTestIceVisualInert";

    // Nothing melts, boils or sublimes it, so the sheet holds what the test put in.
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestIceVisualInert
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
";

    /// <summary>
    ///     A thin sheet is drawn as a small splat, like the puddle it would be, and a full one fills the tile and smooths
    ///         into its neighbours, as a full puddle does.
    /// </summary>
    [Test]
    public async Task SheetIsDrawnInThePuddlesShapeForItsFill()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var testMap = await Pair.CreateTestMap();
        var overflowVolume = 0f;

        await server.WaitPost(() =>
        {
            Assert.That(server.ProtoMan.Index(PuddlePrototype).TryComp<PuddleComponent>(out var puddleComponent, server.EntMan.ComponentFactory));
            overflowVolume = (float)puddleComponent!.OverflowVolume;
            server.System<KsIceSheetSystem>().AddIce(testMap.GridCoords,
                new Solution(_inertReagent, FixedPoint2.New(overflowVolume * SharedPuddleSystem.LowThreshold / 2f)) { Temperature = ColdTemperature });
        });
        await Pair.RunTicksSync(5);

        EntityUid sheetUid = default;
        await server.WaitPost(() =>
        {
            Assert.That(server.System<KsIceSheetSystem>().TryGetIceSheet(testMap.Grid.Owner, testMap.Tile.GridIndices, out var sheet));
            sheetUid = sheet.Owner;
        });

        await client.WaitAssertion(() =>
        {
            var clientUid = Pair.ToClientUid(sheetUid);
            var spriteComponent = client.EntMan.GetComponent<SpriteComponent>(clientUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(client.System<SpriteSystem>().LayerGetRsiState((clientUid, spriteComponent), 0).Name, Is.EqualTo("splata"),
                    "A thin sheet is not drawn as a small puddle.");
                Assert.That(client.EntMan.GetComponent<IconSmoothComponent>(clientUid).Enabled, Is.False,
                    "A thin sheet smooths into its neighbours.");
            }
        });

        await server.WaitPost(() => server.System<KsIceSheetSystem>().AddIce(testMap.GridCoords,
            new Solution(_inertReagent, FixedPoint2.New(overflowVolume)) { Temperature = ColdTemperature }));
        await Pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var clientUid = Pair.ToClientUid(sheetUid);
            var spriteComponent = client.EntMan.GetComponent<SpriteComponent>(clientUid);
            var state = client.System<SpriteSystem>().LayerGetRsiState((clientUid, spriteComponent), 0).Name;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(client.EntMan.GetComponent<IconSmoothComponent>(clientUid).Enabled, Is.True,
                    "A full sheet does not smooth into its neighbours as a full puddle does.");
                Assert.That(state, Is.Not.EqualTo("splata").And.Not.EqualTo("splatb"), "A full sheet is still drawn as a small puddle.");
            }
        });
    }
}
