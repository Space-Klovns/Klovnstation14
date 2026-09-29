using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Client._KS14.Voice;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Voice;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voice;
using Content.Shared.Administration;
using Content.Shared.Eye;
using Content.Shared.Speech.Muting;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Replays;

using static Content.IntegrationTests.Tests._KS14.Voice.KsVoiceTestSockets;

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

    /// <summary>
    ///     A chunk of <see cref="Speech"/> from the user's page, encoded the way their page connection would.
    /// </summary>
    private static KsVoiceInboundChunk Chunk(NetUserId userId, bool abuseTriggered = false, KsVoiceCodec codec = KsVoiceCodec.Adpcm)
    {
        var samples = Speech();
        var payload = KsVoiceEncoder.Create(codec, opusBitrate: 32000, opusComplexity: 2).Encode(samples);
        return new KsVoiceInboundChunk(userId, codec, payload, StreamStart: false, abuseTriggered);
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
            Server.System<KsVoiceSystem>().HandleChunk(Chunk(_speaker.UserId, abuseTriggered: false)));

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
    public async Task OpusVoiceReachesListeners()
    {
        await Setup();
        await HoldPushToTalk(true);

        var playbackSystem = Client.System<KsVoicePlaybackSystem>();
        var before = playbackSystem.ReceivedFrameCount;
        await Server.WaitPost(() =>
            Server.System<KsVoiceSystem>().HandleChunk(Chunk(_speaker.UserId, codec: KsVoiceCodec.Opus)));
        await Pair.RunTicksSync(5);

        NetEntity speakerNetEntity = default;
        await Server.WaitPost(() => speakerNetEntity = SEntMan.GetNetEntity(_speakerUid));
        var buffered = 0;
        await Client.WaitPost(() => buffered = playbackSystem.GetBufferedSamples(speakerNetEntity));

        Assert.Multiple(() =>
        {
            Assert.That(playbackSystem.ReceivedFrameCount - before, Is.EqualTo(1), "the Opus chunk is relayed");
            // Exactly one chunk: decoded as Opus. Taken for ADPCM, its bytes would still decode, to the wrong length.
            //      One chunk is less than the jitter buffer, so none of it has started playing yet.
            Assert.That(buffered, Is.EqualTo(KsVoiceConstants.MaxChunkSamples), "and decoded as Opus");
        });
    }

    /// <summary>
    ///     A lost packet in the middle of a stream: Opus fills the gap from what came before, ADPCM skips it.
    /// </summary>
    [Test]
    public async Task OpusConcealsLossOnTheClient()
    {
        await Setup();

        var playbackSystem = Client.System<KsVoicePlaybackSystem>();
        int Buffered(KsVoiceCodec codec, NetEntity talker)
        {
            var encoder = KsVoiceEncoder.Create(codec, opusBitrate: 32000, opusComplexity: 2);
            var packets = new byte[7][];
            for (var i = 0; i < packets.Length; i++)
                packets[i] = encoder.Encode(Speech());

            // Sequence 3 never arrives. Three later packets are enough for the jitter buffer to give up on it.
            foreach (var sequence in new ushort[] { 1, 2, 4, 5, 6 })
            {
                playbackSystem.HandleFrame(new KsVoiceFrameMessage
                {
                    Source = talker,
                    Sequence = sequence,
                    Codec = codec,
                    Payload = packets[sequence],
                });
            }

            return playbackSystem.GetBufferedSamples(talker);
        }

        // Talkers this client doesn't know, so their audio waits in the holding list, where nothing plays it; and all
        //      in one post, so no frame update runs between packets either.
        var opus = 0;
        var adpcm = 0;
        await Client.WaitPost(() =>
        {
            opus = Buffered(KsVoiceCodec.Opus, new NetEntity(900001));
            adpcm = Buffered(KsVoiceCodec.Adpcm, new NetEntity(900002));
        });

        Assert.Multiple(() =>
        {
            Assert.That(opus, Is.EqualTo(6 * KsVoiceConstants.MaxChunkSamples), "five packets decoded, one concealed");
            Assert.That(adpcm, Is.EqualTo(5 * KsVoiceConstants.MaxChunkSamples), "five packets decoded, the gap skipped");
        });
    }

    /// <summary>
    ///     A talker's mask muffles their voice the way it muffles their emotes: every chunk played for them asks for the
    ///         mask's effect, and only while it's worn.
    /// </summary>
    [Test]
    public async Task MasksMuffleVoice()
    {
        await Setup();
        await HoldPushToTalk(true);

        var inventorySystem = Server.System<Content.Shared.Inventory.InventorySystem>();
        var emoteAudioEffectSystem = Server.System<Content.Shared._KS14.EmoteAudioEffect.EmoteAudioEffectSystem>();
        var playbackSystem = Client.System<KsVoicePlaybackSystem>();

        // Chunks started while talking, and how many of those carried an effect. Playback is paced by the wall clock
        //      (RealTime), and ticks run faster than it, so this waits in real time until chunks have actually started:
        //      counting ticks alone let a fast machine finish a phase before anything played, which read as no effect.
        async Task<(int Started, int Effected)> EffectedChunks()
        {
            var startedBefore = playbackSystem.StartedChunkCount;
            var effectedBefore = playbackSystem.EffectedChunkCount;
            for (var i = 0; i < 4; i++)
                await Talk();

            for (var round = 0; round < 300 && playbackSystem.StartedChunkCount - startedBefore < 2; round++)
            {
                await Pair.RunTicksSync(1);
                await Task.Delay(10);
            }

            return (playbackSystem.StartedChunkCount - startedBefore, playbackSystem.EffectedChunkCount - effectedBefore);
        }

        var bareFaced = await EffectedChunks();

        EntityUid maskUid = default;
        string maskedEffect = null;
        await Server.WaitPost(() =>
        {
            maskUid = SEntMan.SpawnEntity("ClothingMaskGas", SEntMan.GetComponent<TransformComponent>(_speakerUid).Coordinates);
            inventorySystem.TryEquip(_speakerUid, maskUid, "mask", silent: true, force: true);
            maskedEffect = emoteAudioEffectSystem.GetEffect(_speakerUid, Content.Shared.Chat.Prototypes.EmoteCategory.Vocal)?.Id;
        });
        await Pair.RunTicksSync(5);
        var masked = await EffectedChunks();

        await Server.WaitPost(() => inventorySystem.TryUnequip(_speakerUid, "mask", silent: true, force: true));
        await Pair.RunTicksSync(5);
        var unmasked = await EffectedChunks();

        Assert.Multiple(() =>
        {
            Assert.That(maskedEffect, Is.EqualTo("MuffledMask"), "a worn gas mask answers for its wearer");
            // Each phase must actually have played something, or its effect count proves nothing either way.
            Assert.That(bareFaced.Started, Is.GreaterThan(0), "chunks played bare-faced");
            Assert.That(masked.Started, Is.GreaterThan(0), "chunks played masked");
            Assert.That(unmasked.Started, Is.GreaterThan(0), "chunks played after unmasking");

            Assert.That(bareFaced.Effected, Is.Zero, "nothing on the face, no effect");
            Assert.That(masked.Effected, Is.GreaterThan(0), "a masked talker's voice is played with the mask's effect");
            Assert.That(unmasked.Effected, Is.Zero, "and not once it comes off");
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

        await Server.WaitPost(() => Server.System<KsVoiceSystem>().Mute(_speaker.UserId, duration: null, "test", adminSession: null));
        var muted = await Talk();

        await Server.WaitPost(() => Server.System<KsVoiceSystem>().Unmute(_speaker.UserId, adminSession: null));
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
            Server.System<KsVoiceSystem>().HandleChunk(Chunk(_speaker.UserId, abuseTriggered: true)));

        Assert.That(await Talk(), Is.Zero, "a triggered abuse detector mutes the talker");
    }

    [Test]
    public async Task AutoMutesAreListedAndCanBeLifted()
    {
        await Setup();
        await HoldPushToTalk(true);

        var voiceSystem = Server.System<KsVoiceSystem>();
        await Server.WaitPost(() => voiceSystem.HandleChunk(Chunk(_speaker.UserId, abuseTriggered: true)));

        var autoMutes = new List<(Robust.Shared.Network.NetUserId UserId, TimeSpan Remaining)>();
        var listed = false;
        var muted = false;
        var lifted = false;
        await Server.WaitPost(() =>
        {
            voiceSystem.GetAutoMutes(autoMutes);
            listed = autoMutes.Exists(entry => entry.UserId == _speaker.UserId);
            muted = voiceSystem.IsMuted(_speaker.UserId);
            lifted = voiceSystem.Unmute(_speaker.UserId, adminSession: null);
        });
        var afterUnmute = await Talk();

        Assert.Multiple(() =>
        {
            Assert.That(listed, Is.True, "an auto-mute shows up alongside admin mutes (vcmutes)");
            Assert.That(muted, Is.True, "and counts as muted, so admins get the unmute verb");
            Assert.That(lifted, Is.True, "an admin can lift it (vcunmute, or the verb)");
            Assert.That(afterUnmute, Is.EqualTo(1), "after which the talker is heard again");
        });
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
    public async Task DisconnectingForgetsAHeldKey()
    {
        await Setup();
        await HoldPushToTalk(true);

        // A client that crashes mid-press never sends the release. Its disconnect must stand in for it.
        await Server.WaitPost(() => Server.PlayerMan.SetStatus(_speaker, SessionStatus.Disconnected));

        var stillHeld = true;
        await Server.WaitPost(() => stillHeld = Server.System<KsVoiceSystem>().IsHoldingPushToTalk(_speaker.UserId));

        Assert.That(stillHeld, Is.False, "a player back in the same round must not transmit without holding the key");
    }

    [Test]
    public async Task LinkUsesTheConfiguredPublicPath()
    {
        await Setup();
        await OverrideCVar(Side.Server, KsCCVars.VoicePublicUrl, "https://voice.example.com");
        await OverrideCVar(Side.Server, KsCCVars.VoicePublicPath, "talk");

        KsVoiceLinkEvent received = null;
        var clientSystem = Client.System<KsVoiceClientSystem>();
        void OnLink(KsVoiceLinkEvent args) => received = args;
        clientSystem.LinkReceived += OnLink;

        await Client.WaitPost(() => clientSystem.RequestLink(reset: false));
        await Pair.RunTicksSync(10);
        var configuredUrl = received?.Url;

        // Changed to something that isn't a path: back to the default, not stuck on the last good value.
        await OverrideCVar(Side.Server, KsCCVars.VoicePublicPath, "/talk#x");
        await Client.WaitPost(() => clientSystem.RequestLink(reset: false));
        await Pair.RunTicksSync(10);
        var invalidUrl = received?.Url;

        clientSystem.LinkReceived -= OnLink;

        Assert.Multiple(() =>
        {
            Assert.That(configuredUrl, Does.StartWith("https://voice.example.com/talk/#"),
                "the link follows klovn.voice.public_path, for a proxy that maps it to the page");
            Assert.That(invalidUrl, Does.StartWith("https://voice.example.com/klovn/voice/#"),
                "an invalid public_path falls back to where the page is served");
        });
    }

    [Test]
    public async Task ResetRefusedByTheCooldownSaysSo()
    {
        await Setup();
        await OverrideCVar(Side.Server, KsCCVars.VoicePublicUrl, "https://voice.example.com");

        var received = new List<KsVoiceLinkEvent>();
        var clientSystem = Client.System<KsVoiceClientSystem>();
        clientSystem.LinkReceived += received.Add;

        await Client.WaitPost(() => clientSystem.RequestLink(reset: true));
        await Pair.RunTicksSync(10);
        await Client.WaitPost(() => clientSystem.RequestLink(reset: true));
        await Pair.RunTicksSync(10);

        clientSystem.LinkReceived -= received.Add;

        Assert.That(received, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(received[0].ErrorLocId, Is.Null, "the first reset goes through");
            Assert.That(received[1].Url, Is.EqualTo(received[0].Url), "the second, too soon, leaves the link as it was");
            Assert.That(received[1].ErrorLocId, Is.EqualTo("ks-voice-link-error-reset-cooldown"),
                "and says so, rather than passing the old link off as a new one");
        });
    }

    [Test]
    public async Task UntransmittedAbuseDoesNotMute()
    {
        await Setup();

        // A page left open in a loud room, push-to-talk up: whatever the detector says, nothing was transmitted.
        await Server.WaitPost(() =>
            Server.System<KsVoiceSystem>().HandleChunk(Chunk(_speaker.UserId, abuseTriggered: true)));

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
    public async Task CannotSpeakPopupShowsOncePerKeyPress()
    {
        await Setup();

        // The pooled client talks this time, so it receives the popups. A muted body refuses speech with a popup.
        await Server.WaitPost(() =>
        {
            SEntMan.AddComponent<MutedComponent>(_listenerUid);
            Server.System<KsVoiceSystem>().SetPushToTalk(ServerSession!.UserId, true);
        });
        await Pair.RunTicksSync(5);

        var popups = new MutedPopupCounter(this);
        var relayedBefore = Server.System<KsVoiceSystem>().RelayedChunkCount;
        await TalkAsListener(popups, chunks: 10);
        var firstPress = popups.Count;

        await HoldListenerKey(false);
        await HoldListenerKey(true);
        await TalkAsListener(popups, chunks: 10);
        var secondPress = popups.Count;

        Assert.Multiple(() =>
        {
            Assert.That(Server.System<KsVoiceSystem>().RelayedChunkCount, Is.EqualTo(relayedBefore), "a muted body is never relayed");
            Assert.That(firstPress, Is.EqualTo(1), "a key press held through many chunks gets one popup, not one per chunk");
            Assert.That(secondPress, Is.EqualTo(2), "pressing again is a new attempt, and gets a new popup");
        });
    }

    [Test]
    public async Task CannotSpeakPopupShowsOncePerUtteranceWithVoiceActivation()
    {
        await Setup();
        await SetListenerVoiceActivation(true);
        await Server.WaitPost(() => SEntMan.AddComponent<MutedComponent>(_listenerUid));
        await Pair.RunTicksSync(5);

        var popups = new MutedPopupCounter(this);
        await TalkAsListener(popups, chunks: 10);
        var firstUtterance = popups.Count;

        // No key to release: a pause in the audio longer than a burst gap ends the utterance.
        await Pair.RunTicksSync((int)MathF.Ceiling(1.5f * (float)Server.Timing.TickRate));
        await TalkAsListener(popups, chunks: 10);
        var secondUtterance = popups.Count;

        await SetListenerVoiceActivation(false);

        Assert.Multiple(() =>
        {
            Assert.That(firstUtterance, Is.EqualTo(1), "an utterance of many chunks gets one popup, not one per chunk");
            Assert.That(secondUtterance, Is.EqualTo(2), "speaking again after a pause is a new attempt, and gets a new popup");
        });
    }

    [Test]
    public async Task PageFollowsThePlayerIntoABody()
    {
        await Setup();
        await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession!, null));
        await Pair.RunTicksSync(5);

        string token = null;
        await Server.WaitPost(() => token = Server.ResolveDependency<KsVoiceLinkManager>().ResolveToken(ServerSession!, reset: false));
        await using var page = await CreateSocketPair();
        var run = new KsVoiceUplinkConnection(page.Server, IPAddress.Loopback, Server.ResolveDependency<KsVoiceUplinkManager>())
            .RunAsync(CancellationToken.None);
        await SendText(page.Client, JsonSerializer.Serialize(new { type = "auth", token }));

        // Waiting in the lobby, as it were.
        await Pair.RunTicksSync(5);
        var inLobby = await ReadStateReason(page);

        // Spawning: nobody talks and nobody presses anything, so only the attachment itself can update the page.
        await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession!, _listenerUid));
        await Pair.RunTicksSync(5);
        var inBody = await ReadStateReason(page);

        await page.Client.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await run.WaitAsync(SocketTimeout);

        Assert.Multiple(() =>
        {
            Assert.That(inLobby, Is.EqualTo("no-body"));
            Assert.That(inBody, Is.EqualTo("not-holding-key"), "getting a body updates the page straight away");
        });
    }

    /// <summary>
    ///     The reason in the next "state" message the page receives, skipping anything else.
    /// </summary>
    private static async Task<string> ReadStateReason(SocketPair page)
    {
        while (true)
        {
            using var message = JsonDocument.Parse(await ReadText(page.Client));
            if (message.RootElement.GetProperty("type").GetString() != "state")
                continue;

            return message.RootElement.GetProperty("reason").GetString();
        }
    }

    [Test]
    public async Task AnIdlePageNeverShowsTheCannotSpeakPopup()
    {
        await Setup();
        await SetListenerVoiceActivation(true);
        await Server.WaitPost(() => SEntMan.AddComponent<MutedComponent>(_listenerUid));

        // A real page for the pooled client, since the page's state is only worked out while one is connected.
        string token = null;
        await Server.WaitPost(() => token = Server.ResolveDependency<KsVoiceLinkManager>().ResolveToken(ServerSession!, reset: false));
        await using var page = await CreateSocketPair();
        var run = new KsVoiceUplinkConnection(page.Server, IPAddress.Loopback, Server.ResolveDependency<KsVoiceUplinkManager>())
            .RunAsync(CancellationToken.None);
        await SendText(page.Client, JsonSerializer.Serialize(new { type = "auth", token }));
        await ReadText(page.Client);

        var popups = new MutedPopupCounter(this);
        async Task Idle()
        {
            for (var i = 0; i < 10; i++)
            {
                await Pair.RunTicksSync(1);
                await popups.Observe();
            }
        }

        // Connecting, and the server's switch changing, each bring the page's state up to date without anyone
        //      trying to talk.
        await Idle();
        var idleReason = await ReadStateReason(page);
        await OverrideCVar(Side.Server, KsCCVars.VoiceActivationAllowed, false);
        await OverrideCVar(Side.Server, KsCCVars.VoiceActivationAllowed, true);
        await Idle();
        var idle = popups.Count;

        await TalkAsListener(popups, chunks: 3);
        var talking = popups.Count;

        await page.Client.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await run.WaitAsync(SocketTimeout);
        await SetListenerVoiceActivation(false);

        Assert.Multiple(() =>
        {
            Assert.That(idle, Is.Zero, "a muted player who isn't talking is never told they can't speak by popup");
            Assert.That(idleReason, Is.EqualTo("cannot-speak"), "but the page says so, rather than that they're live");
            Assert.That(talking, Is.EqualTo(1), "trying to talk still gets the popup");
        });
    }

    [Test]
    public async Task VoiceActivationTalksWithoutTheKey()
    {
        await Setup();
        var voiceSystem = Server.System<KsVoiceSystem>();

        async Task<bool> Relayed()
        {
            var before = voiceSystem.RelayedChunkCount;
            await Server.WaitPost(() =>
                voiceSystem.HandleChunk(Chunk(ServerSession!.UserId, abuseTriggered: false)));
            await Pair.RunTicksSync(2);
            return voiceSystem.RelayedChunkCount > before;
        }

        var withoutKey = await Relayed();

        // The client's own setting, replicated to the server like any client cvar.
        await SetListenerVoiceActivation(true);
        var voiceActivated = await Relayed();

        await OverrideCVar(Side.Server, KsCCVars.VoiceActivationAllowed, false);
        await Pair.RunTicksSync(5);
        var forbidden = await Relayed();

        await SetListenerVoiceActivation(false);

        Assert.Multiple(() =>
        {
            Assert.That(withoutKey, Is.False, "push-to-talk is still the default");
            Assert.That(voiceActivated, Is.True, "with voice activation on, audio is relayed without holding the key");
            Assert.That(forbidden, Is.False, "a server that doesn't allow voice activation still needs the key");
        });
    }

    [Test]
    public async Task RelayedVoiceIsRecordedInReplays()
    {
        // Nobody in range: a replay is watched from anywhere, so what nobody heard live is recorded all the same.
        await Setup(listenerDistance: 40f);
        await OverrideCVar(Side.Server, CVars.ReplayServerRecordingEnabled, true);
        await HoldPushToTalk(true);

        var voiceSystem = Server.System<KsVoiceSystem>();
        var recordingManager = Server.ResolveDependency<IReplayRecordingManager>();

        async Task<int> RecordedByTalking()
        {
            var before = voiceSystem.RecordedChunkCount;
            await Talk();
            return voiceSystem.RecordedChunkCount - before;
        }

        var notRecording = await RecordedByTalking();

        var started = false;
        await Server.WaitPost(() =>
            started = recordingManager.TryStartRecording(Server.ResolveDependency<IResourceManager>().UserData, name: "ks-voice-replay-test", overwrite: true));
        var recording = await RecordedByTalking();

        await OverrideCVar(Side.Server, KsCCVars.VoiceRecordInReplays, false);
        var switchedOff = await RecordedByTalking();

        // Writing the replay serializes what was recorded, which is where an event that can't be sent would fail.
        await Server.WaitPost(() => recordingManager.StopRecording());
        await recordingManager.WaitWriteTasks();

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.True, "the test could start a recording");
            Assert.That(notRecording, Is.Zero, "nothing is recorded while no replay is");
            Assert.That(recording, Is.EqualTo(1), "a relayed chunk goes into the replay, even with nobody in range");
            Assert.That(switchedOff, Is.Zero, "klovn.voice.record_in_replays turns it off");
        });
    }

    [Test]
    public async Task ReplayedVoicePlays()
    {
        await Setup();

        NetEntity speakerNetEntity = default;
        await Server.WaitPost(() => speakerNetEntity = SEntMan.GetNetEntity(_speakerUid));
        var samples = Speech();
        var encoder = new KsVoiceAdpcm.EncoderState();
        var payload = new byte[KsVoiceAdpcm.EncodedSize(samples.Length)];
        KsVoiceAdpcm.Encode(ref encoder, samples, payload);

        var playbackSystem = Client.System<KsVoicePlaybackSystem>();
        var receivedBefore = playbackSystem.ReceivedFrameCount;

        // What a replay does with a recorded event: raise it as if it had come over the network.
        await Client.WaitPost(() => Client.ResolveDependency<IClientEntityManager>()
            .DispatchReceivedNetworkMsg(new KsVoiceReplayFrameEvent(speakerNetEntity, 1, KsVoiceCodec.Adpcm, true, payload)));
        await Pair.RunTicksSync(2);

        var received = playbackSystem.ReceivedFrameCount - receivedBefore;
        var buffered = -1;
        await Client.WaitPost(() => buffered = playbackSystem.GetBufferedSamples(speakerNetEntity));

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo(1), "the replayed chunk reaches playback");
            Assert.That(buffered, Is.EqualTo(KsVoiceConstants.MaxChunkSamples), "decoded, whole, for its talker");
        });
    }

    private async Task SetListenerVoiceActivation(bool enabled)
    {
        await Client.WaitPost(() => Client.CfgMan.SetCVar(KsCCVars.VoiceActivation, enabled));
        await Pair.RunTicksSync(5);
    }

    /// <summary>
    ///     Counts "you can't speak" popups shown to the pooled client. A label lasts about a second and folds repeats of
    ///         itself into a counter, so it has to be looked at after every tick, adding up what appears.
    /// </summary>
    private sealed class MutedPopupCounter(KsVoiceRelayTests test)
    {
        private readonly string _mutedText = test.Client.ResolveDependency<ILocalizationManager>().GetString("speech-muted");
        private int _lastSeen;

        public int Count { get; private set; }

        public async Task Observe()
        {
            await test.Client.WaitPost(() =>
            {
                var seen = 0;
                foreach (var label in test.Client.System<Content.Client.Popups.PopupSystem>().WorldLabels)
                {
                    // A repeated popup's text gains a count suffix, so match on the message rather than equality.
                    if (label.Text.Contains(_mutedText))
                        seen += label.Repeats;
                }

                if (seen > _lastSeen)
                    Count += seen - _lastSeen;

                _lastSeen = seen;
            });
        }
    }

    /// <summary>
    ///     The pooled client's page sends a run of chunks, one a tick, then goes quiet for a few ticks.
    /// </summary>
    private async Task TalkAsListener(MutedPopupCounter popups, int chunks)
    {
        for (var i = 0; i < chunks; i++)
        {
            await Server.WaitPost(() =>
                Server.System<KsVoiceSystem>().HandleChunk(Chunk(ServerSession!.UserId, abuseTriggered: false)));
            await Pair.RunTicksSync(1);
            await popups.Observe();
        }

        for (var i = 0; i < 5; i++)
        {
            await Pair.RunTicksSync(1);
            await popups.Observe();
        }
    }

    private async Task HoldListenerKey(bool held)
    {
        await Server.WaitPost(() => Server.System<KsVoiceSystem>().SetPushToTalk(ServerSession!.UserId, held));
        await Pair.RunTicksSync(2);
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

    private static byte[] EncodedSpeech()
    {
        var samples = Speech();
        var state = new KsVoiceAdpcm.EncoderState();
        var payload = new byte[KsVoiceAdpcm.EncodedSize(samples.Length)];
        KsVoiceAdpcm.Encode(ref state, samples, payload);
        return payload;
    }

    [Test]
    public async Task AudioMovesOntoItsTalkerWhenTheEntityArrives()
    {
        await Setup();

        // A talker the server has just spawned: the client won't know the entity until the next state arrives.
        var lateNetEntity = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var lateUid = SEntMan.SpawnEntity("MobHuman", _map.MapCoords);
            lateNetEntity = SEntMan.GetNetEntity(lateUid);
        });

        var playback = Client.System<KsVoicePlaybackSystem>();
        var knownBefore = true;
        var waitingBefore = false;
        await Client.WaitPost(() =>
        {
            knownBefore = Client.EntMan.TryGetEntity(lateNetEntity, out _);
            playback.HandleFrame(new KsVoiceFrameMessage { Source = lateNetEntity, Sequence = 0, Payload = EncodedSpeech() });
            waitingBefore = playback.IsWaitingForEntity(lateNetEntity);
        });

        await Pair.RunTicksSync(5);

        var waitingAfter = true;
        var onEntity = false;
        await Client.WaitPost(() =>
        {
            waitingAfter = playback.IsWaitingForEntity(lateNetEntity);
            onEntity = Client.EntMan.TryGetEntity(lateNetEntity, out var lateClientUid) &&
                       Client.EntMan.HasComponent<KsVoicePlaybackComponent>(lateClientUid.Value) &&
                       playback.GetBufferedSamples(lateNetEntity) >= 0;
        });

        Assert.Multiple(() =>
        {
            Assert.That(knownBefore, Is.False, "the test needs audio to arrive before the entity");
            Assert.That(waitingBefore, Is.True, "until then it waits, with nowhere else to go");
            Assert.That(waitingAfter, Is.False, "once the entity arrives it stops waiting");
            Assert.That(onEntity, Is.True, "and its audio is on the entity");
        });
    }

    [Test]
    public async Task LocalMuteLivesOnTheTalkerAndOutlastsTheirAudio()
    {
        await Setup();
        await HoldPushToTalk(true);

        var playback = Client.System<KsVoicePlaybackSystem>();
        var clientSpeakerUid = ToClientUid(_speakerUid);
        await Client.WaitPost(() => playback.SetLocallyMuted(clientSpeakerUid, true));
        await Talk();

        var heardWhileMuted = false;
        await Client.WaitPost(() => heardWhileMuted = playback.GetBufferedSamples(Client.EntMan.GetNetEntity(clientSpeakerUid)) >= 0);

        // Let the talker's audio time out. The timeout is in real time, so wait it out rather than run ticks.
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        await Pair.RunTicksSync(3);

        var audioGone = false;
        var stillMuted = false;
        await Client.WaitPost(() =>
        {
            audioGone = playback.GetBufferedSamples(Client.EntMan.GetNetEntity(clientSpeakerUid)) < 0;
            stillMuted = playback.IsLocallyMuted(clientSpeakerUid);
        });

        var removedOnUnmute = false;
        await Client.WaitPost(() =>
        {
            playback.SetLocallyMuted(clientSpeakerUid, false);
            removedOnUnmute = !Client.EntMan.HasComponent<KsVoicePlaybackComponent>(clientSpeakerUid);
        });

        Assert.Multiple(() =>
        {
            Assert.That(heardWhileMuted, Is.True, "a muted talker's audio still arrives (it just plays silently)");
            Assert.That(audioGone, Is.True, "their audio is dropped once they stop talking");
            Assert.That(stillMuted, Is.True, "but the mute stays on them");
            Assert.That(removedOnUnmute, Is.True, "unmuting a silent talker leaves nothing behind");
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
