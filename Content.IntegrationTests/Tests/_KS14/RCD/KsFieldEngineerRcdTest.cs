using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.RCD;

/// <summary>
///     What the tests of the field engineer RCD share: picking one of its settings, and plating either side of the
///         player to build on.
/// </summary>
public abstract class KsFieldEngineerRcdTest : InteractionTest
{
    protected static readonly EntProtoId FieldEngineerRcd = "KsRCDFieldEngineer";

    /// <summary>
    ///     Opens the held RCD's menu and picks <paramref name="protoId"/>.
    /// </summary>
    protected async Task SetRcdProto(NetEntity rcd, ProtoId<RCDPrototype> protoId)
    {
        await UseInHand();
        await RunTicks(3);
        Assert.That(IsUiOpen(RcdUiKey.Key), Is.True, "RCD UI was not opened when using the RCD while holding it.");

        await SendBui(RcdUiKey.Key, new RCDSystemMessage(protoId), rcd);
        await CloseBui(RcdUiKey.Key, rcd);
    }

    /// <summary>
    ///     Plates the tiles just north and south of the player, and returns them.
    /// </summary>
    protected async Task<(EntityCoordinates North, EntityCoordinates South)> PlateNorthAndSouth()
    {
        var north = Transform.WithEntityId(new EntityCoordinates(SPlayer, new Vector2(0, 1)), MapData.Grid);
        var south = Transform.WithEntityId(new EntityCoordinates(SPlayer, new Vector2(0, -1)), MapData.Grid);

        await SetTile(PlatingRCD, SEntMan.GetNetCoordinates(north), MapData.Grid);
        await SetTile(PlatingRCD, SEntMan.GetNetCoordinates(south), MapData.Grid);

        return (north, south);
    }
}
