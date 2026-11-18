#nullable enable
using Content.Client._KS14.Standing;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Standing;

/// <summary>
///     Barbed wire draws over someone lying in it and under someone standing in it: a mob is drawn lower while it is
///         down, on a client that only receives its state, and put back when it stands.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDownedDrawDepthSystem))]
public sealed class KsDownedDrawDepthTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId BarbedWire = "KsBarbedWire";

    [Test]
    public async Task BarbedWireDrawsOverTheDownedOnly()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid humanUid = default;
        EntityUid wireUid = default;
        await Server.WaitPost(() =>
        {
            wireUid = SSpawnAtPosition(BarbedWire, testMap.GridCoords);
            humanUid = SSpawnAtPosition(Human, testMap.GridCoords);
        });
        await Pair.RunTicksSync(10);

        var (standingDepth, wireDepth) = await ReadDepths(humanUid, wireUid);
        Assert.That(standingDepth, Is.GreaterThan(wireDepth), "someone standing in barbed wire should draw over it");

        await Server.WaitPost(() => Server.System<StandingStateSystem>().Down(humanUid, playSound: false, dropHeldItems: false, force: true));
        await Pair.RunTicksSync(10);
        var (downedDepth, _) = await ReadDepths(humanUid, wireUid);
        Assert.That(downedDepth, Is.LessThan(wireDepth), "someone lying in barbed wire should draw under it");

        await Server.WaitPost(() => Server.System<StandingStateSystem>().Stand(humanUid, force: true));
        await Pair.RunTicksSync(10);
        var (stoodDepth, _) = await ReadDepths(humanUid, wireUid);
        Assert.That(stoodDepth, Is.EqualTo(standingDepth), "standing up should put back the depth they had");
    }

    private async Task<(int Mob, int Wire)> ReadDepths(EntityUid serverMobUid, EntityUid serverWireUid)
    {
        var depths = (0, 0);
        await Client.WaitPost(() =>
        {
            depths = (CComp<SpriteComponent>(ToClientUid(serverMobUid)).DrawDepth, CComp<SpriteComponent>(ToClientUid(serverWireUid)).DrawDepth);
        });

        return depths;
    }
}
