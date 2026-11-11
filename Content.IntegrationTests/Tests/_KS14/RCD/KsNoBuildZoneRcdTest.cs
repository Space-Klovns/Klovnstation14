using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.RCD;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.RCD;

/// <summary>
///     An RCD can't be used in a no-build zone. The client predicts it, so the placement ghost shows the tile as
///         impossible, and the server refuses it.
/// </summary>
[TestOf(typeof(KsNoBuildZoneSystem))]
public sealed class KsNoBuildZoneRcdTest : InteractionTest
{
    private static readonly EntProtoId Rcd = "KsRCDFieldEngineer";
    private static readonly EntProtoId Wall = "WallSolid";
    private static readonly ProtoId<RCDPrototype> RcdSettingWall = "WallSolid";

    [Test]
    public async Task NoBuildingInsideTheZone()
    {
        var pNorth = Transform.WithEntityId(new EntityCoordinates(SPlayer, new Vector2(0, 1)), MapData.Grid);
        var pSouth = Transform.WithEntityId(new EntityCoordinates(SPlayer, new Vector2(0, -1)), MapData.Grid);

        await SetTile(PlatingRCD, SEntMan.GetNetCoordinates(pNorth), MapData.Grid);
        await SetTile(PlatingRCD, SEntMan.GetNetCoordinates(pSouth), MapData.Grid);

        // The north tile is in the zone; the south one, two tiles away, isn't.
        await Server.WaitPost(() =>
        {
            var northMapCoordinates = Transform.ToMapCoordinates(pNorth);
            SEntMan.System<KsNoBuildZoneSystem>().SetZones(MapData.MapUid,
                [new KsNoBuildZone(northMapCoordinates.Position, 0.75f)]);
        });

        var rcd = await PlaceInHands(Rcd);
        await SetRcdProto(rcd, RcdSettingWall);
        await RunTicks(5);

        // What the placement ghost asks, on the client.
        await Client.WaitAssertion(() =>
        {
            var clientRcdUid = CEntMan.GetEntity(rcd);
            var clientPlayerUid = CEntMan.GetEntity(Player);
            var clientGridUid = CEntMan.GetEntity(SEntMan.GetNetEntity(MapData.Grid.Owner));
            var mapGrid = CEntMan.GetComponent<MapGridComponent>(clientGridUid);
            var rcdComponent = CEntMan.GetComponent<RCDComponent>(clientRcdUid);
            var mapSystem = CEntMan.System<SharedMapSystem>();
            var rcdSystem = CEntMan.System<RCDSystem>();

            bool IsValid(EntityCoordinates serverCoordinates)
            {
                var coordinates = CEntMan.GetCoordinates(SEntMan.GetNetCoordinates(serverCoordinates));
                var tile = mapSystem.GetTileRef(clientGridUid, mapGrid, coordinates);
                return rcdSystem.IsRCDOperationStillValid(clientRcdUid, rcdComponent, clientGridUid, mapGrid, tile, tile.GridIndices, null, clientPlayerUid, popMsgs: false);
            }

            Assert.That(IsValid(pNorth), Is.False, "the client should already know the zone is off limits, so the ghost shows it");
            Assert.That(IsValid(pSouth), Is.True, "outside the zone, building is fine");
        });

        // And the server refuses it.
        await Interact(null, pNorth, awaitDoAfters: false);
        await RunTicks(5);
        await Interact(null, pSouth, awaitDoAfters: false);
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var walls = 0;
            var northWall = false;
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var metaDataComponent, out var transformComponent))
            {
                if (metaDataComponent.EntityPrototype?.ID != Wall.Id)
                    continue;

                walls++;
                if ((Transform.ToMapCoordinates(transformComponent.Coordinates).Position - Transform.ToMapCoordinates(pNorth).Position).Length() < 0.5f)
                    northWall = true;
            }

            Assert.That(northWall, Is.False, "no wall should go up in the zone");
            Assert.That(walls, Is.EqualTo(1), "the wall outside the zone should have gone up");
        });
    }

    private async Task SetRcdProto(NetEntity rcd, ProtoId<RCDPrototype> protoId)
    {
        await UseInHand();
        await RunTicks(3);
        Assert.That(IsUiOpen(RcdUiKey.Key), Is.True, "RCD UI was not opened when using the RCD while holding it.");

        await SendBui(RcdUiKey.Key, new RCDSystemMessage(protoId), rcd);
        await CloseBui(RcdUiKey.Key, rcd);
    }
}
