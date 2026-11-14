#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Pinning;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcPinnedDownSystem"/>: an NPC standing in a hostile's line of fire has its meter raised; one that is not, or
///         that shrugs it off (gain 0), or that is hit by a friend's fire, has not. What the operative HTN does with the meter is
///         <c>KsOperativeHtnRootTest.TestPinnedDownOperativeGoesToCover</c> and its neighbours.
/// </summary>
public sealed class KsNpcPinnedDownTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string PinnableMob = "KsPinnedTestMobPinnable";
    private const string EliteMob = "KsPinnedTestMobElite";
    private const string MeterId = "KsPinnedTestMeter";

    private const float Threshold = 60f;

    // The operative HTN's own meter is in the private submodule, so this has one of its own.
    [TestPrototypes]
    private const string Prototypes = @"
- type: npcMeter
  id: KsPinnedTestMeter
  max: 100
  decayPerSecond: 20

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsPinnedTestMobPinnable
  components:
  - type: NpcPinnable
    meter: KsPinnedTestMeter

- type: entity
  parent: KsPinnedTestMobPinnable
  id: KsPinnedTestMobElite
  components:
  - type: NpcPinnable
    gain: 0
";

    /// <summary>
    ///     A NanoTrasen shooter at 0,0 fires east, along y=0.5, at a pinnable Syndicate at 6.5,0.5. Beside it, 4 tiles off the line,
    ///         a second Syndicate; in the line but a shrugging elite; and a Syndicate friend of the target that does the shooting
    ///         in the second case.
    /// </summary>
    [Test]
    public async Task TestUnderHostileFireRaisesTheMeterAndAnEliteShrugsItOff()
    {
        var entManager = Pair.Server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = Pair.Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var meterSystem = entManager.System<NpcMeterSystem>();
        var pinnedDownSystem = entManager.System<NpcPinnedDownSystem>();
        EntityUid gridUid = default, shooterUid = default, friendUid = default, targetUid = default, eliteUid = default, asideUid = default;

        await Pair.Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-4, -8), new Vector2i(14, 8)).Owner;
            shooterUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 0, 0);
            targetUid = SpawnAt(entManager, PinnableMob, gridUid, 6, 0);
            eliteUid = SpawnAt(entManager, EliteMob, gridUid, 9, 0);
            asideUid = SpawnAt(entManager, PinnableMob, gridUid, 6, 5);
            friendUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, -4);
        });

        await Pair.RunTicksSync(5);

        void Fire(EntityUid fromUid, float fromY, float toX, float toY)
        {
            var ev = new GunShotEvent(fromUid,
                new List<(EntityUid?, IShootable)>(),
                new EntityCoordinates(gridUid, new Vector2(0.5f, fromY)),
                new EntityCoordinates(gridUid, new Vector2(toX, toY)));
            entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
        }

        float Meter(EntityUid uid) => meterSystem.GetValue(uid, MeterId);

        await Pair.Server.WaitAssertion(() =>
        {
            // One round at them: a check or two finds it, and that is not enough to pin anyone down.
            Fire(shooterUid, 0.5f, 6.5f, 0.5f);
            pinnedDownSystem.CheckNow((targetUid, entManager.GetComponent<NpcPinnableComponent>(targetUid)));
            pinnedDownSystem.CheckNow((targetUid, entManager.GetComponent<NpcPinnableComponent>(targetUid)));
            pinnedDownSystem.CheckNow((eliteUid, entManager.GetComponent<NpcPinnableComponent>(eliteUid)));
            pinnedDownSystem.CheckNow((asideUid, entManager.GetComponent<NpcPinnableComponent>(asideUid)));

            Assert.Multiple(() =>
            {
                Assert.That(Meter(targetUid), Is.GreaterThan(0f), "in the line of fire, the meter rises");
                Assert.That(Meter(targetUid), Is.LessThan(Threshold), "but one round is not being pinned down");
                Assert.That(Meter(eliteUid), Is.Zero, "an elite, in the same line, shrugs it off");
                Assert.That(Meter(asideUid), Is.Zero, "someone off to the side is not under fire");
            });
        });

        await Pair.Server.WaitAssertion(() =>
        {
            // A burst: shot after shot, a check each time.
            for (var i = 0; i < 4; i++)
            {
                Fire(shooterUid, 0.5f, 6.5f, 0.5f);
                pinnedDownSystem.CheckNow((targetUid, entManager.GetComponent<NpcPinnableComponent>(targetUid)));
            }

            Assert.That(Meter(targetUid), Is.GreaterThanOrEqualTo(Threshold), "a burst pins them down");
        });

        // Fired by a friend, along a line of its own through the target: not hostile fire, so not pinning.
        await Pair.Server.WaitAssertion(() =>
        {
            meterSystem.Set(asideUid, MeterId, 0f);
            Fire(friendUid, -3.5f, 6.5f, 5.5f);
            pinnedDownSystem.CheckNow((asideUid, entManager.GetComponent<NpcPinnableComponent>(asideUid)));
            Assert.That(Meter(asideUid), Is.Zero, "a friend's fire is no reason to take cover");
        });
    }
}
