#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     NPC meters (<see cref="NpcMeterSystem"/>): worked out lazily from when they were last changed, kept within 0 and
///         their maximum, and raised on a squad's survivors when one of them goes down. Also NPC voice sets, which let
///         one HTN speak differently for different mobs.
/// </summary>
public sealed class KsNpcMeterTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string DecayingMeter = "KsMeterTestDecaying";
    private const string SteadyMeter = "KsMeterTestSteady";
    private const string WaryMob = "KsMeterTestWaryMob";
    private const string VoicedMob = "KsMeterTestVoicedMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: npcMeter
  id: KsMeterTestDecaying
  max: 100
  decayPerSecond: 10

- type: npcMeter
  id: KsMeterTestSteady
  max: 100
  decayPerSecond: 0

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsMeterTestWaryMob
  components:
  - type: NpcMeterOnSquadLoss
    meter: KsMeterTestSteady
    critical: 10
    dead: 25

- type: npcVoiceSet
  id: KsMeterTestVoice
  silenced:
  - KsOperativeGetAway
  replacements:
    KsOperativeContact: KsOperativeNtContact

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsMeterTestVoicedMob
  components:
  - type: NpcVoiceSet
    set: KsMeterTestVoice
";

    /// <summary>
    ///     A meter is worked out when read, from what it was set to and how long ago: it decays without anything ticking
    ///         it, stops at 0, and never goes past its maximum. A meter never raised reads 0.
    /// </summary>
    [Test]
    public async Task TestMeterDecaysLazily()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var meterSystem = entManager.System<NpcMeterSystem>();
        EntityUid uid = default;

        await Pair.Server.WaitAssertion(() =>
        {
            uid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            Assert.That(meterSystem.GetValue(uid, DecayingMeter), Is.Zero, "a meter never raised reads 0");

            meterSystem.Add(uid, DecayingMeter, 50f);
            Assert.That(meterSystem.GetValue(uid, DecayingMeter), Is.EqualTo(50f).Within(0.01f));
        });

        await Pair.RunTicksSync(30); // 1s at 10 a second

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(meterSystem.GetValue(uid, DecayingMeter), Is.EqualTo(40f).Within(0.5f), "it should have decayed by a second's worth");

            meterSystem.Add(uid, DecayingMeter, 500f);
            Assert.That(meterSystem.GetValue(uid, DecayingMeter), Is.EqualTo(100f).Within(0.01f), "it should stop at its maximum");
        });

        await Pair.RunTicksSync(450); // 15s: well past empty

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(meterSystem.GetValue(uid, DecayingMeter), Is.Zero, "it should stop at 0");
            Assert.That(meterSystem.GetFraction(uid, DecayingMeter), Is.Zero);
        });
    }

    /// <summary>
    ///     A squadmate going critical, and then dying, raises the meter on everyone else in the squad - the second time
    ///         too, though by then the one that went down has left the squad. Not on the one that went down.
    /// </summary>
    [Test]
    public async Task TestSquadLossRaisesMeterOnSurvivors()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var meterSystem = entManager.System<NpcMeterSystem>();
        var mobStateSystem = entManager.System<MobStateSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();
        EntityUid firstUid = default, secondUid = default, thirdUid = default;

        await Pair.Server.WaitPost(() =>
        {
            firstUid = SpawnAt(entManager, WaryMob, gridUid, 0, 0);
            secondUid = SpawnAt(entManager, WaryMob, gridUid, 1, 0);
            thirdUid = SpawnAt(entManager, WaryMob, gridUid, 2, 0);
        });

        await Pair.RunTicksSync(90);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(squadSystem.TryGetSquad(firstUid, out var squadEntity) && squadEntity.Value.Comp.Members.Count == 3,
                "the three should be in one squad");

            mobStateSystem.ChangeMobState(thirdUid, MobState.Critical);
            Assert.Multiple(() =>
            {
                Assert.That(meterSystem.GetValue(firstUid, SteadyMeter), Is.EqualTo(10f).Within(0.01f), "a squadmate going critical");
                Assert.That(meterSystem.GetValue(secondUid, SteadyMeter), Is.EqualTo(10f).Within(0.01f), "a squadmate going critical");
                Assert.That(meterSystem.GetValue(thirdUid, SteadyMeter), Is.Zero, "not on the one that went down");
            });

            mobStateSystem.ChangeMobState(thirdUid, MobState.Dead);
            Assert.Multiple(() =>
            {
                Assert.That(meterSystem.GetValue(firstUid, SteadyMeter), Is.EqualTo(35f).Within(0.01f), "and then dying, after leaving the squad");
                Assert.That(meterSystem.GetValue(secondUid, SteadyMeter), Is.EqualTo(35f).Within(0.01f), "and then dying, after leaving the squad");
            });
        });
    }

    /// <summary>
    ///     <c>ks_setmeter</c> sets a meter on the NPC named and everyone in its squad, clamped to the meter's range; on an
    ///         NPC with no squad, on it alone.
    /// </summary>
    [Test]
    public async Task TestSetMeterCommandSetsWholeSquad()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var meterSystem = entManager.System<NpcMeterSystem>();
        var squadSystem = entManager.System<NpcSquadSystem>();
        var consoleHost = Pair.Server.ResolveDependency<Robust.Server.Console.IServerConsoleHost>();
        EntityUid firstUid = default, secondUid = default;

        await Pair.Server.WaitPost(() =>
        {
            firstUid = SpawnAt(entManager, SyndicateMob, gridUid, 0, 0);
            secondUid = SpawnAt(entManager, SyndicateMob, gridUid, 1, 0);
        });

        await Pair.RunTicksSync(90);

        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(squadSystem.TryGetSquad(firstUid, out var squadEntity) && squadEntity.Value.Comp.Members.Count == 2,
                "the two should be in one squad");

            consoleHost.ExecuteCommand($"ks_setmeter {entManager.GetNetEntity(firstUid)} {SteadyMeter} 60");
            Assert.Multiple(() =>
            {
                Assert.That(meterSystem.GetValue(firstUid, SteadyMeter), Is.EqualTo(60f).Within(0.01f), "the NPC named");
                Assert.That(meterSystem.GetValue(secondUid, SteadyMeter), Is.EqualTo(60f).Within(0.01f), "the rest of its squad");
            });

            consoleHost.ExecuteCommand($"ks_setmeter {entManager.GetNetEntity(secondUid)} {SteadyMeter} 500");
            Assert.That(meterSystem.GetValue(firstUid, SteadyMeter), Is.EqualTo(100f).Within(0.01f), "clamped to the maximum");

            var lonerUid = SpawnAt(entManager, SyndicateMob, gridUid, 10, 10);
            entManager.RemoveComponent<NpcSquadMemberComponent>(lonerUid);
            consoleHost.ExecuteCommand($"ks_setmeter {entManager.GetNetEntity(lonerUid)} {SteadyMeter} 20");
            Assert.Multiple(() =>
            {
                Assert.That(meterSystem.GetValue(lonerUid, SteadyMeter), Is.EqualTo(20f).Within(0.01f), "an NPC with no squad");
                Assert.That(meterSystem.GetValue(firstUid, SteadyMeter), Is.EqualTo(100f).Within(0.01f), "nobody else");
            });
        });
    }

    /// <summary>
    ///     A mob with a voice set speaks the set's replacement for a line set it lists, the line set itself for one it
    ///         does not, and nothing at all for one it silences. A mob without a voice set speaks every line set as it
    ///         is.
    /// </summary>
    [Test]
    public async Task TestVoiceSetReplacesLineSets()
    {
        var (entManager, gridUid) = await SetUpGrid();
        var speakOperator = new SpeakOperator();

        await Pair.Server.WaitAssertion(() =>
        {
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(speakOperator, oneOff: true);

            var voicedBlackboard = new NPCBlackboard();
            voicedBlackboard.SetValue(NPCBlackboard.Owner, SpawnAt(entManager, VoicedMob, gridUid, 0, 0));
            var plainBlackboard = new NPCBlackboard();
            plainBlackboard.SetValue(NPCBlackboard.Owner, SpawnAt(entManager, SyndicateMob, gridUid, 1, 0));

            Assert.Multiple(() =>
            {
                Assert.That(speakOperator.KsResolveLineSet(voicedBlackboard, "KsOperativeContact").Id, Is.EqualTo("KsOperativeNtContact"));
                Assert.That(speakOperator.KsResolveLineSet(voicedBlackboard, "KsOperativeReloading").Id, Is.EqualTo("KsOperativeReloading"),
                    "a line set the voice set does not list is spoken as it is");
                Assert.That(speakOperator.KsResolveLineSet(plainBlackboard, "KsOperativeContact").Id, Is.EqualTo("KsOperativeContact"),
                    "a mob without a voice set speaks every line set as it is");
            });

            var warningOperator = new SpeakOperator
            {
                Speech = new SpeakOperator.SpeakOperatorSpeech.LocalizedSetSpeakOperatorSpeech { LineSet = "KsOperativeGetAway" },
            };
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(warningOperator, oneOff: true);

            Assert.Multiple(() =>
            {
                Assert.That(warningOperator.KsIsSilenced(voicedBlackboard), "a silenced line set is not said");
                Assert.That(warningOperator.KsIsSilenced(plainBlackboard), Is.False, "a mob without a voice set says it");
                Assert.That(speakOperator.KsIsSilenced(voicedBlackboard), Is.False, "nothing else is silenced");
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
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-5, -5), new Vector2i(5, 5)).Owner;
        });

        return (entManager, gridUid);
    }
}
