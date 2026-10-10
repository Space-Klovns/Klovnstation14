using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.TTS;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.TTS;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using ClientPreviewManager = Content.Client._KS14.TTS.KsTtsPreviewManager;
using KsTtsPreviewAvailability = Content.Client._KS14.TTS.KsTtsPreviewAvailability;

namespace Content.IntegrationTests.Tests._KS14.TTS;

/// <summary>
///     Voice previews, end to end: the server bakes them from a real HTTP endpoint (<see cref="KsFakeTtsEndpoint"/>)
///         and the pooled client receives, decodes and plays them.
/// </summary>
[TestOf(typeof(KsTtsPreviewManager))]
public sealed class KsTtsPreviewTests : GameTest
{
    // Destructive, not just dirty: the server's preview manager keeps what it baked for the life of the process,
    //      which a cleaned-up pair would carry into the next test. Some tests also reload voice prototypes.
    public override PoolSettings PoolSettings => new() { Connected = true, Destructive = true };

    private const string EliteVoice = "en_US-combatant-elite-medium";

    private KsFakeTtsEndpoint _endpoint = default!;
    private string _previewText = default!;

    private KsTtsPreviewManager ServerPreviews => Server.ResolveDependency<KsTtsPreviewManager>();
    private ClientPreviewManager ClientPreviews => Client.ResolveDependency<ClientPreviewManager>();

    [SetUp]
    public async Task SetUpEndpoint()
    {
        using var vorbisStream = Server.ResolveDependency<IResourceManager>().ContentFileRead(new ResPath("/Audio/Voice/Talk/lizard.ogg"));
        using var vorbisBytes = new System.IO.MemoryStream();
        await vorbisStream.CopyToAsync(vorbisBytes);
        var vorbis = vorbisBytes.ToArray();
        _endpoint = new KsFakeTtsEndpoint(vorbis);
        _previewText = Server.ResolveDependency<ILocalizationManager>().GetString("tts-preview-text");

        await OverrideCVar(Side.Server, KsCCVars.TtsEndpoint, _endpoint.Url);
    }

    [TearDown]
    public void TearDownEndpoint()
    {
        _endpoint.Dispose();
    }

    private List<TtsVoicePrototype> SelectableVoices()
    {
        return Server.ProtoMan.EnumeratePrototypes<TtsVoicePrototype>().Where(voice => voice.Selectable).ToList();
    }

    /// <summary>
    ///     Runs both sides until <paramref name="condition"/> holds. The endpoint answers in real time, off the game
    ///         thread, so this waits as well as ticking.
    /// </summary>
    private async Task RunUntil(Func<bool> condition, string description, int maxRounds = 500)
    {
        for (var round = 0; round < maxRounds; round++)
        {
            var met = false;
            await Client.WaitPost(() => met = condition());
            if (met)
                return;

            await Pair.RunTicksSync(2);
            await Task.Delay(10);
        }

        Assert.Fail($"timed out waiting until {description}");
    }

    private Task RunUntilServerStatus(KsTtsPreviewStatus status)
        => RunUntil(() => ServerPreviews.Status == status && ClientPreviews.Status == status, $"previews are {status}");

    /// <summary>
    ///     Ready says every preview has been sent, not that each can play yet: Opus previews (the default,
    ///         <c>klovn.tts.codec transcode</c>) are still being decoded on the thread pool when they arrive.
    /// </summary>
    private Task RunUntilPlayable(ProtoId<TtsVoicePrototype> voice)
        => RunUntil(() => ClientPreviews.GetAvailability(voice) == KsTtsPreviewAvailability.Available, $"{voice}'s preview can play");

    private async Task<KsTtsPreviewAvailability> ClientAvailability(ProtoId<TtsVoicePrototype>? voice)
    {
        var availability = KsTtsPreviewAvailability.Broken;
        await Client.WaitPost(() => availability = ClientPreviews.GetAvailability(voice));
        return availability;
    }

    [Test]
    public async Task NothingIsBakedWhileTtsIsOff()
    {
        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, false);
        await Pair.RunTicksSync(30);
        await Task.Delay(200);
        await Pair.RunTicksSync(30);

        var voice = SelectableVoices()[0].ID;
        Assert.Multiple(async () =>
        {
            Assert.That(_endpoint.Requests, Is.Empty, "nothing is asked of the endpoint while TTS is off");
            Assert.That(ServerPreviews.Status, Is.EqualTo(KsTtsPreviewStatus.Unavailable));
            Assert.That(await ClientAvailability(voice), Is.EqualTo(KsTtsPreviewAvailability.TtsDisabled),
                "the preview button says TTS is off, rather than that previews are coming");
        });
    }

    [Test]
    public async Task BakesEverySelectableVoiceOnceAndSendsThemToClients()
    {
        // Vorbis, pinned: these clips are available the moment they arrive and only loaded when first played, which
        //      is the path this covers (the Opus tests cover decoding). The default is transcode.
        await OverrideCVar(Side.Server, KsCCVars.TtsCodec, "vorbis");
        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, true);
        await RunUntilServerStatus(KsTtsPreviewStatus.Ready);

        var selectable = SelectableVoices();
        var requests = _endpoint.RequestsFor(_previewText);

        Assert.Multiple(async () =>
        {
            Assert.That(requests.Select(request => request.Voice), Is.EquivalentTo(selectable.Select(voice => voice.Voice)),
                "one request per selectable voice, and none for the others");
            Assert.That(requests.Select(request => request.Voice), Does.Not.Contain(EliteVoice));
            Assert.That(requests.Select(request => request.Format), Is.All.Null, "vorbis sends the request it always has");

            foreach (var voice in selectable)
            {
                Assert.That(await ClientAvailability(voice.ID), Is.EqualTo(KsTtsPreviewAvailability.Available), voice.ID);
            }

            Assert.That(await ClientAvailability(EliteVoice), Is.EqualTo(KsTtsPreviewAvailability.Missing));
            Assert.That(await ClientAvailability(null), Is.EqualTo(KsTtsPreviewAvailability.NoVoice));
        });

        var played = false;
        await Client.WaitPost(() => played = ClientPreviews.TryPlay(selectable[0].ID));
        Assert.That(played, Is.True, "a Vorbis preview is loaded the first time it's played");

        // A client that connects later gets them all, without anything being baked again.
        var clientNetManager = Client.ResolveDependency<IClientNetManager>();
        await Client.WaitPost(() => clientNetManager.ClientDisconnect("testing"));
        await Pair.RunTicksSync(20);

        Assert.That(await ClientAvailability(selectable[0].ID), Is.Not.EqualTo(KsTtsPreviewAvailability.Available),
            "previews are dropped on disconnect");

        Client.SetConnectTarget(Server);
        await Client.WaitPost(() => clientNetManager.ClientConnect(null!, 0, null!));
        await RunUntil(() => ClientPreviews.Status == KsTtsPreviewStatus.Ready, "the reconnected client has the previews");

        Assert.Multiple(async () =>
        {
            foreach (var voice in selectable)
            {
                Assert.That(await ClientAvailability(voice.ID), Is.EqualTo(KsTtsPreviewAvailability.Available), voice.ID);
            }

            Assert.That(_endpoint.RequestsFor(_previewText), Has.Count.EqualTo(selectable.Count), "baked once, not per client");
        });
    }

    [Test]
    public async Task OpusFromTheEndpointIsDecodedByClients()
    {
        await OverrideCVar(Side.Server, KsCCVars.TtsCodec, "opus");
        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, true);
        await RunUntilServerStatus(KsTtsPreviewStatus.Ready);

        var voice = SelectableVoices()[0].ID;

        // Decoded on the thread pool, so it may take a moment after arriving.
        await RunUntil(() => ClientPreviews.GetAvailability(voice) == KsTtsPreviewAvailability.Available, "the Opus preview is decoded");

        var played = false;
        await Client.WaitPost(() => played = ClientPreviews.TryPlay(voice));

        Assert.Multiple(() =>
        {
            Assert.That(_endpoint.RequestsFor(_previewText).Select(request => request.Format), Is.All.EqualTo("opus"));
            Assert.That(ServerPreviews.Previews[voice].Codec, Is.EqualTo(TtsCodec.Opus), "labelled by what the endpoint sent");
            Assert.That(played, Is.True);
        });
    }

    [Test]
    public async Task TranscodingTurnsVorbisIntoOpus()
    {
        await OverrideCVar(Side.Server, KsCCVars.TtsCodec, "transcode");
        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, true);
        await RunUntilServerStatus(KsTtsPreviewStatus.Ready);

        var voice = SelectableVoices()[0].ID;
        var preview = ServerPreviews.Previews[voice];
        await RunUntil(() => ClientPreviews.GetAvailability(voice) == KsTtsPreviewAvailability.Available, "the transcoded preview is decoded");

        Assert.Multiple(() =>
        {
            Assert.That(_endpoint.RequestsFor(_previewText).Select(request => request.Format), Is.All.Null,
                "transcoding asks for the endpoint's default");
            Assert.That(preview.Codec, Is.EqualTo(TtsCodec.Opus));
            Assert.That(KsTtsOpus.Identify(preview.Data), Is.EqualTo(TtsCodec.Opus));
        });
    }

    [Test]
    public async Task FailingEndpointIsRetried()
    {
        ServerPreviews.RetryDelay = TimeSpan.FromSeconds(1);
        _endpoint.Failing = true;

        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, true);
        await RunUntilServerStatus(KsTtsPreviewStatus.Failed);

        var voice = SelectableVoices()[0].ID;
        Assert.Multiple(async () =>
        {
            Assert.That(_endpoint.RequestsFor(_previewText), Has.Count.EqualTo(1), "baking stops at the first failure");
            Assert.That(await ClientAvailability(voice), Is.EqualTo(KsTtsPreviewAvailability.Failed));
        });

        _endpoint.Failing = false;
        await RunUntilServerStatus(KsTtsPreviewStatus.Ready);
        await RunUntilPlayable(voice);
    }

    [Test]
    public async Task ReloadDropsAndRebakesChangedVoices()
    {
        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, true);
        await RunUntilServerStatus(KsTtsPreviewStatus.Ready);

        var voices = SelectableVoices();
        var madeUnselectable = voices[0];
        var repointed = voices[1];
        const string newBackend = "en_US-reloaded-medium";

        await Server.WaitPost(() =>
        {
            var changed = new Dictionary<Type, HashSet<string>>();
            Server.ProtoMan.LoadString($@"
- type: ttsVoice
  id: {madeUnselectable.ID}
  voice: {madeUnselectable.Voice}
  name: {madeUnselectable.Name}
  selectable: false

- type: ttsVoice
  id: {repointed.ID}
  voice: {newBackend}
  name: {repointed.Name}
", overwrite: true, changed);
            Server.ProtoMan.ResolveResults();
            Server.ProtoMan.ReloadPrototypes(changed);
        });

        await RunUntil(() => _endpoint.RequestsFor(_previewText).Any(request => request.Voice == newBackend) &&
                             ServerPreviews.Status == KsTtsPreviewStatus.Ready,
            "the repointed voice is baked again");
        await RunUntil(() => ClientPreviews.GetAvailability(repointed.ID) == KsTtsPreviewAvailability.Available,
            "the client has the new preview");

        Assert.Multiple(async () =>
        {
            Assert.That(ServerPreviews.Previews.ContainsKey(madeUnselectable.ID), Is.False, "an unselectable voice loses its preview");
            Assert.That(await ClientAvailability(madeUnselectable.ID), Is.EqualTo(KsTtsPreviewAvailability.Missing),
                "and the client drops it too");
            Assert.That(ServerPreviews.Previews.ContainsKey(repointed.ID), Is.True);
            Assert.That(_endpoint.RequestsFor(_previewText).Count(request => request.Voice == repointed.Voice), Is.EqualTo(1),
                "the old back-end voice isn't asked for again");
        });
    }

    [Test]
    public async Task EditorPreviewButtonFollowsAvailability()
    {
        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, false);
        await Pair.RunTicksSync(5);

        var voice = SelectableVoices()[0].ID;
        Content.Client.Lobby.UI.HumanoidProfileEditor editor = null!;
        Button previewButton = null!;
        OptionButton voiceButton = null!;

        await Client.WaitPost(() =>
        {
            editor = new Content.Client.Lobby.UI.HumanoidProfileEditor(
                Client.ResolveDependency<Content.Client.Lobby.IClientPreferencesManager>(),
                Client.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>(),
                Client.EntMan,
                Client.ResolveDependency<Robust.Client.UserInterface.IFileDialogManager>(),
                Client.ResolveDependency<Robust.Shared.Log.ILogManager>(),
                Client.ResolveDependency<Robust.Client.Player.IPlayerManager>(),
                Client.ProtoMan,
                Client.ResolveDependency<IResourceManager>(),
                Client.ResolveDependency<Content.Client.Players.PlayTimeTracking.JobRequirementsManager>(),
                Client.ResolveDependency<Content.Shared.Humanoid.Markings.MarkingManager>());

            Client.ResolveDependency<Robust.Client.UserInterface.IUserInterfaceManager>().RootControl.AddChild(editor);
            editor.SetProfile(new Content.Shared.Preferences.HumanoidCharacterProfile().WithTtsVoice(voice), slot: 0);

            previewButton = editor.FindControl<Button>("TtsVoicePreviewButton");
            voiceButton = editor.FindControl<OptionButton>("TtsVoiceButton");
        });

        string offTooltip = null;
        var offDisabled = false;
        var listedVoices = new List<ProtoId<TtsVoicePrototype>?>();
        await Client.WaitPost(() =>
        {
            offDisabled = previewButton.Disabled;
            offTooltip = previewButton.ToolTip;
            for (var i = 0; i < voiceButton.ItemCount; i++)
                listedVoices.Add(voiceButton.GetItemMetadata(i) as ProtoId<TtsVoicePrototype>?);
        });

        // Turning TTS on while the editor is open updates the button without reopening it.
        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, true);
        await RunUntilServerStatus(KsTtsPreviewStatus.Ready);
        await RunUntilPlayable(voice);

        var onDisabled = true;
        await Client.WaitPost(() => onDisabled = previewButton.Disabled);

        var loc = Client.ResolveDependency<ILocalizationManager>();

        await Client.WaitPost(() => editor.Orphan());

        Assert.Multiple(() =>
        {
            Assert.That(offDisabled, Is.True, "no previews while TTS is off");
            Assert.That(offTooltip, Is.EqualTo(loc.GetString("humanoid-profile-editor-tts-voice-preview-tts-disabled")),
                "and the tooltip says why");
            Assert.That(onDisabled, Is.False, "once previews arrive, the button works");
            Assert.That(listedVoices[0], Is.Null, "random comes first");
            Assert.That(listedVoices.Skip(1), Is.EquivalentTo(SelectableVoices().Select(voice => (ProtoId<TtsVoicePrototype>?)voice.ID)),
                "every selectable voice, and no others");
        });
    }
}
