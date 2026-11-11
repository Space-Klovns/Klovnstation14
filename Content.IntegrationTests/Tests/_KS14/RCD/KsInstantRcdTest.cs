using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.RCD;

/// <summary>
///     An instant RCD builds with no wait, and takes apart what its own user built with no wait, but still takes the
///         normal time over anything else.
/// </summary>
[TestOf(typeof(RCDSystem))]
public sealed class KsInstantRcdTest : InteractionTest
{
    private static readonly EntProtoId InstantRcd = "KsRCDFieldEngineer";
    private static readonly EntProtoId Wall = "WallSolid";
    private static readonly ProtoId<RCDPrototype> RcdSettingWall = "WallSolid";
    private static readonly ProtoId<RCDPrototype> RcdSettingDeconstruct = "Deconstruct";

    [Test]
    public async Task BuildsInstantlyAndDeconstructsOnlyItsOwnWorkInstantly()
    {
        var pNorth = Transform.WithEntityId(new EntityCoordinates(SPlayer, new Vector2(0, 1)), MapData.Grid);
        var pSouth = Transform.WithEntityId(new EntityCoordinates(SPlayer, new Vector2(0, -1)), MapData.Grid);

        await SetTile(PlatingRCD, SEntMan.GetNetCoordinates(pNorth), MapData.Grid);
        await SetTile(PlatingRCD, SEntMan.GetNetCoordinates(pSouth), MapData.Grid);

        var rcd = await PlaceInHands(InstantRcd);
        await SetRcdProto(rcd, RcdSettingWall);

        Assert.That(ProtoMan.Index(RcdSettingWall).Delay, Is.GreaterThan(0f),
            "the wall must normally take time to build, or this proves nothing about the instant RCD");

        // Built at once, with no do-after to wait out.
        await Interact(null, pNorth, awaitDoAfters: false);
        await RunTicks(3);
        // The build effect lingers beside it, so look for the wall itself rather than at everything around.
        var builtWallUid = await FindEntity(Wall.Id);
        var builtWallNetUid = FromServer(builtWallUid);

        // A wall the player didn't build, standing on the other side.
        EntityUid otherWallUid = default;
        await Server.WaitPost(() => otherWallUid = SEntMan.SpawnAtPosition(Wall, pSouth));
        await RunTicks(1);
        var otherWallNetUid = FromServer(otherWallUid);
        var otherDelay = SEntMan.GetComponent<RCDDeconstructableComponent>(otherWallUid).Delay;
        Assert.That(otherDelay, Is.GreaterThan(1f));

        await SetRcdProto(rcd, RcdSettingDeconstruct);

        // The player's own wall comes down at once.
        await Interact(builtWallUid, pNorth, awaitDoAfters: false);
        await RunTicks(3);
        AssertDeleted(builtWallNetUid);

        // Someone else's takes the usual time.
        await Interact(otherWallUid, pSouth, awaitDoAfters: false);
        await RunTicks(3);
        Assert.That(SEntMan.EntityExists(otherWallUid), Is.True, "a wall the player didn't build should not come down instantly");

        await RunSeconds(otherDelay + 1f);
        AssertDeleted(otherWallNetUid);
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
