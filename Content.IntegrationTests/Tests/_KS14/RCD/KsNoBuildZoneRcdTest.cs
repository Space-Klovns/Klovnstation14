using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.RCD;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.RCD;

/// <summary>
///     An RCD can't be used in a no-build zone. The client predicts it, so the placement ghost shows the tile as
///         impossible, and the server refuses it.
/// </summary>
[TestOf(typeof(KsNoBuildZoneSystem))]
public sealed class KsNoBuildZoneRcdTest : KsFieldEngineerRcdTest
{
    private static readonly EntProtoId Wall = "WallSolid";
    private static readonly ProtoId<RCDPrototype> RcdSettingWall = "WallSolid";
    private static readonly LocId BlockedMessage = "ks-rcd-no-build-zone";

    [Test]
    public async Task NoBuildingInsideTheZone()
    {
        var (pNorth, pSouth) = await PlateNorthAndSouth();

        // The north tile is in the zone; the south one, two tiles away, isn't.
        await Server.WaitPost(() =>
        {
            var northMapCoordinates = Transform.ToMapCoordinates(pNorth);
            SEntMan.System<KsNoBuildZoneSystem>().SetZones(MapData.MapUid,
                [new KsNoBuildZone(northMapCoordinates.Position, 0.75f)]);
        });

        var rcd = await PlaceInHands(FieldEngineerRcd);
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

    /// <summary>
    ///     A map can have RCDs blocked altogether, which the client predicts too, and allowed again.
    /// </summary>
    [Test]
    public async Task NoBuildingOnABlockedMap()
    {
        var (pNorth, _) = await PlateNorthAndSouth();

        var rcd = await PlaceInHands(FieldEngineerRcd);
        await SetRcdProto(rcd, RcdSettingWall);

        await Server.WaitPost(() => SEntMan.System<KsNoBuildZoneSystem>().SetBlocked(MapData.MapUid, BlockedMessage));
        await RunTicks(5);

        Assert.That(await ClientSaysValid(rcd, pNorth), Is.False, "the client should already know RCDs are blocked on this map");

        await Interact(null, pNorth, awaitDoAfters: false);
        await RunTicks(5);
        Assert.That(await CountWalls(), Is.Zero, "no wall should go up on a blocked map");

        await Server.WaitPost(() => SEntMan.System<KsNoBuildZoneSystem>().SetBlocked(MapData.MapUid, null));
        await RunTicks(5);

        Assert.That(await ClientSaysValid(rcd, pNorth), Is.True, "the client should know the block was lifted");

        await Interact(null, pNorth, awaitDoAfters: false);
        await RunTicks(5);
        Assert.That(await CountWalls(), Is.EqualTo(1), "with the block lifted the wall should go up");
    }

    /// <summary>
    ///     What the placement ghost asks, on the client.
    /// </summary>
    private async Task<bool> ClientSaysValid(NetEntity rcd, EntityCoordinates serverCoordinates)
    {
        var valid = false;
        await Client.WaitPost(() =>
        {
            var clientRcdUid = CEntMan.GetEntity(rcd);
            var clientPlayerUid = CEntMan.GetEntity(Player);
            var clientGridUid = CEntMan.GetEntity(SEntMan.GetNetEntity(MapData.Grid.Owner));
            var mapGrid = CEntMan.GetComponent<MapGridComponent>(clientGridUid);
            var rcdComponent = CEntMan.GetComponent<RCDComponent>(clientRcdUid);
            var coordinates = CEntMan.GetCoordinates(SEntMan.GetNetCoordinates(serverCoordinates));
            var tile = CEntMan.System<SharedMapSystem>().GetTileRef(clientGridUid, mapGrid, coordinates);
            valid = CEntMan.System<RCDSystem>().IsRCDOperationStillValid(clientRcdUid, rcdComponent, clientGridUid, mapGrid, tile, tile.GridIndices, null, clientPlayerUid, popMsgs: false);
        });

        return valid;
    }

    private async Task<int> CountWalls()
    {
        var walls = 0;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var metaDataComponent))
            {
                if (metaDataComponent.EntityPrototype?.ID == Wall.Id)
                    walls++;
            }
        });

        return walls;
    }
}
