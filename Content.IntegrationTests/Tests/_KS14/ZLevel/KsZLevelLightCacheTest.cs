using System.Numerics;
using Content.Client._KS14.ZLevel.Light;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

public sealed class KsZLevelLightCacheTest : KsZLevelTestBase
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task SealedFloorReusesSearchAndMovementRechecksIt()
    {
        var stack = await CreateStack();
        await Server.WaitPost(() =>
        {
            var map = SEntMan.System<SharedMapSystem>();
            var tile = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
                map.SetTile(stack.UpperGrid.Owner, stack.UpperGrid.Comp, new Vector2i(x, y), tile);
        });
        await RunTicksSync(5);
        await OverrideCVar(Side.Client, KsCCVars.ZLevelLightLeakEnabled, true);
        await OverrideCVar(Side.Client, KsCCVars.ZLevelLightLeakMode, "StandIns");
        await OverrideCVar(Side.Client, KsCCVars.ZLevelLightLeakSearchInterval, 10f);
        await OverrideCVar(Side.Server, KsCCVars.ZLevelLightLeakHeight, 0.1f);
        var grid = ToClientUid(stack.UpperGrid.Owner);
        await Client.WaitAssertion(() =>
        {
            var lights = CEntMan.System<PointLightSystem>();
            var leaks = CEntMan.System<KsZLevelLightLeakSystem>();
            var uid = CEntMan.SpawnEntity(null, new EntityCoordinates(grid, 0.5f, 0.5f));
            var light = CEntMan.EnsureComponent<PointLightComponent>(uid);
            lights.SetRadius(uid, 3f, light);
            leaks.FrameUpdate(0f);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < 120; frame++)
                leaks.FrameUpdate(0f);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.Out.WriteLine($"Sealed floor, 120 lighting updates: {allocated} bytes.");

            var cache = CEntMan.GetComponent<KsZLevelLightLeakComponent>(uid);
            Assert.That(cache.Holes, Is.Empty);
            leaks.FrameUpdate(0f);
            Assert.That(CEntMan.GetComponent<KsZLevelLightLeakComponent>(uid), Is.SameAs(cache));

            CEntMan.System<SharedTransformSystem>().SetLocalPosition(uid, new Vector2(9.5f, 0.5f));
            leaks.FrameUpdate(0f);
            Assert.That(cache.Holes, Is.Not.Empty);
            Assert.That(cache.StandIns, Is.Not.Empty);

            CEntMan.System<SharedTransformSystem>().SetLocalPosition(uid, new Vector2(0.5f, 0.5f));
            leaks.FrameUpdate(0f);
            Assert.That(cache.Holes, Is.Empty);
            Assert.That(cache.StandIns, Is.Empty);
            lights.SetEnabled(uid, false, light);
            leaks.FrameUpdate(0f);
            Assert.That(CEntMan.HasComponent<KsZLevelLightLeakComponent>(uid), Is.False);
        });
    }
}
