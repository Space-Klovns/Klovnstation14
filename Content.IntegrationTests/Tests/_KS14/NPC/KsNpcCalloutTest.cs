#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Callsigns;
using Content.Server._KS14.NPC.HTN.Preconditions;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Voices;
using Content.Server.NPC;
using Content.Shared.Mobs;
using Content.Shared.Trigger.Systems;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Callouts only some voices make (<see cref="NpcCallout"/>), callsigns, a squadmate down called out by callsign, and an
///         enemy grenade told from the squad's own.
/// </summary>
public sealed class KsNpcCalloutTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<Content.Shared.Damage.Prototypes.DamageTypePrototype> BluntDamage = "Blunt";

    private const string VoicedMob = "KsCalloutTestMob";
    private const string PlainMob = "KsCalloutTestMobPlain";
    private const string Grenade = "KsCalloutTestGrenade";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsCalloutTestMob
  components:
  - type: NpcVoiceSet
    set: KsOperativeRedacted
    speakers:
    - KsRedactedSpeakerA
  - type: NpcCallsign
    set: KsRedactedCallsigns

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsCalloutTestMobPlain

- type: entity
  id: KsCalloutTestGrenade
  components:
  - type: TimerTrigger
    delay: 1000
";

    /// <summary>
    ///     A voice that makes a callout says it, and not again until its cooldown is up. One that does not never does.
    /// </summary>
    [Test]
    public async Task TestACalloutIsOnlyInAVoiceThatMakesIt()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var voiceSystem = SEntMan.System<NpcVoiceSystem>();
            var voicedUid = SEntMan.SpawnAtPosition(VoicedMob, map.GridCoords);
            var plainUid = SEntMan.SpawnAtPosition(PlainMob, map.GridCoords);

            Assert.That(voiceSystem.TrySpeakCallout(voicedUid, NpcCallout.ThrowFrag, TimeSpan.FromSeconds(5)), "the Redacted call out their throws");
            Assert.That(voiceSystem.TrySpeakCallout(voicedUid, NpcCallout.ThrowFrag, TimeSpan.FromSeconds(5)), Is.False, "but not twice at once");
            Assert.That(voiceSystem.TrySpeakCallout(voicedUid, NpcCallout.Push), "another callout has a cooldown of its own");
            Assert.That(CountClips(voicedUid), Is.EqualTo(2), "each said aloud");

            foreach (var callout in Enum.GetValues<NpcCallout>())
            {
                Assert.That(voiceSystem.TrySpeakCallout(plainUid, callout), Is.False, $"a voice without {callout} says nothing");
            }
        });
    }

    /// <summary>
    ///     Callsigns go out in order, one each among the living on a map, after the name.
    /// </summary>
    [Test]
    public async Task TestCallsignsAreHandedOutOnceEach()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var callsignSystem = SEntMan.System<NpcCallsignSystem>();
            var names = new List<string>();

            for (var i = 0; i < 3; i++)
            {
                var mobUid = SEntMan.SpawnAtPosition(VoicedMob, map.GridCoords);
                Assert.That(callsignSystem.TryGetCallsign(mobUid, out var callsign), "it should have a callsign");
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(mobUid).EntityName, Does.EndWith(Loc.GetString(callsign.Name)), "shown after its name");
                names.Add(Loc.GetString(callsign.Name));
            }

            Assert.That(names, Is.EqualTo(new[] { "Viper 1", "Titan 1", "Dagger 1" }));
        });
    }

    /// <summary>
    ///     A squadmate that sees one go down says so by its callsign, a moment later, aloud. Going critical and then dying is
    ///         one callout, not two.
    /// </summary>
    [Test]
    public async Task TestAManDownIsCalledOutByCallsign()
    {
        var map = await Pair.CreateTestMap();
        EntityUid speakerUid = default, downedUid = default;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(SEntMan, Server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>(), map.MapId, map.Grid,
                new Robust.Shared.Maths.Vector2i(-4, -4), new Robust.Shared.Maths.Vector2i(4, 4)).Owner;
            speakerUid = SpawnAt(SEntMan, VoicedMob, gridUid, 0, 0);
            downedUid = SpawnAt(SEntMan, VoicedMob, gridUid, 2, 0);
        });

        await Pair.RunTicksSync(240); // for a squad to form

        await Server.WaitPost(() =>
        {
            var squadSystem = SEntMan.System<NpcSquadSystem>();
            Assert.That(squadSystem.TryGetSquad(speakerUid, out var squad) && squadSystem.TryGetSquad(downedUid, out var otherSquad) &&
                squad.Value.Owner == otherSquad.Value.Owner, "the test needs the two in one squad");

            var blunt = Server.ResolveDependency<IPrototypeManager>().Index(BluntDamage);
            var damageableSystem = SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>();
            damageableSystem.SetDamage(downedUid, new Content.Shared.Damage.DamageSpecifier(blunt, 150));
            damageableSystem.SetDamage(downedUid, new Content.Shared.Damage.DamageSpecifier(blunt, 250));
        });

        await Server.WaitAssertion(() => Assert.That(CountClips(speakerUid), Is.Zero, "not straight away"));
        await Pair.RunTicksSync(40);

        await Server.WaitAssertion(() =>
        {
            Assert.That(CountClips(speakerUid, "friendly_down_07_n"), Is.EqualTo(1), "Titan 1 down, said once");
            Assert.That(CountClips(speakerUid), Is.EqualTo(1));
        });
    }

    /// <summary>
    ///     A grenade lit by a friend is not an enemy's; one lit by a hostile is, and so is one nobody can tell the thrower of.
    /// </summary>
    [TestCase("friend", false)]
    [TestCase("hostile", true)]
    [TestCase("nobody", true)]
    public async Task TestAnEnemyGrenadeIsToldFromTheSquadsOwn(string lighter, bool hostile)
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var ownerUid = SEntMan.SpawnAtPosition(PlainMob, map.GridCoords);
            var grenadeUid = SEntMan.SpawnAtPosition(Grenade, map.GridCoords);

            EntityUid? lighterUid = lighter switch
            {
                "friend" => SEntMan.SpawnAtPosition(PlainMob, map.GridCoords),
                "hostile" => SEntMan.SpawnAtPosition("KsSquadTestMobNanoTrasen", map.GridCoords),
                _ => null,
            };

            SEntMan.System<TriggerSystem>().ActivateTimerTrigger(grenadeUid, lighterUid);

            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, ownerUid);
            blackboard.SetValue("GrenadeTarget", grenadeUid);

            var precondition = new HostileGrenadePrecondition { Key = "GrenadeTarget" };
            SEntMan.EntitySysManager.DependencyCollection.InjectDependencies(precondition, oneOff: true);
            Assert.That(precondition.IsMet(blackboard), Is.EqualTo(hostile));
        });
    }

    private int CountClips(EntityUid uid, string? clip = null)
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<AudioComponent, TransformComponent>();

        while (query.MoveNext(out _, out var audioComponent, out var transformComponent))
        {
            var fileName = $"{audioComponent.FileName}";
            if (transformComponent.ParentUid == uid && fileName.Contains("/Voice/Redacted/") && (clip == null || fileName.Contains(clip)))
                count++;
        }

        return count;
    }
}
