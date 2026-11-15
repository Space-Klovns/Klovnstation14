#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Squad;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.LockOn;
using Content.Shared.Damage.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Which target a planning turret keeps (KsTargetLockOn.KeepTargetUntilLost), and whom the fire-lane check holds fire
///         for (NpcFireLane.Mode).
/// </summary>
public sealed class KsNpcTurretTargetingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string StickyTurret = "KsTurretTargetingTestSticky";
    private const string FickleTurret = "KsTurretTargetingTestFickle";
    private const string FireThroughMob = "KsTurretTargetingTestMobFireThrough";
    private const string HoldForNeutralsMob = "KsTurretTargetingTestMobHoldForNeutrals";

    /// <summary>
    ///     Neither friendly nor hostile to the Syndicate.
    /// </summary>
    private static readonly ProtoId<NpcFactionPrototype> NeutralFaction = "Passive";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WeaponTurretSyndicate
  id: KsTurretTargetingTestFickle
  components:
  - type: KsTargetLockOn
    lockOnTime: 2

- type: entity
  parent: KsTurretTargetingTestFickle
  id: KsTurretTargetingTestSticky
  components:
  - type: KsTargetLockOn
    keepTargetUntilLost: true

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsTurretTargetingTestMobFireThrough
  components:
  - type: NpcFireLane
    mode: FireThrough

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsTurretTargetingTestMobHoldForNeutrals
  components:
  - type: NpcFireLane
    mode: HoldForFriendliesAndNeutrals
";

    /// <summary>
    ///     The turrets' query is upstream's gun targets with one more consideration, by inheritance
    ///         (<c>AlwaysPushInheritance</c> on considerations), not a copy that would fall behind it.
    /// </summary>
    [Test]
    public void TestTurretQueryIsGunTargetsAndOneMore()
    {
        var turretQueryId = new ProtoId<Content.Server.NPC.Queries.UtilityQueryPrototype>("KsTurretGunTargets");
        var gunQueryId = new ProtoId<Content.Server.NPC.Queries.UtilityQueryPrototype>("NearbyGunTargets");
        var turretQuery = Server.ProtoMan.Index(turretQueryId);
        var gunQuery = Server.ProtoMan.Index(gunQueryId);

        Assert.Multiple(() =>
        {
            Assert.That(turretQuery.Query, Has.Count.EqualTo(gunQuery.Query.Count), "the same query");
            Assert.That(turretQuery.Considerations, Has.Count.EqualTo(gunQuery.Considerations.Count + 1), "every consideration, and one more");
            Assert.That(turretQuery.Considerations, Has.Exactly(1).TypeOf<Content.Server._KS14.NPC.Queries.Considerations.KsTargetInLineOfFireCon>(),
                "its own among them");
        });
    }

    /// <summary>
    ///     Two hostiles take turns at being the closest. A turret that keeps its target finishes its lock on the first
    ///         and holds it while it is in sight, and moves to the other only once a wall hides it. Without the switch, the
    ///         control, the lock keeps changing hands and never finishes.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestKeepTargetUntilLost(bool keepTarget)
    {
        var (entManager, gridUid) = await SetUpOpenGrid();
        var lockSystem = entManager.System<SharedKsTargetLockOnSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var tickRate = (float)Pair.Server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid turretUid = default, firstUid = default, secondUid = default;
        var origin = Vector2.Zero;

        await Pair.Server.WaitPost(() =>
        {
            turretUid = SpawnAt(entManager, keepTarget ? StickyTurret : FickleTurret, gridUid, 0, 0);
            firstUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);
            secondUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 0, -3);
            var godmodeSystem = entManager.System<SharedGodmodeSystem>();
            godmodeSystem.EnableGodmode(firstUid);
            godmodeSystem.EnableGodmode(secondUid);
            origin = transformSystem.GetWorldPosition(turretUid);
        });

        // They swap places every half second, so each is the closest in turn.
        var targets = new List<EntityUid>();
        var finishedFor = default(EntityUid);
        var swapTicks = (int)(0.5f * tickRate);

        for (var tick = 0; tick < (int)(6f * tickRate); tick++)
        {
            var firstNear = tick / swapTicks % 2 == 0;

            await Pair.Server.WaitPost(() =>
            {
                transformSystem.SetWorldPosition(firstUid, origin + new Vector2(firstNear ? 3f : 4.5f, 0f));
                transformSystem.SetWorldPosition(secondUid, origin + new Vector2(0f, firstNear ? -4.5f : -3f));

                var lockOn = entManager.GetComponent<KsTargetLockOnComponent>(turretUid);
                if (lockOn.Target is { } lockTarget)
                {
                    if (targets.Count == 0 || targets[^1] != lockTarget)
                        targets.Add(lockTarget);

                    if (lockSystem.IsLocked(turretUid, lockTarget))
                        finishedFor = lockTarget;
                }
            });
            await Pair.RunTicksSync(1);
        }

        if (!keepTarget)
        {
            Assert.That(targets, Has.Count.GreaterThan(2), "control: the lock should have been changing hands");
            Assert.That(finishedFor, Is.EqualTo(default(EntityUid)), "control: no lock should have finished");
            return;
        }

        Assert.That(targets, Has.Count.EqualTo(1), "a sticky turret should lock one target and keep it");
        Assert.That(finishedFor, Is.EqualTo(targets[0]), "its lock should have finished");

        // The kept target is walled off: the turret turns to the other.
        var keptUid = targets[0];
        await Pair.Server.WaitPost(() =>
        {
            var keptPosition = transformSystem.GetWorldPosition(keptUid) - origin;
            var wallPosition = origin + keptPosition.Normalized() * 1.5f;
            entManager.SpawnEntity(TestWall, new EntityCoordinates(gridUid, wallPosition));
        });

        // However long the turret takes to give the first up and plan again, up to a cap.
        EntityUid? newTarget = keptUid;
        for (var tick = 0; tick < (int)(15f * tickRate) && (newTarget == null || newTarget == keptUid); tick++)
        {
            await Pair.RunTicksSync(1);
            await Pair.Server.WaitPost(() => newTarget = entManager.GetComponent<KsTargetLockOnComponent>(turretUid).Target);
        }

        Assert.That(newTarget, Is.EqualTo(keptUid == firstUid ? secondUid : firstUid), "once the first is out of sight it should take the other");
    }

    /// <summary>
    ///     The fire-lane check itself, asked of a mob shooter with no HTN, with a friend of its, or a bystander (neither
    ///         friendly nor hostile to it), in the line of fire, for the aim and for a round as it goes. Each
    ///         <see cref="NpcFireLaneMode"/>: <c>FireThrough</c> holds for neither; <c>HoldForFriendlies</c>, which is also
    ///         what a shooter with no <c>NpcFireLane</c> does, holds for the friend only; <c>HoldForFriendliesAndNeutrals</c>
    ///         for both. Live turrets are <c>KsNpcTurretSightTest.TestFireLaneMode</c>.
    /// </summary>
    [TestCase(NpcFireLaneMode.FireThrough, false)]
    [TestCase(NpcFireLaneMode.FireThrough, true)]
    [TestCase(NpcFireLaneMode.HoldForFriendlies, false)]
    [TestCase(NpcFireLaneMode.HoldForFriendlies, true)]
    [TestCase(NpcFireLaneMode.HoldForFriendliesAndNeutrals, false)]
    [TestCase(NpcFireLaneMode.HoldForFriendliesAndNeutrals, true)]
    public async Task TestFireLaneModes(NpcFireLaneMode mode, bool neutralInTheWay)
    {
        await OverrideCVar(Side.Server, KsCCVars.NpcSquadFireLanes, true);

        var (entManager, gridUid) = await SetUpOpenGrid();
        var fireLaneSystem = entManager.System<NpcSquadFireLaneSystem>();
        var factionSystem = entManager.System<NpcFactionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var clear = mode == NpcFireLaneMode.FireThrough ||
            mode == NpcFireLaneMode.HoldForFriendlies && neutralInTheWay;

        await Pair.Server.WaitPost(() =>
        {
            var shooter = mode switch
            {
                NpcFireLaneMode.FireThrough => FireThroughMob,
                NpcFireLaneMode.HoldForFriendliesAndNeutrals => HoldForNeutralsMob,
                _ => SyndicateMob,
            };
            var shooterUid = SpawnAt(entManager, shooter, gridUid, 0, 0);
            var inTheWayUid = SpawnAt(entManager, SyndicateMob, gridUid, 3, 0);

            if (neutralInTheWay)
            {
                factionSystem.ClearFactions(inTheWayUid);
                factionSystem.AddFaction(inTheWayUid, NeutralFaction);
            }

            Assert.Multiple(() =>
            {
                Assert.That(fireLaneSystem.GetFireLaneMode(shooterUid), Is.EqualTo(mode));
                Assert.That(factionSystem.IsEntityFriendly(shooterUid, inTheWayUid), Is.EqualTo(!neutralInTheWay));
                Assert.That(factionSystem.IsEntityHostile(shooterUid, inTheWayUid), Is.False);
            });

            var from = transformSystem.GetMapCoordinates(shooterUid);
            var aim = new MapCoordinates(from.Position + new Vector2(6f, 0f), from.MapId);
            var what = neutralInTheWay ? "a bystander" : "a friend";

            Assert.Multiple(() =>
            {
                Assert.That(fireLaneSystem.TryGetClearShot(shooterUid, from, aim, 0.35f, out _), Is.EqualTo(clear),
                    $"{mode}, {what} in the line: clear shot");
                Assert.That(fireLaneSystem.IsShotClear(shooterUid, from, aim), Is.EqualTo(clear),
                    $"{mode}, {what} in the line: round as it goes");
            });
        });
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpOpenGrid()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(12, 8)).Owner;
        });

        return (entManager, gridUid);
    }
}
