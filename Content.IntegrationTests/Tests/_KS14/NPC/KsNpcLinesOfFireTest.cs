#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Lines of fire (<c>NpcGunfireSystem.LinesOfFire.cs</c>): a shot leaves its shooter a line of fire, from where it fired
///         along where it aimed to the first wall, friendly or hostile as seen by whoever asks, for a second.
/// </summary>
public sealed class KsNpcLinesOfFireTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const float Clearance = 0.8f;

    /// <summary>
    ///     A Syndicate shooter at 0,0 fires east at 6,0. A Syndicate operative asks; so does a NanoTrasen one.
    /// </summary>
    [Test]
    public async Task TestALineOfFireIsSeenForWhatItIs()
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var gunfireSystem = entManager.System<NpcGunfireSystem>();
        EntityUid gridUid = default, shooterUid = default, friendUid = default, hostileUid = default;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-4, -6), new Vector2i(12, 6)).Owner;
            shooterUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            friendUid = SpawnAt(entManager, SyndicateMob, gridUid, 4, 4);
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 4, -4);
        });

        await Pair.RunTicksSync(5);

        MapCoordinates At(float x, float y) => new(new Vector2(x, y), map.MapId);

        await Pair.Server.WaitAssertion(() =>
        {
            var ev = new GunShotEvent(shooterUid,
                new List<(EntityUid?, IShootable)>(),
                new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)),
                new EntityCoordinates(gridUid, new Vector2(6.5f, 0.5f)));
            entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);

            Assert.Multiple(() =>
            {
                // Across it, beyond what it was aimed at: a miss flies on.
                Assert.That(gunfireSystem.CrossesFire(friendUid, At(8.5f, 3f), At(8.5f, -2f), NpcFireSide.Friendly, Clearance),
                    "across a friend's fire, past its aim");
                Assert.That(gunfireSystem.CrossesFire(friendUid, At(8.5f, 3f), At(8.5f, -2f), NpcFireSide.Hostile, Clearance), Is.False,
                    "a friend's fire is not hostile");
                Assert.That(gunfireSystem.CrossesFire(hostileUid, At(8.5f, 3f), At(8.5f, -2f), NpcFireSide.Hostile, Clearance),
                    "to the other side, it is");

                // Alongside it, and behind the shooter.
                Assert.That(gunfireSystem.CrossesFire(friendUid, At(2f, 4f), At(8f, 4f), NpcFireSide.Any, Clearance), Is.False,
                    "alongside, clear of it");
                Assert.That(gunfireSystem.CrossesFire(friendUid, At(-2.5f, 3f), At(-2.5f, -2f), NpcFireSide.Any, Clearance), Is.False,
                    "behind the shooter");

                // Stood in it, or not.
                Assert.That(gunfireSystem.IsUnderFire(friendUid, NpcFireSide.Any, Clearance), Is.False, "the friend is off to the side");
                Assert.That(gunfireSystem.CrossesFire(shooterUid, At(3f, 3f), At(3f, -2f), NpcFireSide.Any, Clearance), Is.False,
                    "its own fire never counts");
            });
        });

        // Gone quiet: a second later, nobody is firing there.
        await RunSeconds(1.2f);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(gunfireSystem.CrossesFire(friendUid, At(8.5f, 3f), At(8.5f, -2f), NpcFireSide.Any, Clearance), Is.False,
                "a line of fire lasts only as long as the firing");
        });
    }
}
