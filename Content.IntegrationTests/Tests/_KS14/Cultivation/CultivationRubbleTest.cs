using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Body;
using Content.Shared.Stacks;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Body arts break things into rubble, never into nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(CultivationRubbleSystem))]
public sealed class CultivationRubbleTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private int CountNear(MapCoordinates centre, string prototypeId, float range = 1.5f)
    {
        var lookupSystem = SEntMan.System<EntityLookupSystem>();
        var total = 0;
        foreach (var uid in lookupSystem.GetEntitiesInRange(centre, range))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID != prototypeId)
                continue;

            total += SEntMan.TryGetComponent<StackComponent>(uid, out var stackComponent) ? stackComponent.Count : 1;
        }

        return total;
    }

    [Test]
    public async Task TestBrokenWallLeavesGirderAndSomePlating()
    {
        var map = await Pair.CreateTestMap();
        EntityUid wallUid = default;
        MapCoordinates wallMap = default;

        await Server.WaitAssertion(() =>
        {
            wallUid = SEntMan.SpawnEntity("WallSolid", map.GridCoords.Offset(new Vector2(2f, 0f)));
            wallMap = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(wallUid);
            SEntMan.System<CultivationRubbleSystem>().BreakWall(wallUid, devastate: false, userUid: null);
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(wallUid), Is.True, "The wall is still standing.");
                Assert.That(CountNear(wallMap, "Girder", 0.5f), Is.EqualTo(1), "The wall didn't come down to its girder.");
                // The plating is two sheets of steel: half of it is left lying about.
                Assert.That(CountNear(wallMap, "SheetSteel1"), Is.EqualTo(1), "The wall's plating wasn't strewn about.");
            }
        });
    }

    [Test]
    public async Task TestDevastatedWallLeavesOnlyScrap()
    {
        var map = await Pair.CreateTestMap();
        EntityUid wallUid = default;
        MapCoordinates wallMap = default;

        await Server.WaitAssertion(() =>
        {
            wallUid = SEntMan.SpawnEntity("WallSolid", map.GridCoords.Offset(new Vector2(2f, 0f)));
            wallMap = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(wallUid);
            SEntMan.System<CultivationRubbleSystem>().BreakWall(wallUid, devastate: true, userUid: null);
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(wallUid), Is.True, "The wall is still standing.");
                Assert.That(CountNear(wallMap, "Girder", 0.5f), Is.Zero, "A devastated wall kept its girder.");
                // Four sheets went into girder and plating; half of them are left.
                Assert.That(CountNear(wallMap, "SheetSteel1"), Is.EqualTo(2), "A devastated wall left nothing behind.");
            }
        });
    }

    [Test]
    public async Task TestSmashedWindowLeavesShards()
    {
        var map = await Pair.CreateTestMap();
        EntityUid windowUid = default;
        MapCoordinates windowMap = default;

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            windowUid = SEntMan.SpawnEntity("Window", map.GridCoords.Offset(new Vector2(2f, 0f)));
            windowMap = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(windowUid);
            // Far past the window's overkill threshold, which would otherwise make it vanish without a shard.
            SEntMan.System<BodyArtSystem>().Smash(windowMap, 0.4f, 5000f, wallTier: 2, userUid: userUid);
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(windowUid), Is.True, "The window survived.");
                Assert.That(CountNear(windowMap, "ShardGlass"), Is.GreaterThan(0), "The window vanished without leaving shards.");
            }
        });
    }

    [Test]
    public async Task TestStompLeavesCrater()
    {
        var map = await Pair.CreateTestMap();
        MapCoordinates centre = default;

        await Server.WaitAssertion(() =>
        {
            centre = SEntMan.System<SharedTransformSystem>().ToMapCoordinates(map.GridCoords);
            SEntMan.System<BodyArtSystem>().Impact(centre, 2f, strength: 2f);
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
            Assert.That(CountNear(centre, "KsFxCraterBig", 1.5f), Is.EqualTo(1), "The blow left no crater."));
    }
}
