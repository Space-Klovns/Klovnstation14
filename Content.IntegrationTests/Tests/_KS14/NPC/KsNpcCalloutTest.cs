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
    private const string RedactedSpawner = "KsRandomHumanoidSpawnerOperativeRedacted";

    private static readonly ProtoId<NpcVoiceRecordingPrototype> RedactedSpeaker = "KsRedactedSpeakerA";

    /// <summary>
    ///     Whether the Redacted's speakers have their clips. The speakers are public and mute; the clips are a private
    ///         partial of them (<see cref="NpcVoiceRecordingPrototype"/>), so without the _KsModule submodule, as on CI
    ///         for a pull request, a line is spoken and nothing plays.
    /// </summary>
    private bool HasVoiceClips => Server.ProtoMan.Index(RedactedSpeaker).Lines.Count > 0;

    private const string NoVoiceClips = "the Redacted's voice clips are in the private _KsModule submodule, which is not present";

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
        var hasVoiceClips = HasVoiceClips;

        await Server.WaitAssertion(() =>
        {
            var voiceSystem = SEntMan.System<NpcVoiceSystem>();
            var voicedUid = SEntMan.SpawnAtPosition(VoicedMob, map.GridCoords);
            var plainUid = SEntMan.SpawnAtPosition(PlainMob, map.GridCoords);

            Assert.That(voiceSystem.TrySpeakCallout(voicedUid, NpcCallout.ThrowFrag, TimeSpan.FromSeconds(5)), "the Redacted call out their throws");
            Assert.That(voiceSystem.TrySpeakCallout(voicedUid, NpcCallout.ThrowFrag, TimeSpan.FromSeconds(5)), Is.False, "but not twice at once");
            Assert.That(voiceSystem.TrySpeakCallout(voicedUid, NpcCallout.Push), "another callout has a cooldown of its own");
            Assert.That(CountClips(voicedUid), Is.EqualTo(hasVoiceClips ? 2 : 0), "each said aloud, where there are clips");

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
    ///     A Redacted as the game spawns one goes by its codename alone, which is on its ID card too, so the name that shows
    ///         behind its mask is the codename. Without the card, the mask hides who it is.
    /// </summary>
    [Test]
    public async Task TestTheRedactedGoByCodenameAlone()
    {
        if (!Server.ProtoMan.HasIndex<EntityPrototype>(RedactedSpawner))
            Assert.Ignore("the Redacted are in the private _KsModule submodule, which is not present");

        var map = await Pair.CreateTestMap();
        await Server.WaitPost(() => SEntMan.SpawnAtPosition(RedactedSpawner, map.GridCoords));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<NpcCallsignComponent>();
            Assert.That(query.MoveNext(out var mobUid, out _), "the spawner should have spawned a Redacted");
            Assert.That(SEntMan.System<NpcCallsignSystem>().TryGetCallsign(mobUid, out var callsign), "with a callsign");

            var codename = Loc.GetString(callsign.Name);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(mobUid).EntityName, Is.EqualTo(codename), "and no other name");

            var idCardSystem = SEntMan.System<Content.Shared.Access.Systems.SharedIdCardSystem>();
            Assert.That(idCardSystem.TryFindIdCard(mobUid, out var idCard), "the test needs its ID card");
            Assert.That(idCard.Comp.FullName, Is.EqualTo(codename), "the codename is on its card");
            Assert.That(Content.Shared.IdentityManagement.Identity.Name(mobUid, SEntMan), Is.EqualTo(codename), "and is what shows");

            SEntMan.DeleteEntity(idCard.Owner);
        });

        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<NpcCallsignComponent>();
            query.MoveNext(out var mobUid, out _);
            Assert.That(Content.Shared.IdentityManagement.Identity.Name(mobUid, SEntMan),
                Is.Not.EqualTo(SEntMan.GetComponent<MetaDataComponent>(mobUid).EntityName), "without the card, the mask hides who it is");
        });
    }

    /// <summary>
    ///     A squadmate that sees one go down - critical, dead outright, or gibbed - says so by its callsign, a moment later,
    ///         aloud. Once only until it is back on its feet: going critical and then dying is one callout, but one that gets up
    ///         and goes down again is called out again.
    /// </summary>
    [TestCase("crit then dead")]
    [TestCase("dead")]
    [TestCase("gibbed")]
    [TestCase("revived")]
    public async Task TestAManDownIsCalledOutByCallsign(string how)
    {
        if (!HasVoiceClips)
            Assert.Ignore(NoVoiceClips);

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

            switch (how)
            {
                case "crit then dead":
                    SetDamage(downedUid, 150);
                    SetDamage(downedUid, 250);
                    break;
                case "dead":
                    SetDamage(downedUid, 250);
                    break;
                case "gibbed":
                    SEntMan.System<Content.Shared.Gibbing.GibbingSystem>().Gib(downedUid);
                    break;
                case "revived":
                    SetDamage(downedUid, 150);
                    break;
            }
        });

        await Server.WaitAssertion(() => Assert.That(CountClips(speakerUid), Is.Zero, "not straight away"));
        await Pair.RunTicksSync(40);

        await Server.WaitAssertion(() =>
        {
            Assert.That(CountClips(speakerUid, "friendly_down_07_n"), Is.EqualTo(1), "Titan 1 down, said once");
            Assert.That(CountClips(speakerUid), Is.EqualTo(1));
        });

        if (how != "revived")
            return;

        // Down again while still down: nothing. Back on its feet, then down again: called out again.
        await Server.WaitPost(() => SetDamage(downedUid, 160));
        for (var i = 0; i < 40; i++)
        {
            await Pair.RunTicksSync(15);
            var playing = 0;
            await Server.WaitPost(() => playing = CountClips(speakerUid));
            if (playing == 0)
                break;
        }

        await Server.WaitAssertion(() => Assert.That(CountClips(speakerUid), Is.Zero, "still down: not said again"));

        await Server.WaitPost(() => SetDamage(downedUid, 0));
        await Pair.RunTicksSync(90);
        await Server.WaitPost(() => SetDamage(downedUid, 150));
        await Pair.RunTicksSync(40);

        await Server.WaitAssertion(() =>
            Assert.That(CountClips(speakerUid, "friendly_down_07_n"), Is.EqualTo(1), "up and down again: said again"));
    }

    private void SetDamage(EntityUid uid, float damage)
    {
        var blunt = Server.ResolveDependency<IPrototypeManager>().Index(BluntDamage);
        SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>().SetDamage(uid, new Content.Shared.Damage.DamageSpecifier(blunt, damage));
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
