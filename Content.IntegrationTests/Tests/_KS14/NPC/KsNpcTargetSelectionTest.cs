#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Targeting;
using Content.Server.NPC;
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
///     How NPCs choose between targets: by the priority a target is given (<see cref="KsTargetPriorityCon"/>).
/// </summary>
[TestFixture]
[TestOf(typeof(KsTargetPriorityCon))]
public sealed class KsNpcTargetSelectionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Ids that exist only in the test prototypes below.
    private const string PriorityQuery = "KsTestTargetSelectionPriority";

    // The private module's operative target query, and the public defender turret: run only where both are present.
    private readonly ProtoId<UtilityQueryPrototype> _operativeGunTargets = "KsOperativeNearbyGunTargets";
    private readonly EntProtoId _defenderTurret = "KsWeaponTurretNanoTrasenDisposable";

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
    ///     Content: an operative picks a defender turret over a healthy defender just as far off, though the turret,
    ///         having no health to speak of, used to score far below one.
    /// </summary>
    [Test]
    public async Task OperativesPreferDefenderTurrets()
    {
        if (!SProtoMan.HasIndex(_operativeGunTargets))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var gridUid = await MakeFloor();

        await Server.WaitAssertion(() =>
        {
            var ownerUid = SpawnAt(SEntMan, LonerMob, gridUid, 0, 0);
            var defenderUid = SpawnAt(SEntMan, NanoTrasenMob, gridUid, 4, 0);
            var turretUid = SpawnAt(SEntMan, _defenderTurret, gridUid, 0, 4);

            var perceptionSystem = SEntMan.System<NpcPerceptionSystem>();
            var now = Server.ResolveDependency<IGameTiming>().CurTime;
            foreach (var targetUid in new[] { defenderUid, turretUid })
            {
                perceptionSystem.SetContact(ownerUid, targetUid, new NpcContact(NpcContactState.Visible,
                    now, now, now, SEntMan.GetComponent<TransformComponent>(targetUid).Coordinates, default, null, Reacted: true));
            }

            var scores = Scores(ownerUid, _operativeGunTargets);
            Assert.That(scores.Keys, Is.EquivalentTo(new[] { defenderUid, turretUid }), "both should be candidates");
            Assert.That(Best(scores), Is.EqualTo(turretUid),
                $"the turret ({scores[turretUid]:0.000}) should be preferred to a healthy defender ({scores[defenderUid]:0.000})");
        });
    }
}
