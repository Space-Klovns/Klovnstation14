#nullable enable
using System.Linq;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Voices;
using Content.Shared.Dataset;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     The Redacted's recorded voices: each locale string they speak has exactly one clip per speaker, every clip
///         exists, and a mob keeps the speaker and pitch it was spawned with.
/// </summary>
public sealed class KsNpcVoiceRecordingTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly string[] SpokenLineSets =
    [
        "KsOperativeRedactedTargetDown", "KsOperativeExecution", "KsOperativeRedactedAlertGrenade", "KsOperativeRedactedThrowFrag",
        "KsOperativeRedactedThrowOther", "KsOperativeRedactedManDown", "KsOperativeRedactedSuppressing", "KsOperativeRedactedPush",
        "KsOperativeRedactedFlank",
    ];

    private const string Callsigns = "KsRedactedCallsigns";
    private static readonly string[] Speakers = ["KsRedactedSpeakerA", "KsRedactedSpeakerB"];

    private const string VoicedMob = "KsVoiceRecordingTestMob";
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
  id: KsVoiceRecordingTestMob
  components:
  - type: NpcVoiceSet
    set: KsOperativeRedacted
    speakers:
    - KsRedactedSpeakerA
    - KsRedactedSpeakerB
    pitchSpread: 0.05
";

    /// <summary>
    ///     Every string the Redacted speak resolves, and every speaker has a clip for it that exists, which no string of the
    ///         speaker saying something else shares.
    /// </summary>
    [Test]
    public async Task TestEverySpokenLineHasOneClipPerSpeaker()
    {
        if (!HasVoiceClips)
            Assert.Ignore(NoVoiceClips);

        var protoManager = Server.ResolveDependency<IPrototypeManager>();
        var resourceManager = Server.ResolveDependency<IResourceManager>();

        await Server.WaitAssertion(() =>
        {
            foreach (var speakerId in Speakers)
            {
                var speaker = protoManager.Index<NpcVoiceRecordingPrototype>(speakerId);
                var usedPaths = new Dictionary<string, string>(); // clip, and the text it says
                var voicedIds = new HashSet<string>();

                // A string in two sets (a flashbang's "Fire in the hole" is a frag's too) is one string, with one clip.
                var spokenIds = new HashSet<string>();
                foreach (var lineSetId in SpokenLineSets)
                {
                    spokenIds.UnionWith(protoManager.Index<LocalizedDatasetPrototype>(lineSetId).Values);
                }

                foreach (var callsign in protoManager.Index<Content.Server._KS14.NPC.Callsigns.NpcCallsignSetPrototype>(Callsigns).Callsigns)
                {
                    Assert.That(Loc.TryGetString(callsign.Name, out _), $"{callsign.Name} should resolve");
                    spokenIds.Add(callsign.DownLine);
                }

                foreach (var locId in spokenIds)
                {
                    Assert.That(Loc.TryGetString(locId, out var text) && !string.IsNullOrWhiteSpace(text), $"{locId} should resolve");
                    Assert.That(speaker.Lines.TryGetValue(locId, out var clip), $"{speakerId} should have a clip for {locId}");

                    if (clip is not SoundPathSpecifier pathClip)
                    {
                        Assert.Fail($"{speakerId}'s clip for {locId} should be a single file");
                        continue;
                    }

                    Assert.That(resourceManager.ContentFileExists(pathClip.Path), $"{pathClip.Path} should exist");
                    // One clip may serve two strings only if they say the same: "Fire in the hole" for a frag and for anything else.
                    if (usedPaths.TryGetValue(pathClip.Path.ToString(), out var saidAs))
                        Assert.That(text, Is.EqualTo(saidAs), $"{pathClip.Path} should be the clip of one line only");
                    else
                        usedPaths[pathClip.Path.ToString()] = text!;
                    voicedIds.Add(locId);
                }

                Assert.That(speaker.Lines.Keys.Select(locId => locId.Id), Is.EquivalentTo(voicedIds), $"{speakerId} should voice only strings that are spoken");
            }
        });
    }

    /// <summary>
    ///     A mob is given one speaker and a pitch within the spread when it spawns, a population of them is not all the
    ///         same, and neither changes afterwards.
    /// </summary>
    [Test]
    public async Task TestSpeakerAndPitchAreRolledOnceAndKept()
    {
        var entManager = Server.ResolveDependency<IEntityManager>();
        var voiceSystem = entManager.System<NpcVoiceSystem>();
        var map = await Pair.CreateTestMap();
        var mobs = new List<EntityUid>();
        var rolled = new List<(string? Speaker, float Pitch)>();
        var hasVoiceClips = HasVoiceClips;

        await Server.WaitAssertion(() =>
        {
            for (var i = 0; i < 24; i++)
            {
                var mob = entManager.SpawnAtPosition(VoicedMob, map.GridCoords);
                var voice = entManager.GetComponent<NpcVoiceSetComponent>(mob);
                mobs.Add(mob);
                rolled.Add((voice.Speaker?.Id, voice.Pitch));

                Assert.That(voice.Speaker, Is.Not.Null);
                Assert.That(voice.Pitch, Is.InRange(0.95f, 1.05f));
            }

            Assert.That(rolled.ConvertAll(roll => roll.Speaker), Does.Contain("KsRedactedSpeakerA").And.Contain("KsRedactedSpeakerB"),
                "both speakers should turn up among 24 mobs");
            Assert.That(new HashSet<float>(rolled.ConvertAll(roll => roll.Pitch)).Count, Is.GreaterThan(1), "mobs should not all share a pitch");

            var mob0 = mobs[0];
            Assert.That(voiceSystem.TryPlayLine(mob0, "ks-operative-redacted-line-execution-1"), Is.EqualTo(hasVoiceClips),
                "a voiced string plays its clip, where there are clips");
            Assert.That(voiceSystem.TryPlayLine(mob0, "ks-not-a-line"), Is.False, "a string with no clip plays nothing");
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            for (var i = 0; i < mobs.Count; i++)
            {
                var voice = entManager.GetComponent<NpcVoiceSetComponent>(mobs[i]);
                Assert.That(voice.Speaker?.Id, Is.EqualTo(rolled[i].Speaker), "the speaker should stay for life");
                Assert.That(voice.Pitch, Is.EqualTo(rolled[i].Pitch), "the pitch should stay for life");
            }
        });
    }
    /// <summary>
    ///     A Redacted as the game spawns one, through its random humanoid spawner: it has a speaker, and every line it says
    ///         plays that speaker's clip.
    /// </summary>
    [Test]
    public async Task TestASpawnedRedactedSpeaksAloud()
    {
        var protoManager = Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex<EntityPrototype>(RedactedSpawner))
            Assert.Ignore("the Redacted are in the private _KsModule submodule, which is not present");

        var entManager = Server.ResolveDependency<IEntityManager>();
        var voiceSystem = entManager.System<NpcVoiceSystem>();
        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() => entManager.SpawnAtPosition(RedactedSpawner, map.GridCoords));
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var query = entManager.EntityQueryEnumerator<NpcVoiceSetComponent>();
            Assert.That(query.MoveNext(out var mobUid, out var voice), "the spawner should have spawned a voiced Redacted");
            Assert.That(voice!.Speaker, Is.Not.Null, "it should have a speaker");

            foreach (var lineSetId in SpokenLineSets)
            {
                foreach (var locId in protoManager.Index<LocalizedDatasetPrototype>(lineSetId).Values)
                {
                    Assert.That(voiceSystem.TryPlayLine(mobUid, locId), $"{locId} should play {voice.Speaker}'s clip");
                }
            }

            var audioQuery = entManager.EntityQueryEnumerator<Robust.Shared.Audio.Components.AudioComponent, TransformComponent>();
            var playing = 0;
            while (audioQuery.MoveNext(out _, out var audioComponent, out var transformComponent))
            {
                if (transformComponent.ParentUid == mobUid && $"{audioComponent.FileName}".Contains("/Voice/Redacted/"))
                    playing++;
            }

            Assert.That(playing, Is.GreaterThan(0), "the clips should be playing on the mob");
        });
    }
    /// <summary>
    ///     A line the HTN has a Redacted say, through <c>SpeakOperator</c> as in game, plays its clip as well as going to chat.
    ///         It once went silent: the operator looked the clip up by the line's text rather than its id.
    /// </summary>
    [TestCase("KsOperativeTargetDown")]
    [TestCase("KsOperativeExecution")]
    public async Task TestSpeakingALinePlaysItsClip(string lineSet)
    {
        var protoManager = Server.ResolveDependency<IPrototypeManager>();
        if (!protoManager.HasIndex<EntityPrototype>(RedactedSpawner))
            Assert.Ignore("the Redacted are in the private _KsModule submodule, which is not present");

        var entManager = Server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() => entManager.SpawnAtPosition(RedactedSpawner, map.GridCoords));
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var query = entManager.EntityQueryEnumerator<NpcVoiceSetComponent>();
            Assert.That(query.MoveNext(out var mobUid, out _), "the spawner should have spawned a voiced Redacted");

            var speakOperator = new Content.Server.NPC.HTN.PrimitiveTasks.Operators.SpeakOperator
            {
                Speech = new Content.Server.NPC.HTN.PrimitiveTasks.Operators.SpeakOperator.SpeakOperatorSpeech.LocalizedSetSpeakOperatorSpeech
                {
                    LineSet = lineSet,
                },
            };
            entManager.EntitySysManager.DependencyCollection.InjectDependencies(speakOperator, oneOff: true);
            speakOperator.Initialize(entManager.EntitySysManager);

            var blackboard = new Content.Server.NPC.NPCBlackboard();
            blackboard.SetValue(Content.Server.NPC.NPCBlackboard.Owner, mobUid);
            speakOperator.Update(blackboard, 0f);

            var audioQuery = entManager.EntityQueryEnumerator<Robust.Shared.Audio.Components.AudioComponent, TransformComponent>();
            var playing = 0;
            while (audioQuery.MoveNext(out _, out var audioComponent, out var transformComponent))
            {
                if (transformComponent.ParentUid == mobUid && $"{audioComponent.FileName}".Contains("/Voice/Redacted/"))
                    playing++;
            }

            Assert.That(playing, Is.EqualTo(1), "saying the line should play its clip, once");
        });
    }
}
