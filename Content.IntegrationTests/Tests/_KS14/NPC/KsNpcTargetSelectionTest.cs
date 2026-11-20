#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Targeting;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Queries;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     How NPCs choose between targets: by the priority a target is given (<see cref="KsTargetPriorityCon"/>), and
///         away from one enough of their side are already fighting (<see cref="TargetCrowdedCon"/>).
/// </summary>
[TestFixture]
[TestOf(typeof(KsTargetPriorityCon))]
[TestOf(typeof(TargetCrowdedCon))]
[TestOf(typeof(NpcTargetTrackingSystem))]
public sealed class KsNpcTargetSelectionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Ids that exist only in the test prototypes below.
    private const string PriorityQuery = "KsTestTargetSelectionPriority";
    private const string CrowdedQuery = "KsTestTargetSelectionCrowded";

    // The private module's operative target query, and the public defender turret: run only where both are present.
    private readonly ProtoId<UtilityQueryPrototype> _operativeGunTargets = "KsOperativeNearbyGunTargets";
    private readonly EntProtoId _defenderTurret = "KsWeaponTurretNanoTrasenDisposable";
    private readonly EntProtoId _fieldCommanderTank = "KsFcMobTank";
    private readonly ProtoId<UtilityQueryPrototype> _turretGunTargets = "KsTurretGunTargets";

    [TestPrototypes]
    private const string Prototypes = @"
- type: utilityQuery
  id: KsTestTargetSelectionPriority
  query:
  - !type:ComponentQuery
    components:
    - type: NpcFactionMember
  considerations:
  - !type:TargetDistanceCon
    curve: !type:QuadraticCurve
      slope: -1
      exponent: 1
      yOffset: 1
      xOffset: 0
  - !type:KsTargetPriorityCon
    curve: !type:QuadraticCurve
      slope: 1
      exponent: 1
      yOffset: 0
      xOffset: 0

- type: utilityQuery
  id: KsTestTargetSelectionCrowded
  query:
  - !type:NearbyHostilesQuery
  considerations:
  - !type:TargetDistanceCon
    curve: !type:QuadraticCurve
      slope: -1
      exponent: 1
      yOffset: 1
      xOffset: 0
  - !type:TargetCrowdedCon
    max: 2
    curve: !type:BoolCurve
      lowScore: 0.1
";

    private async Task<EntityUid> MakeFloor()
    {
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;
        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-8, -8), new Vector2i(8, 8)).Owner;
        });
        return gridUid;
    }

    private Dictionary<EntityUid, float> Scores(EntityUid ownerUid, string query)
    {
        var blackboard = new NPCBlackboard();
        blackboard.SetValue(NPCBlackboard.Owner, ownerUid);
        return SEntMan.System<NPCUtilitySystem>().GetEntities(blackboard, query, bestOnly: false).Entities
            .Where(pair => pair.Key != ownerUid)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static EntityUid Best(Dictionary<EntityUid, float> scores)
    {
        return scores.MaxBy(pair => pair.Value).Key;
    }

    /// <summary>
    ///     A farther target with a priority beats a nearer one without; take the priority away and the nearer wins
    ///         again, as everything without one scores alike.
    /// </summary>
    [Test]
    public async Task PriorityLiftsATargetAboveNearerOnes()
    {
        var gridUid = await MakeFloor();

        await Server.WaitAssertion(() =>
        {
            var ownerUid = SpawnAt(SEntMan, LonerMob, gridUid, 0, 0);
            var nearUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 2, 0);
            var farUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 5, 0);

            Assert.That(Best(Scores(ownerUid, PriorityQuery)), Is.EqualTo(nearUid), "without priorities the nearer target should win");

            SEntMan.AddComponent<KsNpcTargetPriorityComponent>(farUid).Priority = 1f;
            Assert.That(Best(Scores(ownerUid, PriorityQuery)), Is.EqualTo(farUid), "a target with a priority should beat a nearer one without");

            SEntMan.RemoveComponent<KsNpcTargetPriorityComponent>(farUid);
            Assert.That(Best(Scores(ownerUid, PriorityQuery)), Is.EqualTo(nearUid), "with its priority gone the nearer target should win again");
        });
    }

    /// <summary>
    ///     With two others of its side ranked ahead of it on the nearer hostile, an NPC takes the farther one; with only
    ///         one, it stays on the nearer, its own fighting of it not counted; and those ranked behind it never push it
    ///         off the target it is on.
    /// </summary>
    [Test]
    public async Task CrowdedTargetsArePutBehindOthers()
    {
        var gridUid = await MakeFloor();

        await Server.WaitAssertion(() =>
        {
            var trackingSystem = SEntMan.System<NpcTargetTrackingSystem>();
            // Spawned first, so ranked ahead of the owner.
            var allyUid = SpawnAt(SEntMan, LonerMob, gridUid, 0, 3);
            var secondAllyUid = SpawnAt(SEntMan, LonerMob, gridUid, 0, -3);
            var ownerUid = SpawnAt(SEntMan, LonerMob, gridUid, 0, 0);
            var nearUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 2, 0);
            var farUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, -5, 0);
            var laterAllyUid = SpawnAt(SEntMan, LonerMob, gridUid, -1, 3);
            var secondLaterAllyUid = SpawnAt(SEntMan, LonerMob, gridUid, -1, -3);

            SEntMan.EnsureComponent<NPCRangedCombatComponent>(ownerUid).Target = nearUid;
            SEntMan.EnsureComponent<NPCRangedCombatComponent>(allyUid).Target = nearUid;
            trackingSystem.UpdateNow();
            Assert.That(trackingSystem.GetEngagingAllies(ownerUid, nearUid), Is.EqualTo(1), "the owner should not count itself");
            Assert.That(Best(Scores(ownerUid, CrowdedQuery)), Is.EqualTo(nearUid), "one other on it is not a crowd");

            SEntMan.EnsureComponent<NPCMeleeCombatComponent>(secondAllyUid).Target = nearUid;
            trackingSystem.UpdateNow();
            Assert.That(Best(Scores(ownerUid, CrowdedQuery)), Is.EqualTo(farUid), "with two others ahead of it on the nearer, the farther should be taken");

            SEntMan.RemoveComponent<NPCMeleeCombatComponent>(secondAllyUid);
            SEntMan.RemoveComponent<NPCRangedCombatComponent>(allyUid);
            SEntMan.EnsureComponent<NPCRangedCombatComponent>(laterAllyUid).Target = nearUid;
            SEntMan.EnsureComponent<NPCRangedCombatComponent>(secondLaterAllyUid).Target = nearUid;
            trackingSystem.UpdateNow();
            Assert.That(Best(Scores(ownerUid, CrowdedQuery)), Is.EqualTo(nearUid), "those ranked behind it should not push it off its target");
            Assert.That(Best(Scores(secondLaterAllyUid, CrowdedQuery)), Is.EqualTo(farUid), "the last ranked of three should move off");

            SEntMan.RemoveComponent<NPCRangedCombatComponent>(laterAllyUid);
            SEntMan.RemoveComponent<NPCRangedCombatComponent>(secondLaterAllyUid);
            SEntMan.RemoveComponent<NPCRangedCombatComponent>(ownerUid);
            trackingSystem.UpdateNow();
            Assert.That(trackingSystem.GetEngagingAllies(allyUid, nearUid), Is.Zero, "the count should follow those who stopped");
            Assert.That(SEntMan.HasComponent<NpcTargetedComponent>(nearUid), Is.False, "a target nobody fights should carry no count");
            Assert.That(SEntMan.HasComponent<NpcTargetedComponent>(farUid), Is.False);
        });
    }

    /// <summary>
    ///     Four NPCs, two hostiles, picking their targets over and over in a changing order while the count is taken
    ///         less often, as planning and the count really run: they split and stay split, rather than all leaving the
    ///         nearer hostile for the farther together and all coming back.
    /// </summary>
    [Test]
    public async Task ASquadSplitsOverTwoTargetsAndStaysSplit()
    {
        var gridUid = await MakeFloor();

        await Server.WaitAssertion(() =>
        {
            var trackingSystem = SEntMan.System<NpcTargetTrackingSystem>();
            var npcs = new List<EntityUid>();
            for (var i = 0; i < 4; i++)
                npcs.Add(SpawnAt(SEntMan, LonerMob, gridUid, 0, i - 2));

            var nearUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 3, 0);
            var farUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, -4, 0);

            // Everyone starts on the nearer: what nobody spreading out would do.
            foreach (var npcUid in npcs)
                SEntMan.EnsureComponent<NPCRangedCombatComponent>(npcUid).Target = nearUid;

            trackingSystem.UpdateNow();

            const int rounds = 40;
            var switchesLate = 0;
            for (var round = 0; round < rounds; round++)
            {
                // A different order each round, as their plans fall on different ticks.
                for (var i = 0; i < npcs.Count; i++)
                {
                    var npcUid = npcs[(i + round) % npcs.Count];
                    var combatComponent = SEntMan.GetComponent<NPCRangedCombatComponent>(npcUid);
                    var best = Best(Scores(npcUid, CrowdedQuery));
                    if (best != combatComponent.Target && round >= rounds / 2)
                        switchesLate++;

                    combatComponent.Target = best;
                }

                // The count, less often than they plan.
                if (round % 3 == 2)
                    trackingSystem.UpdateNow();
            }

            var onNear = npcs.Count(npcUid => SEntMan.GetComponent<NPCRangedCombatComponent>(npcUid).Target == nearUid);
            Assert.That(switchesLate, Is.Zero, "once split, nobody should keep switching targets");
            Assert.That(onNear, Is.EqualTo(2), "with two allowed on a target, two should stay on the nearer and two take the farther");
        });
    }

    /// <summary>
    ///     Content: a Field Commander operative, in a squad or on its own, picks a defender turret over a healthy defender
    ///         just as far off, though the turret, having no health to speak of, used to score far below one. Another
    ///         operative, on the same query, ranks them as though the turret had no priority at all.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task FieldCommanderOperativesPreferDefenderTurrets(bool squadded)
    {
        if (!SProtoMan.HasIndex(_operativeGunTargets))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var gridUid = await MakeFloor();
        EntityUid ownerUid = default, otherUid = default;

        await Server.WaitPost(() =>
        {
            ownerUid = SpawnAt(SEntMan, squadded ? SyndicateMob : LonerMob, gridUid, 0, 0);
            otherUid = SpawnAt(SEntMan, squadded ? SyndicateMob : LonerMob, gridUid, -1, -1);
            SEntMan.EnsureComponent<Content.Server._KS14.FieldCommander.KsFieldCommanderUnitComponent>(ownerUid);
        });

        // For the two to form a squad, or not.
        await Pair.RunTicksSync(150);

        await Server.WaitAssertion(() =>
        {
            var squadSystem = SEntMan.System<Content.Server._KS14.NPC.Squad.NpcSquadSystem>();
            Assert.That(squadSystem.TryGetSquad(ownerUid, out _), Is.EqualTo(squadded), squadded ? "precondition: in a squad" : "precondition: on its own");

            var defenderUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 4, 0);
            var turretUid = SpawnAt(SEntMan, _defenderTurret, gridUid, 0, 4);
            Assert.That(SEntMan.HasComponent<KsNpcTargetPriorityComponent>(turretUid), Is.True, "precondition: the turret has a priority");

            var perceptionSystem = SEntMan.System<NpcPerceptionSystem>();
            var now = Server.ResolveDependency<IGameTiming>().CurTime;
            foreach (var viewerUid in new[] { ownerUid, otherUid })
            {
                foreach (var targetUid in new[] { defenderUid, turretUid })
                {
                    perceptionSystem.SetContact(viewerUid, targetUid, new NpcContact(NpcContactState.Visible,
                        now, now, now, SEntMan.GetComponent<TransformComponent>(targetUid).Coordinates, default, null, Reacted: true));
                }
            }

            var scores = Scores(ownerUid, _operativeGunTargets);
            Assert.That(scores.Keys, Is.EquivalentTo(new[] { defenderUid, turretUid }), "both should be candidates");
            Assert.That(Best(scores), Is.EqualTo(turretUid),
                $"the turret ({scores[turretUid]:0.000}) should be preferred to a healthy defender ({scores[defenderUid]:0.000})");

            // The other is no Field Commander unit: the turret's priority changes nothing for it.
            var withPriority = Scores(otherUid, _operativeGunTargets);
            SEntMan.RemoveComponent<KsNpcTargetPriorityComponent>(turretUid);
            var withoutPriority = Scores(otherUid, _operativeGunTargets);
            Assert.That(withPriority, Is.EquivalentTo(withoutPriority),
                "an operative that is no Field Commander unit should rank them as it would with no priority anywhere");
        });
    }

    /// <summary>
    ///     An NPC on upstream's own target query (carp, xenos, the Syndicate's turrets) scores a target with a priority
    ///         exactly as one without: no priority reaches upstream's queries.
    /// </summary>
    [TestCase("NearbyGunTargets")]
    [TestCase("NearbyMeleeTargets")]
    public async Task UpstreamQueriesIgnorePriority(string query)
    {
        var gridUid = await MakeFloor();

        await Server.WaitAssertion(() =>
        {
            var ownerUid = SpawnAt(SEntMan, LonerMob, gridUid, 0, 0);
            var nearUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 2, 0);
            var farUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 0, 4);

            var without = Scores(ownerUid, query);
            Assert.That(without.Values.Any(score => score > 0f), Is.True, "precondition: the query scores something");

            SEntMan.AddComponent<KsNpcTargetPriorityComponent>(farUid).Priority = 1f;
            var with = Scores(ownerUid, query);
            Assert.That(with, Is.EquivalentTo(without), $"a priority should change nothing in {query}");
            Assert.That(nearUid, Is.Not.EqualTo(farUid));
        });
    }

    /// <summary>
    ///     Content: a Field Commander tank's turret, picking from the query its own HTN uses, goes for a target with a priority
    ///         over a nearer one without, as the Field Commander's infantry do; on the shared turret query, which every other
    ///         turret uses, it ranks them as though there were no priority. (A turret query only takes living targets, so a
    ///         defender turret or a mortar, having no mob state, is never one of them.)
    /// </summary>
    [Test]
    public async Task FieldCommanderTankTurretsWeighPriority()
    {
        if (!SProtoMan.HasIndex(_fieldCommanderTank))
            Assert.Ignore("the Field Commander tanks are in the private _KsModule submodule, which is not present");

        var gridUid = await MakeFloor();

        await Server.WaitAssertion(() =>
        {
            var tankUid = SpawnAt(SEntMan, _fieldCommanderTank, gridUid, 0, 0);
            var parts = SEntMan.System<Content.Server._KS14.WeldedParts.KsWeldedPartsSystem>().GetParts(tankUid);
            Assert.That(parts, Has.Count.EqualTo(1), "precondition: the tank has its turret");
            var turretPartUid = parts[0];

            // The query the turret's own HTN picks its targets from.
            var rootTask = SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(turretPartUid).RootTask.Task;
            var compound = SProtoMan.Index<Content.Server.NPC.HTN.HTNCompoundPrototype>(rootTask);
            var picker = compound.Branches[0].Tasks.OfType<Content.Server.NPC.HTN.PrimitiveTasks.HTNPrimitiveTask>()
                .Select(task => task.Operator).OfType<Content.Server.NPC.HTN.PrimitiveTasks.Operators.UtilityOperator>().First();

            var nearUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 3, 0);
            var priorityUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 0, 6);

            var sharedWithout = Scores(turretPartUid, _turretGunTargets);
            Assert.That(Best(sharedWithout), Is.EqualTo(nearUid), "precondition: without a priority the nearer is taken");

            SEntMan.AddComponent<KsNpcTargetPriorityComponent>(priorityUid).Priority = 1f;
            var scores = Scores(turretPartUid, picker.Prototype);
            Assert.That(Best(scores), Is.EqualTo(priorityUid),
                $"the target with a priority ({scores.GetValueOrDefault(priorityUid):0.000}) should be preferred to the nearer ({scores.GetValueOrDefault(nearUid):0.000})");

            Assert.That(Scores(turretPartUid, _turretGunTargets), Is.EquivalentTo(sharedWithout),
                "the shared turret query should rank them as it would with no priority anywhere");
        });
    }
}
