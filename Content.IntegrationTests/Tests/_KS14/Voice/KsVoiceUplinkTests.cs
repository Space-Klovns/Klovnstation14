using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Voice;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Voice;
using Robust.Shared.Network;
using static Content.IntegrationTests.Tests._KS14.Voice.KsVoiceTestSockets;

namespace Content.IntegrationTests.Tests._KS14.Voice;

/// <summary>
///     The voice link and microphone websocket, which together are the only thing standing between "has the link" and
///         "can talk as that player". Sockets are real websocket framing over a loopback TCP pair, skipping only the
///         HTTP upgrade (the status host's job).
/// </summary>
[TestOf(typeof(KsVoiceUplinkConnection))]
public sealed class KsVoiceUplinkTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    /// <summary>
    ///     Host that resolves tokens through the real <see cref="KsVoiceLinkManager"/> and records what it's handed.
    /// </summary>
    private sealed class RecordingHost(KsVoiceLinkManager linkManager) : IKsVoiceUplinkHost
    {
        public readonly ConcurrentQueue<KsVoiceInboundChunk> Chunks = new();
        public int AuthFailures;

        public KsVoiceProcessorSettings ProcessorSettings => new(-6f, -9f, 0.05f, 3f);

        public volatile KsVoiceCodec Codec = KsVoiceCodec.Adpcm;

        public KsVoiceEncoderSettings EncoderSettings => new(Codec, OpusBitrate: 32000, OpusComplexity: 2);

        public float UplinkRateFactor => 1.25f;

        public bool TryAuthenticate(string token, out NetUserId userId, out string userName)
        {
            var resolved = linkManager.TryResolve(token, out userId, out var name);
            userName = name ?? string.Empty;
            return resolved;
        }

        public void OnAuthenticationFailed(IPAddress remoteAddress)
            => Interlocked.Increment(ref AuthFailures);

        public void OnAuthenticated(KsVoiceUplinkConnection connection)
        {
        }

        public void OnChunk(KsVoiceUplinkConnection connection, KsVoiceInboundChunk chunk)
            => Chunks.Enqueue(chunk);

        public void OnClosed(KsVoiceUplinkConnection connection)
        {
        }
    }

    private async Task<string> IssueToken(bool reset = false)
    {
        var token = string.Empty;
        await Server.WaitPost(() =>
            token = Server.ResolveDependency<KsVoiceLinkManager>().ResolveToken(ServerSession!, reset));
        return token;
    }

    [Test]
    public async Task PageIsServedInTheGamesLanguage()
    {
        var uplinkManager = Server.ResolveDependency<KsVoiceUplinkManager>();
        var gameCulture = Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>().DefaultCulture?.Name;

        Assert.That(uplinkManager.TryGetStaticFile(KsVoiceLinkManager.PagePath, out var page, out var contentType), Is.True);
        var html = Encoding.UTF8.GetString(page!);

        Assert.Multiple(() =>
        {
            // en-US, not the markup's own "en": only the server putting the game's culture in can make this pass.
            Assert.That(gameCulture, Is.EqualTo("en-US"), "the culture ContentLocalizationManager loads");
            Assert.That(html, Does.Contain($"<html lang=\"{gameCulture}\">"), "the page carries the game's language");
            Assert.That(contentType, Does.StartWith("text/html"));
        });
    }

    [Test]
    public async Task IssuedLinkCarriesTokenOnlyInTheFragment()
    {
        await OverrideCVar(Side.Server, KsCCVars.VoicePublicUrl, "https://voice.example.com");
        var token = await IssueToken();

        var url = string.Empty;
        var again = string.Empty;
        await Server.WaitPost(() =>
        {
            var linkManager = Server.ResolveDependency<KsVoiceLinkManager>();
            url = linkManager.BuildUrl(token)!;
            again = linkManager.ResolveToken(ServerSession!, reset: false);
        });

        var uri = new Uri(url);
        Assert.Multiple(() =>
        {
            Assert.That(uri.GetLeftPart(UriPartial.Query), Is.EqualTo("https://voice.example.com/klovn/voice/"));
            Assert.That(uri.PathAndQuery, Does.Not.Contain(token), "the token must never be in the path or query");
            Assert.That(uri.Fragment, Is.EqualTo("#" + token));
            Assert.That(token, Has.Length.GreaterThanOrEqualTo(43), "256 bits, base64url");
            Assert.That(again, Is.EqualTo(token), "asking again without reset returns the same link");
        });
    }

    [Test]
    public async Task WrongTokenIsRejected()
    {
        await IssueToken();
        var host = new RecordingHost(Server.ResolveDependency<KsVoiceLinkManager>());
        await using var pair = await CreateSocketPair();

        var connection = new KsVoiceUplinkConnection(pair.Server, IPAddress.Loopback, host);
        var run = connection.RunAsync(CancellationToken.None);

        await SendText(pair.Client, JsonSerializer.Serialize(new { type = "auth", token = "not-the-token" }));
        var (_, close) = await ReadUntilClosed(pair.Client);
        await run.WaitAsync(SocketTimeout);

        Assert.Multiple(() =>
        {
            Assert.That(close, Is.EqualTo("auth-failed"));
            Assert.That(connection.Authenticated, Is.False);
            Assert.That(host.AuthFailures, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AudioBeforeAuthenticationIsRejected()
    {
        await IssueToken();
        var host = new RecordingHost(Server.ResolveDependency<KsVoiceLinkManager>());
        await using var pair = await CreateSocketPair();

        var connection = new KsVoiceUplinkConnection(pair.Server, IPAddress.Loopback, host);
        var run = connection.RunAsync(CancellationToken.None);

        await SendAudio(pair.Client, frames: 1);
        var (_, close) = await ReadUntilClosed(pair.Client);
        await run.WaitAsync(SocketTimeout);

        Assert.Multiple(() =>
        {
            Assert.That(close, Is.EqualTo("auth-failed"));
            Assert.That(host.Chunks, Is.Empty);
        });
    }

    [Test]
    public async Task ValidTokenAttributesAudioToItsOwner()
    {
        var token = await IssueToken();
        var host = new RecordingHost(Server.ResolveDependency<KsVoiceLinkManager>());
        await using var pair = await CreateSocketPair();

        // As if the main thread were relaying this page, so its audio is encoded.
        var connection = new KsVoiceUplinkConnection(pair.Server, IPAddress.Loopback, host) { Transmitting = true };
        var run = connection.RunAsync(CancellationToken.None);

        await SendText(pair.Client, JsonSerializer.Serialize(new { type = "auth", token }));
        var hello = JsonDocument.Parse(await ReadText(pair.Client)).RootElement;

        await SendAudio(pair.Client, frames: KsVoiceConstants.MaxFramesPerUplinkMessage);
        await SendText(pair.Client, JsonSerializer.Serialize(new { type = "ping" }));
        await SendAudio(pair.Client, frames: 1, sequence: 1);

        using var timeout = new CancellationTokenSource(SocketTimeout);
        while (host.Chunks.Count < 2)
            await Task.Delay(10, timeout.Token);

        await pair.Client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await run.WaitAsync(SocketTimeout);

        var chunks = host.Chunks.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(hello.GetProperty("type").GetString(), Is.EqualTo("hello"));
            Assert.That(hello.GetProperty("name").GetString(), Is.EqualTo(ServerSession!.Name));
            Assert.That(chunks, Has.Length.EqualTo(2));
            Assert.That(chunks.Select(c => c.UserId), Is.All.EqualTo(ServerSession!.UserId));
            Assert.That(KsVoiceAdpcm.Decode(chunks[0].Payload, new short[KsVoiceConstants.MaxChunkSamples]),
                Is.EqualTo(KsVoiceConstants.MaxChunkSamples), "the whole message, encoded");
        });
    }

    [Test]
    [TestOf(typeof(KsVoiceEncoderSettings))]
    public async Task AudioIsEncodedWithTheConfiguredCodec()
    {
        var token = await IssueToken();
        var host = new RecordingHost(Server.ResolveDependency<KsVoiceLinkManager>()) { Codec = KsVoiceCodec.Opus };
        await using var pair = await CreateSocketPair();

        var connection = new KsVoiceUplinkConnection(pair.Server, IPAddress.Loopback, host) { Transmitting = true };
        var run = connection.RunAsync(CancellationToken.None);

        await SendText(pair.Client, JsonSerializer.Serialize(new { type = "auth", token }));
        await ReadText(pair.Client);

        var sequence = (ushort)0;
        async Task<KsVoiceInboundChunk> Next()
        {
            var before = host.Chunks.Count;
            await SendAudio(pair.Client, frames: KsVoiceConstants.MaxFramesPerUplinkMessage, sequence: sequence++);
            using var timeout = new CancellationTokenSource(SocketTimeout);
            while (host.Chunks.Count <= before)
                await Task.Delay(10, timeout.Token);

            return host.Chunks.ToArray()[before];
        }

        var opusFirst = await Next();
        var opusSecond = await Next();

        host.Codec = KsVoiceCodec.Adpcm; // the server's klovn.voice.codec changes mid-stream
        var adpcm = await Next();

        connection.Transmitting = false; // push-to-talk released, say
        var idle = await Next();
        connection.Transmitting = true;
        var resumed = await Next();

        await pair.Client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await run.WaitAsync(SocketTimeout);

        var scratch = new short[KsVoiceOpus.MaxConcealSamples];
        Assert.Multiple(() =>
        {
            Assert.That(opusFirst.Codec, Is.EqualTo(KsVoiceCodec.Opus));
            Assert.That(new KsVoiceOpusDecoder().Decode(opusFirst.Payload, scratch), Is.EqualTo(KsVoiceConstants.MaxChunkSamples),
                "a real Opus packet for the whole chunk");
            Assert.That(opusFirst.StreamStart, Is.True, "a new encoder starts a stream");
            Assert.That(opusSecond.StreamStart, Is.False, "and carries on with it");

            Assert.That(adpcm.Codec, Is.EqualTo(KsVoiceCodec.Adpcm), "a codec change takes effect from the next chunk");
            Assert.That(adpcm.StreamStart, Is.True, "with a new stream");
            Assert.That(KsVoiceAdpcm.Decode(adpcm.Payload, scratch), Is.EqualTo(KsVoiceConstants.MaxChunkSamples));

            Assert.That(idle.Payload, Is.Empty, "audio that won't be relayed isn't encoded");
            Assert.That(resumed.StreamStart, Is.True, "and transmitting again starts a new stream, so decoders restart with it");
        });
    }

    [Test]
    public async Task ResetTokenNoLongerAuthenticates()
    {
        var oldToken = await IssueToken();
        var newToken = await IssueToken(reset: true);
        var linkManager = Server.ResolveDependency<KsVoiceLinkManager>();

        Assert.Multiple(() =>
        {
            Assert.That(newToken, Is.Not.EqualTo(oldToken));
            Assert.That(linkManager.TryResolve(oldToken, out _, out _), Is.False, "a reset must invalidate the old link");
            Assert.That(linkManager.TryResolve(newToken, out var userId, out _), Is.True);
            Assert.That(userId, Is.EqualTo(ServerSession!.UserId));
        });

        await Server.WaitPost(() => linkManager.Revoke(ServerSession!.UserId));
        Assert.That(linkManager.TryResolve(newToken, out _, out _), Is.False, "revoking (e.g. on disconnect) must invalidate the link");
    }

    [Test]
    public async Task MalformedAudioClosesTheSocket()
    {
        var token = await IssueToken();
        var host = new RecordingHost(Server.ResolveDependency<KsVoiceLinkManager>());
        await using var pair = await CreateSocketPair();

        var connection = new KsVoiceUplinkConnection(pair.Server, IPAddress.Loopback, host);
        var run = connection.RunAsync(CancellationToken.None);

        await SendText(pair.Client, JsonSerializer.Serialize(new { type = "auth", token }));
        await ReadText(pair.Client);

        // Half a frame: not a whole number of 20 ms frames.
        var bad = new byte[KsVoiceConstants.UplinkHeaderBytes + KsVoiceConstants.FrameSamples];
        bad[0] = KsVoiceConstants.UplinkProtocolVersion;
        await pair.Client.SendAsync(bad, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);

        var (_, close) = await ReadUntilClosed(pair.Client);
        await run.WaitAsync(SocketTimeout);

        Assert.Multiple(() =>
        {
            Assert.That(close, Is.EqualTo("bad-audio"));
            Assert.That(host.Chunks, Is.Empty);
        });
    }

    [Test]
    public async Task OversizedMessageClosesTheSocket()
    {
        var token = await IssueToken();
        var host = new RecordingHost(Server.ResolveDependency<KsVoiceLinkManager>());
        await using var pair = await CreateSocketPair();

        var connection = new KsVoiceUplinkConnection(pair.Server, IPAddress.Loopback, host);
        var run = connection.RunAsync(CancellationToken.None);

        await SendText(pair.Client, JsonSerializer.Serialize(new { type = "auth", token }));
        await ReadText(pair.Client);

        await SendAudio(pair.Client, frames: KsVoiceConstants.MaxFramesPerUplinkMessage + 1);
        var (_, close) = await ReadUntilClosed(pair.Client);
        await run.WaitAsync(SocketTimeout);

        Assert.Multiple(() =>
        {
            Assert.That(close, Is.EqualTo("too-big"));
            Assert.That(host.Chunks, Is.Empty);
        });
    }

    [Test]
    public async Task FasterThanRealTimeIsCutOff()
    {
        var token = await IssueToken();
        var host = new RecordingHost(Server.ResolveDependency<KsVoiceLinkManager>());
        await using var pair = await CreateSocketPair();

        var connection = new KsVoiceUplinkConnection(pair.Server, IPAddress.Loopback, host);
        var run = connection.RunAsync(CancellationToken.None);

        await SendText(pair.Client, JsonSerializer.Serialize(new { type = "auth", token }));
        await ReadText(pair.Client);

        // Two seconds of audio as fast as possible, against a burst allowance of well under one.
        var closed = ReadUntilClosed(pair.Client);
        try
        {
            for (var i = 0; i < 34; i++)
                await SendAudio(pair.Client, frames: KsVoiceConstants.MaxFramesPerUplinkMessage, sequence: (ushort)i);
        }
        catch (WebSocketException)
        {
            // The server may hang up mid-flood.
        }

        var (_, close) = await closed;
        await run.WaitAsync(SocketTimeout);

        Assert.That(close, Is.EqualTo("rate"));
    }

    [Test]
    [TestOf(typeof(KsVoiceUplinkManager))]
    public async Task SecondPageForTheSameUserReplacesTheFirst()
    {
        await OverrideCVar(Side.Server, KsCCVars.VoiceEnabled, true);
        var token = await IssueToken();
        var manager = Server.ResolveDependency<KsVoiceUplinkManager>();

        await using var first = await CreateSocketPair();
        await using var second = await CreateSocketPair();

        var firstRun = new KsVoiceUplinkConnection(first.Server, IPAddress.Loopback, manager).RunAsync(CancellationToken.None);
        await SendText(first.Client, JsonSerializer.Serialize(new { type = "auth", token }));
        await ReadText(first.Client);

        var secondRun = new KsVoiceUplinkConnection(second.Server, IPAddress.Loopback, manager).RunAsync(CancellationToken.None);
        await SendText(second.Client, JsonSerializer.Serialize(new { type = "auth", token }));
        await ReadText(second.Client);

        var (messages, close) = await ReadUntilClosed(first.Client);
        await firstRun.WaitAsync(SocketTimeout);

        Assert.Multiple(() =>
        {
            Assert.That(close, Is.EqualTo("replaced"));
            Assert.That(messages, Has.Some.Contains("\"replaced\""));
            Assert.That(manager.IsConnected(ServerSession!.UserId), Is.True, "the second page stays connected");
        });

        await second.Client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await secondRun.WaitAsync(SocketTimeout);
    }

    [Test]
    [TestOf(typeof(KsVoiceUplinkManager))]
    public async Task DisabledVoiceRefusesPages()
    {
        await OverrideCVar(Side.Server, KsCCVars.VoiceEnabled, false);
        var token = await IssueToken();
        var manager = Server.ResolveDependency<KsVoiceUplinkManager>();

        await using var pair = await CreateSocketPair();
        var run = new KsVoiceUplinkConnection(pair.Server, IPAddress.Loopback, manager).RunAsync(CancellationToken.None);
        await SendText(pair.Client, JsonSerializer.Serialize(new { type = "auth", token }));

        var (_, close) = await ReadUntilClosed(pair.Client);
        await run.WaitAsync(SocketTimeout);

        Assert.That(close, Is.EqualTo("auth-failed"), "with voice disabled even a valid link must not authenticate");
    }
}
