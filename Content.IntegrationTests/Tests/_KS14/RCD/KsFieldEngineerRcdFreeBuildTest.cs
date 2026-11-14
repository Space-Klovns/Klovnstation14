using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.RCD;
using Content.Shared.RCD.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.RCD;

/// <summary>
///     The field engineer RCD builds windows for nothing, so an empty one still builds them, but not what costs charges.
/// </summary>
[TestOf(typeof(RCDSystem))]
public sealed class KsFieldEngineerRcdFreeBuildTest : KsFieldEngineerRcdTest
{
    private static readonly EntProtoId Window = "Window";
    private static readonly EntProtoId Wall = "WallSolid";
    private static readonly ProtoId<RCDPrototype> RcdSettingWindow = "KsFieldWindow";
    private static readonly ProtoId<RCDPrototype> RcdSettingWall = "KsFieldWallSolid";

    [Test]
    public async Task EmptyRcdStillBuildsAFreeWindowButNotAWall()
    {
        Assert.That(ProtoMan.Index(RcdSettingWindow).Cost, Is.EqualTo(0), "the window must be free, or this proves nothing");
        Assert.That(ProtoMan.Index(RcdSettingWall).Cost, Is.GreaterThan(0), "the wall must cost charges, or this proves nothing");

        var (pNorth, pSouth) = await PlateNorthAndSouth();

        var rcd = await PlaceInHands(FieldEngineerRcd);
        var rcdUid = ToServer(rcd);

        // Empty it, and stop it refilling mid-test.
        await Server.WaitPost(() =>
        {
            SEntMan.RemoveComponent<AutoRechargeComponent>(rcdUid);
            SEntMan.System<SharedChargesSystem>().SetCharges(rcdUid, 0);
        });
        await RunTicks(1);

        await SetRcdProto(rcd, RcdSettingWall);
        await Interact(null, pSouth, awaitDoAfters: false);
        await RunTicks(5);
        Assert.That(await CountBuilt(Wall), Is.EqualTo(0), "an empty RCD must not build a wall");

        await SetRcdProto(rcd, RcdSettingWindow);
        await Interact(null, pNorth, awaitDoAfters: false);
        await RunTicks(5);
        Assert.That(await CountBuilt(Window), Is.EqualTo(1), "an empty RCD must still build a free window");
    }

    private async Task<int> CountBuilt(EntProtoId protoId)
    {
        var count = 0;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var metaDataComponent))
            {
                if (metaDataComponent.EntityPrototype?.ID == protoId.Id)
                    count++;
            }
        });
        return count;
    }
}
