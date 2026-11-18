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
///     Floor clutter at BelowMobs, such as barbed wire, draws over someone lying in it and under someone standing in it: a mob is drawn lower while it is
///         down, on a client that only receives its state, and put back when it stands.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDownedDrawDepthSystem))]
public sealed class KsDownedDrawDepthTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly EntProtoId _mob = "KsDownedDrawDepthTestMob";
    private readonly EntProtoId _wire = "KsDownedDrawDepthTestWire";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsDownedDrawDepthTestMob
  components:
  - type: Sprite
    drawdepth: Mobs
  - type: Appearance
  - type: StandingState

# Lies on the floor under standing mobs, as barbed wire does.
- type: entity
  id: KsDownedDrawDepthTestWire
  components:
  - type: Sprite
    drawdepth: BelowMobs
";

    [Test]
    public async Task WireDrawsOverTheDownedOnly()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid mobUid = default;
        EntityUid wireUid = default;
        await Server.WaitPost(() =>
        {
            wireUid = SSpawnAtPosition(_wire, testMap.GridCoords);
            mobUid = SSpawnAtPosition(_mob, testMap.GridCoords);
        });
        await Pair.RunTicksSync(10);

        var (standingDepth, wireDepth) = await ReadDepths(mobUid, wireUid);
        Assert.That(standingDepth, Is.GreaterThan(wireDepth), "someone standing in wire should draw over it");

        await Server.WaitPost(() => Server.System<StandingStateSystem>().Down(mobUid, playSound: false, dropHeldItems: false, force: true));
        await Pair.RunTicksSync(10);
        var (downedDepth, _) = await ReadDepths(mobUid, wireUid);
        Assert.That(downedDepth, Is.LessThan(wireDepth), "someone lying in wire should draw under it");

        await Server.WaitPost(() => Server.System<StandingStateSystem>().Stand(mobUid, force: true));
        await Pair.RunTicksSync(10);
        var (stoodDepth, _) = await ReadDepths(mobUid, wireUid);
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
