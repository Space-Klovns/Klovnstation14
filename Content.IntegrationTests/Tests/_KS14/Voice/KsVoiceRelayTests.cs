using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Voice;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Voice;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voice;
using Content.Shared.Administration;
using Content.Shared.Eye;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._KS14.Voice;

/// <summary>
///     Server relay of voice to nearby players, end to end over the real net channel: a dummy session talks (chunks
///         injected as if from its microphone page) and the pooled client listens.
/// </summary>
[TestOf(typeof(KsVoiceSystem))]
public sealed class KsVoiceRelayTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private TestMapData _map = default!;
    private ICommonSession _speaker = default!;
    private EntityUid _speakerUid;
    private EntityUid _listenerUid;

    private async Task Setup(bool enabled = true, float listenerDistance = 2f)
    {
        await OverrideCVar(Side.Server, KsCCVars.VoiceEnabled, enabled);
        await OverrideCVar(Side.Server, KsCCVars.VoiceAdminLogBursts, false);

        _map = await Pair.CreateTestMap();
        _speaker = await Server.AddDummySession();

        await Server.WaitPost(() =>
        {
            _speakerUid = SEntMan.SpawnEntity("MobHuman", _map.MapCoords);
            _listenerUid = SEntMan.SpawnEntity("MobHuman",
                new MapCoordinates(_map.MapCoords.Position + new Vector2(listenerDistance, 0f), _map.MapId));

            Server.PlayerMan.SetAttachedEntity(_speaker, _speakerUid);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, _listenerUid);
        });

        await Pair.RunTicksSync(10);
    }

    private static short[] Speech()
    {
        var samples = new short[KsVoiceConstants.MaxChunkSamples];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(6000f * MathF.Sin(2f * MathF.PI * 220f * (float)i / (float)KsVoiceConstants.SampleRate));

        return samples;
    }

    /// <summary>
    ///     Makes the dummy speaker "say" one chunk, then returns how many voice frames the client received as a result.
    /// </summary>
    private async Task<int> Talk()
    {
        var before = Client.System<KsVoicePlaybackSystem>().ReceivedFrameCount;

        await Server.WaitPost(() =>
            Server.System<KsVoiceSystem>().HandleChunk(new KsVoiceInboundChunk(_speaker.UserId, Speech(), AbuseTriggered: false)));

        await Pair.RunTicksSync(5);
        return Client.System<KsVoicePlaybackSystem>().ReceivedFrameCount - before;
    }

    private async Task HoldPushToTalk(bool held)
    {
        await Server.WaitPost(() => Server.System<KsVoiceSystem>().SetPushToTalk(_speaker.UserId, held));
    }

    [Test]
    public async Task NearbyListenerHearsOnlyWhileKeyIsHeld()
    {
        await Setup();

        var withoutKey = await Talk();
        await HoldPushToTalk(true);
        var withKey = await Talk();
        await HoldPushToTalk(false);
        var afterRelease = await Talk();

        Assert.Multiple(() =>
        {
            Assert.That(withoutKey, Is.Zero, "nothing is relayed without push-to-talk");
            Assert.That(withKey, Is.EqualTo(1), "a nearby listener receives the chunk");
            Assert.That(afterRelease, Is.Zero, "releasing the key stops relay");
        });
    }

    [Test]
    public async Task FarListenerHearsNothing()
    {
        await Setup(listenerDistance: 40f);
        await HoldPushToTalk(true);

        var relayedBefore = Server.System<KsVoiceSystem>().RelayedChunkCount;
        var received = await Talk();

        Assert.Multiple(() =>
        {
            Assert.That(Server.System<KsVoiceSystem>().RelayedChunkCount, Is.EqualTo(relayedBefore + 1), "the talker was allowed to talk");
            Assert.That(received, Is.Zero, "but nobody was in range");
        });
    }

    [Test]
    public async Task DisabledVoiceRelaysNothing()
    {
        await Setup(enabled: false);
        await HoldPushToTalk(true);

        Assert.That(await Talk(), Is.Zero);
    }

    [Test]
    public async Task AdminMuteBlocksAndUnmuteRestores()
    {
        await Setup();
        await HoldPushToTalk(true);

        await Server.WaitPost(() => Server.System<KsVoiceSystem>().Mute(_speaker.UserId, duration: null, "test", admin: null));
        var muted = await Talk();

        await Server.WaitPost(() => Server.System<KsVoiceSystem>().Unmute(_speaker.UserId, admin: null));
        var unmuted = await Talk();

        Assert.Multiple(() =>
        {
            Assert.That(muted, Is.Zero);
            Assert.That(unmuted, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SpeechBlockersSilenceVoice()
    {
        await Setup();
        await HoldPushToTalk(true);

        // The same freeze-and-mute admins use on speech must also silence voice, via SpeakAttemptEvent.
        await Server.WaitPost(() => Server.System<AdminFrozenSystem>().FreezeAndMute(_speakerUid));

        Assert.That(await Talk(), Is.Zero);
    }

    [Test]
    public async Task AbuseTriggersAutoMute()
    {
        await Setup();
        await HoldPushToTalk(true);

        await Server.WaitPost(() =>
            Server.System<KsVoiceSystem>().HandleChunk(new KsVoiceInboundChunk(_speaker.UserId, Speech(), AbuseTriggered: true)));

        Assert.That(await Talk(), Is.Zero, "a triggered abuse detector mutes the talker");
    }

    [Test]
    public async Task ClientControlEventsReachTheServerAndBack()
    {
        await Setup();
        await OverrideCVar(Side.Server, KsCCVars.VoicePublicUrl, "https://voice.example.com");

        KsVoiceLinkEvent received = null;
        var clientSystem = Client.System<KsVoiceClientSystem>();
        clientSystem.LinkReceived += args => received = args;

        await Client.WaitPost(() =>
        {
            clientSystem.SetPushToTalk(true);
            clientSystem.RequestLink(reset: false);
        });
        await Pair.RunTicksSync(10);

        var holding = false;
        await Server.WaitPost(() => holding = Server.System<KsVoiceSystem>().IsHoldingPushToTalk(ServerSession!.UserId));

        await Client.WaitPost(() => clientSystem.SetPushToTalk(false));
        await Pair.RunTicksSync(10);

        var released = true;
        await Server.WaitPost(() => released = !Server.System<KsVoiceSystem>().IsHoldingPushToTalk(ServerSession!.UserId));

        Assert.Multiple(() =>
        {
            Assert.That(holding, Is.True, "push-to-talk must reach the server");
            Assert.That(released, Is.True, "and so must its release");
            Assert.That(received?.Url, Does.StartWith("https://voice.example.com/klovn/voice/#"), "the link must come back to the client");
            Assert.That(received?.ErrorLocId, Is.Null);
        });
    }

    [Test]
    public async Task UntransmittedAbuseDoesNotMute()
    {
        await Setup();

        // A page left open in a loud room, push-to-talk up: whatever the detector says, nothing was transmitted.
        await Server.WaitPost(() =>
            Server.System<KsVoiceSystem>().HandleChunk(new KsVoiceInboundChunk(_speaker.UserId, Speech(), AbuseTriggered: true)));

        await HoldPushToTalk(true);
        Assert.That(await Talk(), Is.EqualTo(1), "audio that was never relayed must not earn a mute");
    }

    [Test]
    public async Task PushToTalkSurvivesChangingBodies()
    {
        await Setup();
        await HoldPushToTalk(true);

        await Server.WaitPost(() =>
        {
            var newBodyUid = SEntMan.SpawnEntity("MobHuman", _map.MapCoords);
            Server.PlayerMan.SetAttachedEntity(_speaker, newBodyUid);
        });
        await Pair.RunTicksSync(5);

        Assert.That(await Talk(), Is.EqualTo(1), "the key is still held; the client won't re-send it");
    }

    [Test]
    public async Task GhostsCannotTalk()
    {
        await Setup();
        await HoldPushToTalk(true);

        await Server.WaitPost(() =>
        {
            var ghostUid = SEntMan.SpawnEntity("MobObserver", _map.MapCoords);
            Server.PlayerMan.SetAttachedEntity(_speaker, ghostUid);
        });
        await Pair.RunTicksSync(5);

        // Ghosts are kept silent three ways: GetBlockReason refuses ghost bodies, observers fail CanSpeak (they have
        //      no SpeechComponent), and the ghost layer is invisible to living eyes. The last only filters listeners
        //      and has its own test (VoiceFollowsVisibilityLayers); this pins the transmit side, where either of the
        //      first two stops the voice before it is relayed to anyone.
        var relayedBefore = Server.System<KsVoiceSystem>().RelayedChunkCount;
        var heard = await Talk();

        Assert.Multiple(() =>
        {
            Assert.That(heard, Is.Zero, "ghosts are never heard by the living");
            Assert.That(Server.System<KsVoiceSystem>().RelayedChunkCount, Is.EqualTo(relayedBefore),
                "a ghost's voice must not be relayed at all");
        });
    }

    [Test]
    public async Task VoiceFollowsVisibilityLayers()
    {
        await Setup();
        await HoldPushToTalk(true);

        // Put the (living) speaker on the ghost layer: living eyes can't see it, so living ears mustn't hear it.
        await Server.WaitPost(() =>
            Server.System<SharedVisibilitySystem>().SetLayer(_speakerUid, (ushort)VisibilityFlags.Ghost));
        await Pair.RunTicksSync(5);
        var livingHeard = await Talk();

        // A ghost listener's eye does see that layer.
        await Server.WaitPost(() =>
        {
            var ghostUid = SEntMan.SpawnEntity("MobObserver", _map.MapCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, ghostUid);
        });
        await Pair.RunTicksSync(10);
        var ghostHeard = await Talk();

        Assert.Multiple(() =>
        {
            Assert.That(livingHeard, Is.Zero, "a living listener must not hear a speaker it can't see");
            Assert.That(ghostHeard, Is.EqualTo(1), "a ghost listener sees the ghost layer, so hears it");
        });
    }

    [Test]
    public async Task AudioForAnUnknownSpeakerIsKept()
    {
        await Setup();

        // Voice packets and entity state travel separately, so a talker's first audio can arrive before their entity
        //      does. That audio must wait for the entity, not be thrown away (which cut off the start of what people
        //      said as they came into view).
        var unknownSpeaker = new NetEntity(int.MaxValue - 1);
        var samples = Speech();
        var state = new KsVoiceAdpcm.EncoderState();
        var payload = new byte[KsVoiceAdpcm.EncodedSize(samples.Length)];
        KsVoiceAdpcm.Encode(ref state, samples, payload);

        var playback = Client.System<KsVoicePlaybackSystem>();
        var known = true;
        await Client.WaitPost(() =>
        {
            known = Client.EntMan.TryGetEntity(unknownSpeaker, out _);
            playback.HandleFrame(new KsVoiceFrameMessage { Source = unknownSpeaker, Sequence = 0, Payload = payload });
        });
        await Pair.RunTicksSync(5);

        var buffered = playback.GetBufferedSamples(unknownSpeaker);

        Assert.Multiple(() =>
        {
            Assert.That(known, Is.False, "the test needs a speaker the client doesn't know");
            Assert.That(buffered, Is.EqualTo(samples.Length), "its audio waits for it rather than being dropped");
        });
    }

    [Test]
    public async Task TalkingIndicatorShowsAndClears()
    {
        await Setup();
        await HoldPushToTalk(true);
        await Talk();

        var clientSpeakerUid = ToClientUid(_speakerUid);
        bool TalkingOnClient()
        {
            var appearance = Client.System<SharedAppearanceSystem>();
            return appearance.TryGetData<bool>(clientSpeakerUid, KsVoiceVisuals.Talking, out var talking) && talking;
        }

        var shownOnClient = false;
        await Client.WaitPost(() => shownOnClient = TalkingOnClient());

        await Pair.RunSeconds(1f);

        var clearedOnClient = true;
        await Client.WaitPost(() => clearedOnClient = !TalkingOnClient());

        Assert.Multiple(() =>
        {
            Assert.That(SEntMan.HasComponent<KsVoiceIndicatorComponent>(_speakerUid), Is.True);
            Assert.That(shownOnClient, Is.True, "listeners see the talking indicator");
            Assert.That(clearedOnClient, Is.True, "and it clears once the talker stops");
        });
    }
}
