#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Execution;
using Content.Server._KS14.NPC.HTN.Preconditions;
using Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Queries.Considerations;
using Content.Server._KS14.NPC.Queries.Queries;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Queries.Considerations;
using Content.Server.NPC.Queries.Curves;
using Content.Shared._KS14.NPC;
using Content.Shared.Damage.Systems;
using Content.Shared.Explosion.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Scores what it is told, and counts how often it is asked.
/// </summary>
public sealed partial class KsQueryTestCountingCon : UtilityConsideration
{
    public float Score;
    public int Calls;

    public override float GetScore(NPCBlackboard blackboard, EntityUid ownerUid, EntityUid targetUid)
    {
        Calls++;
        return Score;
    }
}

/// <summary>
///     The pieces HTN picks entities with: <see cref="SquadmatesQuery"/>, and the considerations narrowing a query. Those
///         about hostiles out of sight have to go by what the NPC believes, never by where the hostile really is.
/// </summary>
public sealed class KsNpcQueryTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string ExecutionerMob = "KsQueryTestMobExecutioner";
    private const string TestExplosive = "KsQueryTestExplosive";
    private const string TestFlash = "KsQueryTestFlash";
    private const string TestPrimed = "KsQueryTestPrimed";
    private const string FlashProofMob = "KsQueryTestMobFlashProof";
    private const string ClothedMob = "KsQueryTestMobClothed";
    private const string HurtByFlashesMob = "KsQueryTestMobHurtByFlashes";
    private const string FlashGoggles = "KsQueryTestFlashGoggles";

    /// <summary>
    ///     Squad updates run once a second: long enough for mobs spawned together to form one.
    /// </summary>
    private const int SquadUpdateTicks = 90;

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsQueryTestMobExecutioner
  components:
  - type: NpcExecutioner

- type: entity
  id: KsQueryTestExplosive
  components:
  - type: Explosive
    explosionType: Default
    totalIntensity: 40
    intensitySlope: 2
    maxIntensity: 8

- type: entity
  id: KsQueryTestFlash
  components:
  - type: FlashOnTrigger
    range: 6

- type: entity
  id: KsQueryTestPrimed

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsQueryTestMobFlashProof
  components:
  - type: FlashImmunity

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsQueryTestMobClothed
  components:
  - type: Inventory
    templateId: human
  - type: ContainerContainer

- type: entity
  id: KsQueryTestFlashGoggles
  components:
  - type: Item
  - type: Clothing
    slots: [ eyes ]
  - type: FlashImmunity

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsQueryTestMobHurtByFlashes
  components:
  - type: Injurable
  - type: DamagedByFlashing
    flashDamage:
      types:
        Heat: 10
";

    /// <summary>
    ///     A flash is nothing to run from for an NPC it would do nothing to, even at its feet, where the margin would have it
    ///         run from any reach at all. That goes for flashes only: the same NPC runs from an explosive.
    /// </summary>
    [Test]
    public async Task TestFlashProtectionRulesOutFlashes()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var danger = new TargetDangerRangeCon { Curve = new BoolCurve() };

        await Pair.Server.WaitAssertion(() =>
        {
            danger.Initialise(entManager.EntitySysManager.DependencyCollection);

            var flashProofUid = SpawnAt(entManager, FlashProofMob, gridUid, 0, 0);
            var flashUid = SpawnAt(entManager, TestFlash, gridUid, 0, 0);
            var explosiveUid = SpawnAt(entManager, TestExplosive, gridUid, 0, 0);
            var blackboard = new NPCBlackboard();

            Assert.Multiple(() =>
            {
                Assert.That(danger.GetScore(blackboard, flashProofUid, flashUid), Is.Zero, "a flash cannot touch it");
                Assert.That(danger.GetScore(blackboard, flashProofUid, explosiveUid), Is.EqualTo(1f), "an explosive still can");
            });
        });
    }

    /// <summary>
    ///     What protects an NPC from a flash may be what it wears, and stops when taken off.
    /// </summary>
    [Test]
    public async Task TestWornFlashProtection()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var inventorySystem = entManager.System<InventorySystem>();
        var danger = new TargetDangerRangeCon { Curve = new BoolCurve() };

        await Pair.Server.WaitAssertion(() =>
        {
            danger.Initialise(entManager.EntitySysManager.DependencyCollection);

            var ownerUid = SpawnAt(entManager, ClothedMob, gridUid, 0, 0);
            var flashUid = SpawnAt(entManager, TestFlash, gridUid, 0, 0);
            var gogglesUid = SpawnAt(entManager, FlashGoggles, gridUid, 0, 0);
            var blackboard = new NPCBlackboard();

            Assert.That(danger.GetScore(blackboard, ownerUid, flashUid), Is.EqualTo(1f), "bare-eyed, it should run");

            Assert.That(inventorySystem.TryEquip(ownerUid, gogglesUid, "eyes", silent: true, force: true));
            Assert.That(danger.GetScore(blackboard, ownerUid, flashUid), Is.Zero, "with them on, it need not");

            Assert.That(inventorySystem.TryUnequip(ownerUid, "eyes", silent: true, force: true));
            Assert.That(danger.GetScore(blackboard, ownerUid, flashUid), Is.EqualTo(1f), "and with them off it should run again");
        });
    }

    /// <summary>
    ///     Asking whether a flash would matter does not flash anything: a consideration changes nothing.
    /// </summary>
    [Test]
    public async Task TestWeighingAFlashDoesNotHurt()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var danger = new TargetDangerRangeCon { Curve = new BoolCurve() };

        await Pair.Server.WaitAssertion(() =>
        {
            danger.Initialise(entManager.EntitySysManager.DependencyCollection);

            var ownerUid = SpawnAt(entManager, HurtByFlashesMob, gridUid, 0, 0);
            var flashUid = SpawnAt(entManager, TestFlash, gridUid, 0, 0);

            Assert.Multiple(() =>
            {
                Assert.That(danger.GetScore(new NPCBlackboard(), ownerUid, flashUid), Is.EqualTo(1f), "it should run from something that would hurt it");
                Assert.That(entManager.System<DamageableSystem>().GetTotalDamage(ownerUid).Float(), Is.Zero, "without having been hurt by the thought");
            });
        });
    }

    /// <summary>
    ///     Squadmates are everyone else in the squad, those that have gone down and left it included. Nobody else is.
    /// </summary>
    [Test]
    public async Task TestSquadmatesQuery()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var query = new SquadmatesQuery();
        var members = new List<EntityUid>();
        EntityUid strangerUid = default;

        await Pair.Server.WaitPost(() =>
        {
            query.Initialise(entManager.EntitySysManager.DependencyCollection);
            members.Add(SpawnAt(entManager, SyndicateMob, gridUid, 0, 0));
            members.Add(SpawnAt(entManager, SyndicateMob, gridUid, 1, 0));
            members.Add(SpawnAt(entManager, SyndicateMob, gridUid, 2, 0));
            strangerUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var ownerUid = members[0];
            var downedUid = members[1];

            entManager.System<MobStateSystem>().ChangeMobState(downedUid, MobState.Critical);

            Assert.That(squadSystem.TryGetSquad(downedUid, out _), Is.False, "the test relies on going down taking it out of the squad");

            var squadmates = new HashSet<EntityUid>();
            query.AddEntities(new NPCBlackboard(), ownerUid, squadmates);

            Assert.That(squadmates, Is.EquivalentTo(new[] { members[1], members[2] }),
                "everyone else in the squad, the one that is down included");
            Assert.That(squadmates, Does.Not.Contain(strangerUid));

            entManager.System<MobStateSystem>().ChangeMobState(downedUid, MobState.Dead);

            squadmates.Clear();
            query.AddEntities(new NPCBlackboard(), ownerUid, squadmates);

            Assert.That(squadmates, Is.EquivalentTo(new[] { members[2] }), "one that has died is nobody to see to");
        });
    }

    /// <summary>
    ///     The three considerations about a hostile out of sight go by where the owner believes it to be. So move it
    ///         somewhere else entirely and they should not change.
    /// </summary>
    [Test]
    public async Task TestContactConsiderationsGoByBelief()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var perceptionSystem = entManager.System<NpcPerceptionSystem>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var distance = new ContactDistanceCon { MaxRange = 8f, Curve = new BoolCurve() };
        var lineOfSight = new ContactInLOSCon { Radius = 8f, Curve = new BoolCurve() };
        var clear = new ContactClearOfSquadCon { Radius = 2f, Curve = new BoolCurve() };

        EntityUid ownerUid = default;
        EntityUid mateUid = default;
        EntityUid hostileUid = default;

        await Pair.Server.WaitPost(() =>
        {
            ownerUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            mateUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 0);
            hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 0, 3);

            // Between the owner and everything east of it.
            for (var y = -3; y <= 3; y++)
            {
                SpawnAt(entManager, "WallSolid", gridUid, 5, y);
            }
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var dependencyCollection = entManager.EntitySysManager.DependencyCollection;
            distance.Initialise(dependencyCollection);
            lineOfSight.Initialise(dependencyCollection);
            clear.Initialise(dependencyCollection);

            var blackboard = new NPCBlackboard();

            void Believe(int x, int y)
            {
                var now = Pair.Server.ResolveDependency<IGameTiming>().CurTime;
                perceptionSystem.SetContact(ownerUid, hostileUid, new NpcContact(NpcContactState.Lost, now, now, now,
                    new EntityCoordinates(gridUid, new Vector2(x + 0.5f, y + 0.5f)), default, null, Reacted: true));
            }

            // Believed to be behind the wall, and really right beside the owner.
            Believe(8, 0);

            Assert.Multiple(() =>
            {
                Assert.That(distance.GetScore(blackboard, ownerUid, hostileUid), Is.EqualTo(1f).Within(0.05f), "as far as it is believed to be");
                Assert.That(lineOfSight.GetScore(blackboard, ownerUid, hostileUid), Is.Zero, "the spot it is believed at is behind the wall");
                Assert.That(clear.GetScore(blackboard, ownerUid, hostileUid), Is.EqualTo(1f), "nobody is near the spot it is believed at");
            });

            // Believed to be right beside the squadmate, and really somewhere else again.
            Believe(1, 1);
            transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(8.5f, 0.5f)));

            Assert.Multiple(() =>
            {
                Assert.That(distance.GetScore(blackboard, ownerUid, hostileUid), Is.LessThan(0.3f), "as near as it is believed to be");
                Assert.That(lineOfSight.GetScore(blackboard, ownerUid, hostileUid), Is.EqualTo(1f), "the spot it is believed at is in plain view");
                Assert.That(clear.GetScore(blackboard, ownerUid, hostileUid), Is.Zero, "a squadmate is standing on the spot it is believed at");
            });

            Assert.That(entManager.System<NpcSquadSystem>().TryGetSquad(mateUid, out _), "the test relies on the two being in a squad");
        });
    }

    /// <summary>
    ///     Something primed is only worth running from if it can reach: just inside and just outside its reach, whatever
    ///         that is, for each way a thing can say what it does.
    /// </summary>
    [TestCase(TestExplosive)]
    [TestCase(TestFlash)]
    [TestCase(TestPrimed)]
    public async Task TestDangerRange(string prototype)
    {
        const float margin = 0.5f;

        var (entManager, gridUid) = await SetUpGrid();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var danger = new TargetDangerRangeCon { Margin = margin, DefaultRange = 3f, Curve = new BoolCurve() };

        await Pair.Server.WaitAssertion(() =>
        {
            danger.Initialise(entManager.EntitySysManager.DependencyCollection);

            var ownerUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            var thingUid = SpawnAt(entManager, prototype, gridUid, 0, 0);

            float range;
            if (entManager.TryGetComponent<ExplosiveComponent>(thingUid, out var explosiveComponent))
                range = entManager.System<ExplosionSystem>().IntensityToRadius(explosiveComponent.TotalIntensity, explosiveComponent.IntensitySlope, explosiveComponent.MaxIntensity);
            else if (entManager.TryGetComponent<FlashOnTriggerComponent>(thingUid, out var flashOnTriggerComponent))
                range = flashOnTriggerComponent.Range;
            else
                range = danger.DefaultRange;

            var blackboard = new NPCBlackboard();

            void MoveTo(float x)
            {
                var ownerCoordinates = entManager.GetComponent<TransformComponent>(ownerUid).Coordinates;
                transformSystem.SetCoordinates(thingUid, ownerCoordinates.Offset(new Vector2(x, 0f)));
            }

            MoveTo(range + margin - 0.25f);
            Assert.That(danger.GetScore(blackboard, ownerUid, thingUid), Is.EqualTo(1f), "just within its reach");

            MoveTo(range + margin + 0.25f);
            Assert.That(danger.GetScore(blackboard, ownerUid, thingUid), Is.Zero, "just past its reach");
        });
    }

    /// <summary>
    ///     And stops at the first thing that fails, Or at the first that passes, so what comes after is never worked out.
    /// </summary>
    [TestCase(false, 0f, 0f, 0f, 1, 0)]
    [TestCase(false, 1f, 0.5f, 0.5f, 1, 1)]
    [TestCase(true, 1f, 0f, 1f, 1, 0)]
    [TestCase(true, 0f, 0f, 0f, 1, 1)]
    [TestCase(true, 0f, 1f, 1f, 1, 1)]
    public async Task TestLogicConsiderationsShortCircuit(bool or, float first, float second, float expected, int firstCalls, int secondCalls)
    {
        var (entManager, gridUid) = await SetUpGrid();
        var firstCon = new KsQueryTestCountingCon { Score = first, Curve = new QuadraticCurve() };
        var secondCon = new KsQueryTestCountingCon { Score = second, Curve = new QuadraticCurve() };

        KsLogicCon logic = or
            ? new KsLogicOrCon { Curve = new BoolCurve() }
            : new KsLogicAndCon { Curve = new BoolCurve() };

        logic.Considerations.Add(firstCon);
        logic.Considerations.Add(secondCon);

        await Pair.Server.WaitAssertion(() =>
        {
            logic.Initialise(entManager.EntitySysManager.DependencyCollection);

            var mobUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, mobUid);

            Assert.Multiple(() =>
            {
                Assert.That(logic.GetScore(blackboard, mobUid, mobUid), Is.EqualTo(expected).Within(0.001f));
                Assert.That(firstCon.Calls, Is.EqualTo(firstCalls));
                Assert.That(secondCon.Calls, Is.EqualTo(secondCalls));
            });
        });
    }

    /// <summary>
    ///     Each part of a logic consideration goes through its own curve, and a whitelist can ask about the owner rather than
    ///         the target: together, "by those that do that".
    /// </summary>
    [Test]
    public async Task TestOwnerWhitelistInALogicConsideration()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var factory = Pair.Server.ResolveDependency<IComponentFactory>();
        var logic = new KsLogicAndCon { Curve = new InverseBoolCurve() };

        logic.Considerations.Add(new FitsWhitelistCon
        {
            CheckOwner = true,
            Whitelist = new EntityWhitelist { Components = [factory.GetComponentName<NpcExecutionerComponent>()] },
            Curve = new BoolCurve(),
        });
        logic.Considerations.Add(new TargetIsCritCon { Curve = new BoolCurve() });

        await Pair.Server.WaitAssertion(() =>
        {
            logic.Initialise(entManager.EntitySysManager.DependencyCollection);

            var executionerUid = SpawnAt(entManager, ExecutionerMob, gridUid, 0, 0);
            var otherUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 0);
            var victimUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 2, 0);

            float Score(EntityUid ownerUid)
            {
                var blackboard = new NPCBlackboard();
                blackboard.SetValue(NPCBlackboard.Owner, ownerUid);
                return logic.GetScore(blackboard, ownerUid, victimUid);
            }

            Assert.That(Score(executionerUid), Is.Zero, "the target is on its feet");

            entManager.System<MobStateSystem>().ChangeMobState(victimUid, MobState.Critical);

            Assert.That(Score(executionerUid), Is.EqualTo(1f), "the owner is one that does this, and the target is down");
            Assert.That(Score(otherUid), Is.Zero, "this owner is not one that does this");
        });
    }

    /// <summary>
    ///     One to a target: a squadmate's claim keeps the others off it until it lapses, and an NPC's own claim does not
    ///         keep it off its own target.
    /// </summary>
    [Test]
    public async Task TestTargetClaims()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var claimSystem = entManager.System<NpcTargetClaimSystem>();
        var unclaimed = new TargetUnclaimedCon { Curve = new BoolCurve() };
        EntityUid firstUid = default;
        EntityUid secondUid = default;
        EntityUid targetUid = default;
        EntityUid otherTargetUid = default;

        await Pair.Server.WaitPost(() =>
        {
            unclaimed.Initialise(entManager.EntitySysManager.DependencyCollection);
            firstUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            secondUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 0);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 2, 0);
            otherTargetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var blackboard = new NPCBlackboard();

            Assert.That(claimSystem.TryClaim(firstUid, targetUid, System.TimeSpan.FromHours(1)), "nobody has it yet");

            Assert.Multiple(() =>
            {
                Assert.That(unclaimed.GetScore(blackboard, secondUid, targetUid), Is.Zero, "a squadmate has claimed it");
                Assert.That(unclaimed.GetScore(blackboard, firstUid, targetUid), Is.EqualTo(1f), "its own claim is no obstacle");
                Assert.That(unclaimed.GetScore(blackboard, secondUid, otherTargetUid), Is.EqualTo(1f), "nobody has claimed this one");
            });

            Assert.That(claimSystem.TryClaim(secondUid, targetUid, System.TimeSpan.FromHours(1)), Is.False, "a squadmate got there first");
            Assert.That(claimSystem.TryClaim(firstUid, targetUid, System.TimeSpan.Zero), "its own claim is no obstacle to renewing it");

            Assert.That(unclaimed.GetScore(blackboard, secondUid, targetUid), Is.EqualTo(1f), "a lapsed claim holds nobody back");
            Assert.That(claimSystem.TryClaim(secondUid, targetUid, System.TimeSpan.FromHours(1)), "so a squadmate can have it");
        });
    }

    /// <summary>
    ///     Squadmates that planned side by side all reach the claim: the first gets it, and the rest fail and plan again.
    /// </summary>
    [Test]
    public async Task TestOnlyTheFirstToActOnAPlanGetsTheClaim()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid firstUid = default;
        EntityUid secondUid = default;
        EntityUid targetUid = default;

        await Pair.Server.WaitPost(() =>
        {
            firstUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            secondUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 0);
            targetUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 2, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var claim = new ClaimTargetOperator();
            claim.Initialize(entManager.EntitySysManager);

            HTNOperatorStatus Act(EntityUid uid)
            {
                var blackboard = new NPCBlackboard();
                blackboard.SetValue(NPCBlackboard.Owner, uid);
                blackboard.SetValue(claim.TargetKey, targetUid);
                return claim.Update(blackboard, 0f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Act(firstUid), Is.EqualTo(HTNOperatorStatus.Finished));
                Assert.That(Act(secondUid), Is.EqualTo(HTNOperatorStatus.Failed));
            });
        });
    }

    [Test]
    public async Task TestTargetShotBySquad()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var shot = new TargetShotBySquadCon { Curve = new BoolCurve() };
        EntityUid ownerUid = default;
        EntityUid mateUid = default;
        EntityUid shotUid = default;
        EntityUid otherUid = default;

        await Pair.Server.WaitPost(() =>
        {
            shot.Initialise(entManager.EntitySysManager.DependencyCollection);
            ownerUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            mateUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 0);
            shotUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 2, 0);
            otherUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 3, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.EnsureComponent<NPCRangedCombatComponent>(mateUid).Target = shotUid;

            var blackboard = new NPCBlackboard();

            Assert.Multiple(() =>
            {
                Assert.That(shot.GetScore(blackboard, ownerUid, shotUid), Is.EqualTo(1f));
                Assert.That(shot.GetScore(blackboard, ownerUid, otherUid), Is.Zero);
            });
        });
    }

    /// <summary>
    ///     With <see cref="CooldownOperator.FailIfRunning"/>, of squadmates that planned side by side, one gets to go on.
    /// </summary>
    [Test]
    public async Task TestOnlyTheFirstToActOnAPlanStartsTheCooldown()
    {
        var (entManager, gridUid) = await SetUpGrid();
        EntityUid firstUid = default;
        EntityUid secondUid = default;

        await Pair.Server.WaitPost(() =>
        {
            firstUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            secondUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var cooldown = new CooldownOperator
            {
                Id = "KsQueryTestRaceCooldown",
                Duration = System.TimeSpan.FromHours(1),
                SquadWide = true,
                FailIfRunning = true,
            };
            cooldown.Initialize(entManager.EntitySysManager);

            HTNOperatorStatus Act(EntityUid uid)
            {
                var blackboard = new NPCBlackboard();
                blackboard.SetValue(NPCBlackboard.Owner, uid);
                return cooldown.Update(blackboard, 0f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Act(firstUid), Is.EqualTo(HTNOperatorStatus.Finished));
                Assert.That(Act(secondUid), Is.EqualTo(HTNOperatorStatus.Failed));
            });
        });
    }

    /// <summary>
    ///     A squad-wide cooldown set by one member holds back the rest. One that is not stays with whoever set it.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestCooldownSharedBySquad(bool squadWide)
    {
        const string cooldown = "KsQueryTestCooldown";

        var (entManager, gridUid) = await SetUpGrid();
        EntityUid firstUid = default;
        EntityUid secondUid = default;

        await Pair.Server.WaitPost(() =>
        {
            firstUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            secondUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 0);
        });

        await Pair.RunTicksSync(SquadUpdateTicks);

        await Pair.Server.WaitAssertion(() =>
        {
            var setCooldown = new CooldownOperator { Id = cooldown, Duration = System.TimeSpan.FromHours(1), SquadWide = squadWide };
            var onCooldown = new CooldownPrecondition { Id = cooldown, SquadWide = squadWide };
            setCooldown.Initialize(entManager.EntitySysManager);
            onCooldown.Initialize(entManager.EntitySysManager);

            var firstBlackboard = new NPCBlackboard();
            firstBlackboard.SetValue(NPCBlackboard.Owner, firstUid);
            var secondBlackboard = new NPCBlackboard();
            secondBlackboard.SetValue(NPCBlackboard.Owner, secondUid);

            setCooldown.Update(firstBlackboard, 0f);

            Assert.Multiple(() =>
            {
                Assert.That(onCooldown.IsMet(firstBlackboard), "whoever set it is on cooldown");
                Assert.That(onCooldown.IsMet(secondBlackboard), Is.EqualTo(squadWide));
            });
        });
    }

    private async Task<(IEntityManager EntManager, EntityUid GridUid)> SetUpGrid()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;

        await server.WaitPost(() =>
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner);

        return (entManager, gridUid);
    }
}
