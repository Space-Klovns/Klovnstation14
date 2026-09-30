using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.TTS;
using Content.Shared._KS14.CCVar;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using ClientTtsSystem = Content.Client._KS14.TTS.TtsSystem;

namespace Content.IntegrationTests.Tests._KS14.TTS;

/// <summary>
///     Lines spoken in game: rate-limited per speaker, cut to length, and played by clients in either codec.
/// </summary>
[TestOf(typeof(TtsSystem))]
public sealed class KsTtsSpeechTests : GameTest
{
    // Destructive: turning TTS on sets the server's preview manager baking, and it keeps the result.
    public override PoolSettings PoolSettings => new() { Connected = true, Destructive = true };

    private const string Voice = "en_GB-alan-medium";

    private KsFakeTtsEndpoint _endpoint = default!;
    private EntityUid _speakerUid;

    [SetUp]
    public async Task SetUpSpeaker()
    {
        using var vorbisStream = Server.ResolveDependency<IResourceManager>().ContentFileRead(new ResPath("/Audio/Voice/Talk/lizard.ogg"));
        using var vorbisBytes = new System.IO.MemoryStream();
        await vorbisStream.CopyToAsync(vorbisBytes);
        _endpoint = new KsFakeTtsEndpoint(vorbisBytes.ToArray());

        await OverrideCVar(Side.Server, KsCCVars.TtsEndpoint, _endpoint.Url);
        await OverrideCVar(Side.Server, KsCCVars.TtsEnabled, true);

        var map = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            _speakerUid = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var listenerUid = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(map.MapCoords.Position + new Vector2(1f, 0f), map.MapId));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, listenerUid);
        });

        await Pair.RunTicksSync(10);
    }

    [TearDown]
    public void TearDownEndpoint()
    {
        _endpoint.Dispose();
    }

    private Task Say(string text)
        => Server.WaitPost(() => Server.System<TtsSystem>().TrySpeak(_speakerUid, Voice, text));

    /// <summary>
    ///     Ticks and waits, since the endpoint answers in real time off the game thread.
    /// </summary>
    private async Task Settle(int rounds = 20)
    {
        for (var i = 0; i < rounds; i++)
        {
            await Pair.RunTicksSync(2);
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task CooldownLimitsBurstsOfLines()
    {
        await Say("first line");
        await Pair.RunTicksSync(3);
        await Say("second line, too soon");
        await Settle();

        // The cooldown is 0.5 s plus 21 ms per character; a few seconds is well clear of it.
        await Pair.RunTicksSync(Server.Timing.TickRate * 3);
        await Say("third line, later");
        await Settle();

        Assert.Multiple(() =>
        {
            Assert.That(_endpoint.RequestsFor("first line"), Has.Count.EqualTo(1));
            Assert.That(_endpoint.RequestsFor("second line, too soon"), Is.Empty,
                "a line a few ticks after the last one is inside the cooldown");
            Assert.That(_endpoint.RequestsFor("third line, later"), Has.Count.EqualTo(1),
                "and once it has passed, lines are voiced again");
        });
    }

    [Test]
    public async Task LongLinesKeepTheirStart()
    {
        var line = string.Concat(Enumerable.Range(0, 8).Select(i => $"word{i}ten "));
        Assert.That(line, Has.Length.GreaterThan(60));

        await Say(line);
        await Settle();

        Assert.That(_endpoint.Requests.Select(request => request.Text), Does.Contain(line[..50]),
            "a long line is cut to its first 50 characters");
    }

    [Test]
    public async Task ClientsPlayBothCodecs([Values("vorbis", "opus", "transcode")] string codec)
    {
        await OverrideCVar(Side.Server, KsCCVars.TtsCodec, codec);

        var before = 0;
        await Client.WaitPost(() => before = Client.System<ClientTtsSystem>().PlayedClipCount);

        await Say($"a line in {codec}");

        var played = 0;
        for (var round = 0; round < 200 && played == 0; round++)
        {
            await Pair.RunTicksSync(2);
            await Task.Delay(10);
            await Client.WaitPost(() => played = Client.System<ClientTtsSystem>().PlayedClipCount - before);
        }

        Assert.That(played, Is.EqualTo(1), $"the client decoded and played a {codec} clip");
    }
}
